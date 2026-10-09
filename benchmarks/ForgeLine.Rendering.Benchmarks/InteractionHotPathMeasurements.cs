using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using static ForgeLine.Rendering.Benchmarks.PresentationBenchmarks;

namespace ForgeLine.Rendering.Benchmarks;

internal static class InteractionHotPathMeasurements
{
    private const int Warmup = 128;
    private const int Samples = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string output)
    {
        var results = new List<Measurement>();
        foreach (var profile in new[] { (1024, 768, 96u), (1600, 900, 120u), (3440, 1440, 144u), (3840, 2160, 192u) })
        foreach (int count in new[] { 0, 1, 1000 })
        foreach (float distance in new[] { 12f, 90f, 260f })
        {
            using var fixture = new Fixture(profile.Item1, profile.Item2, profile.Item3, count, distance);
            results.Add(Measure(fixture));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Backend = "CPU null graphics; includes HUD geometry, terrain-sampled rings, feedback and submission; excludes GPU uploads, waits and present",
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            BuildVersion = typeof(GameplayHudRenderer).Assembly.GetName().Version?.ToString(),
            Warmup, Samples,
            TimingPolicy = "Report percentiles; compare on the same machine. Allocation and bounded geometry are hard gates; timing is observational.",
            Results = results
        }, JsonOptions));
        if (results.Any(result => result.AllocatedBytes != 0 || result.DroppedLines != 0 ||
            result.DrawCalls > 1 || result.HudVertices <= 0 || result.LineCount != result.SelectedCount * 24 + 22))
            throw new InvalidOperationException("Interaction hot paths violated allocation, geometry or draw budgets; inspect the report.");
        Console.WriteLine($"Interaction hot-path measurements: {Path.GetFullPath(output)} ({results.Count} cases; zero allocation and no dropped lines)");
    }

    private static Measurement Measure(Fixture fixture)
    {
        for (int i = 0; i < Warmup; i++) fixture.Frame();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var times = new double[Samples];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            fixture.Frame();
            times[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        int[] collections = [GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2];
        Array.Sort(times);
        var lines = fixture.Lines.LastDiagnostics;
        return new Measurement(fixture.Context.Width, fixture.Context.Height, fixture.Dpi,
            fixture.Instances.Length, fixture.Camera.Distance, bytes, (double)bytes / Samples,
            times[Samples / 2], times[(int)(Samples * .95)], times[(int)(Samples * .99)], times[^1],
            collections, fixture.Hud.LastRenderedVertexCount, fixture.Draw.Lines.Length, lines.DroppedLines, lines.DrawCalls);
    }

    private sealed record Measurement(int Width, int Height, uint Dpi, int SelectedCount, float Distance,
        long AllocatedBytes, double BytesPerFrame, double P50Microseconds, double P95Microseconds,
        double P99Microseconds, double MaximumMicroseconds, int[] GenCollections,
        int HudVertices, int LineCount, int DroppedLines, int DrawCalls);

    private sealed class Fixture : IDisposable
    {
        private readonly FlatTerrain _terrain = new();
        private readonly RuntimeMetricsSampler _metrics = new();
        private readonly RtsCommandFeedback _feedback = new();
        private readonly PresentationSnapshot[] _snapshots;
        private readonly RtsInformationLayerView _view;
        private ulong _frame;
        public uint Dpi { get; }
        public NullGraphicsCommandContext Context { get; }
        public RtsCamera Camera { get; }
        public RenderInstance[] Instances { get; }
        public GameplayHudRenderer Hud { get; }
        public DebugDrawRenderer Lines { get; }
        public DebugDraw Draw { get; } = new() { Enabled = true };

        public Fixture(int width, int height, uint dpi, int count, float distance)
        {
            Dpi = dpi;
            Context = new NullGraphicsCommandContext { Width = width, Height = height };
            var graphics = new NullGraphicsDevice();
            Hud = new GameplayHudRenderer(graphics);
            Lines = new DebugDrawRenderer(graphics, depthEnabled: false, lineWidthPixels: 3);
            Camera = new RtsCamera(new RtsCameraSettings { InitialDistance = distance, EdgeScrollEnabled = false });
            Instances = new RenderInstance[count];
            var selected = new EntityId[count];
            for (int i = 0; i < count; i++)
            {
                selected[i] = new EntityId((uint)i + 1, 1);
                Instances[i] = new RenderInstance(selected[i],
                    new RenderTransform(new Vector3(i % 32 - 16, 0, i / 32 - 16), Quaternion.Identity, Vector3.One),
                    new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, (uint)i + 1,
                    new SelectablePresentationMetadata(new PlayerId(1), ControllableEntityCategory.Unit),
                    UnitFeature: new UnitFeaturePresentationMetadata(UnitIds.MainBattleTank, UnitPresentationDamageState.Intact));
            }
            _snapshots = new PresentationSnapshot[2];
            for (int index = 0; index < _snapshots.Length; index++)
                _snapshots[index] = new PresentationSnapshot(new SimulationTick((ulong)index + 1), TimeSpan.FromMilliseconds(50), count, Instances,
                    sessionId: new SimulationSessionId(1), playerExperience: default(PlayerExperienceSnapshot) with { Player = new PlayerId(1) });
            _view = RtsInformationLayerView.Empty with
            {
                SelectedEntities = selected, IsDragSelecting = true,
                DragStart = new Vector2(width * .4f, height * .4f), DragCurrent = new Vector2(width * .6f, height * .6f)
            };
        }

        public void Frame()
        {
            _frame++;
            var snapshot = _snapshots[_frame % 2];
            var metrics = _metrics.Sample(TimeSpan.FromTicks((long)_frame * 160_000), _frame, _frame / 3,
                snapshot.SessionId, RuntimeSimulationState.Running);
            Draw.Clear();
            foreach (var instance in Instances)
                RtsWorldMarkerVisualization.DrawSelected(Draw, instance, new Vector4(.1f, .8f, 1, .95f), _terrain);
            _feedback.Show(Vector3.Zero, true);
            _feedback.Draw(Draw);
            float scale = GameplayHudLayout.Create(Context.Width, Context.Height, Dpi).Scale;
            Lines.Render(Context, Camera, Draw, scale);
            Hud.Render(Context, Camera, snapshot, _terrain.WorldBounds, _view, default, default,
                FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, Dpi, 1,
                runtimeMetrics: metrics);
        }

        public void Dispose() { Lines.Dispose(); Hud.Dispose(); }
    }

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new Vector3(-1000), new Vector3(1000));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
