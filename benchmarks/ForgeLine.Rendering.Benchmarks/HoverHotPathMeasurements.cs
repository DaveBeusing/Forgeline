using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using static ForgeLine.Rendering.Benchmarks.PresentationBenchmarks;

namespace ForgeLine.Rendering.Benchmarks;

internal static class HoverHotPathMeasurements
{
    private const int Warmup = 128;
    private const int Samples = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Run(string output)
    {
        var results = new List<Measurement>();
        foreach (var profile in new[] { (1600, 900, 96u), (1600, 900, 144u), (5120, 2160, 144u) })
            foreach (float scale in new[] { .75f, 1f, 2f })
                foreach (int count in new[] { 1, 1000 })
                    foreach (bool dock in new[] { false, true })
                    {
                        using var fixture = new Fixture(profile.Item1, profile.Item2, profile.Item3, scale, count, dock);
                        for (int i = 0; i < Warmup; i++) fixture.Frame();
                        var times = new double[Samples];
                        long start = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 0; i < Samples; i++)
                        {
                            long timestamp = Stopwatch.GetTimestamp();
                            fixture.Frame();
                            times[i] = Stopwatch.GetElapsedTime(timestamp).TotalMicroseconds;
                        }
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
                        Array.Sort(times);
                        results.Add(new(profile.Item1, profile.Item2, profile.Item3, scale, count, dock,
                            bytes, times[Samples / 2], times[(int)(Samples * .95)], times[(int)(Samples * .99)], times[^1],
                fixture.Hud.LastRenderedVertexCount, fixture.Hud.LastHoverTooltipVertexCount));
                    }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Backend = "CPU null graphics; input, resolution and full HUD with active world or dock tooltip; excludes GPU upload, wait and present",
            Runtime = RuntimeInformation.FrameworkDescription,
            BuildVersion = typeof(GameplayHudRenderer).Assembly.GetName().Version?.ToString(),
            Warmup,
            Samples,
            TimingPolicy = "Timing is observational; zero warm allocation and bounded geometry are hard gates.",
            Results = results
        }, JsonOptions));
        if (results.Any(result => result.AllocatedBytes != 0 || result.HudVertices <= 0 || result.HudVertices > 1_000_000 ||
            result.TooltipVertices <= 0 || result.TooltipVertices > 131_072))
            throw new InvalidOperationException("Hover hot paths violated allocation or geometry budgets; inspect the report.");
        Console.WriteLine($"Hover hot paths: {results.Count} cases; zero allocation and bounded geometry; {Path.GetFullPath(output)}");
    }

    private sealed record Measurement(int Width, int Height, uint Dpi, float UiScale, int EntityCount, bool Dock,
        long AllocatedBytes, double P50Microseconds, double P95Microseconds, double P99Microseconds,
        double MaximumMicroseconds, int HudVertices, int TooltipVertices);

    private sealed class Fixture : IDisposable
    {
        private readonly PresentationSnapshot[] _snapshots = new PresentationSnapshot[2];
        private readonly HoverTooltipController _hover = new();
        private readonly InputState _input = new();
        private readonly RtsCamera _camera = new();
        private readonly NullGraphicsCommandContext _context;
        private readonly GameplayHudLayout _layout;
        private readonly PlayerActionPanelView _panel;
        private readonly EntityId _entity;
        private readonly uint _dpi;
        private readonly float _scale;
        private readonly bool _dock;
        private int _frame;
        public GameplayHudRenderer Hud { get; }

        public Fixture(int width, int height, uint dpi, float scale, int count, bool dock)
        {
            _dpi = dpi; _scale = scale; _dock = dock;
            _context = new NullGraphicsCommandContext { Width = width, Height = height };
            _layout = GameplayHudLayout.Create(width, height, dpi, scale);
            _panel = default(PlayerActionPanelView) with { Mode = dock ? PlayerActionPanelMode.Construction : PlayerActionPanelMode.Closed };
            Hud = new GameplayHudRenderer(new NullGraphicsDevice());
            var instances = new RenderInstance[count];
            for (int i = 0; i < count; i++)
                instances[i] = new(new EntityId((uint)i + 1, 1),
                    new RenderTransform(new Vector3(i % 32, 0, i / 32), Quaternion.Identity, Vector3.One),
                    new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                    Selectable: new SelectablePresentationMetadata(new PlayerId(1), ControllableEntityCategory.Unit));
            _entity = instances[^1].Entity; // Search through the whole copied population.
            for (int i = 0; i < _snapshots.Length; i++)
            {
                var tick = new SimulationTick((ulong)i + 1);
                var session = new SimulationSessionId(1);
                var details = PlayerSelectionSummary.Empty with
                {
                    Count = 1,
                    PrimaryEntity = _entity,
                    Kind = PlayerSelectionKind.Unit,
                    DisplayName = "Main Battle Tank",
                    HasHealth = true,
                    HealthFraction = .8,
                    HasSupply = true,
                    FuelFraction = .6,
                    AmmunitionFraction = .4,
                    HasReadiness = true,
                    Readiness = .7
                };
                var actions = new PlayerActionSnapshot(session, tick,
                    [new PlayerConstructionActionReadModel(BuildingIds.Extractor, "Mine / Extractor",
                        [new PlayerActionResourceAmount(ResourceIds.Steel, "Steel", 25, 10)], true)], 0, null, null);
                _snapshots[i] = new(tick, TimeSpan.FromMilliseconds(50), count, instances, sessionId: session,
                    playerExperience: default(PlayerExperienceSnapshot) with { Player = new PlayerId(1) },
                    playerActions: dock ? actions : null,
                    hover: new PlayerHoverSummary(_entity, session, tick, PlayerHoverCategory.Unit, details.DisplayName,
                        RtsUiIcon.UnitArmor, details));
            }
            var card = new Vector2(_layout.ActionDock.X + 20 * _layout.Scale, _layout.ActionDock.Y + 100 * _layout.Scale);
            var position = dock ? card : new Vector2(width / 2, height / 2);
            _input.Apply(PlatformInputEvent.PointerMoved((int)position.X, (int)position.Y));
        }

        public void Frame()
        {
            var snapshot = _snapshots[_frame++ % 2];
            var view = _hover.Update(_input, snapshot, _entity, _camera, _layout, _panel,
                false, _dock, TimeSpan.FromMilliseconds(16));
            if (_frame > Warmup && !view.Ready) throw new InvalidOperationException("Hover fixture is not active.");
            Hud.Render(_context, _camera, snapshot, new AxisAlignedBounds(new Vector3(-1000), new Vector3(1000)),
                RtsInformationLayerView.Empty, _panel, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty,
                default, _dpi, _scale, hoverTooltip: view);
        }

        public void Dispose() => Hud.Dispose();
    }
}
