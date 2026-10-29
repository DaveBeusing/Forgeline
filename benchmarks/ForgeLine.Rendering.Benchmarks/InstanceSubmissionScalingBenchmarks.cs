using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Simulation;

namespace ForgeLine.Rendering.Benchmarks;

/// <summary>CPU submission qualification. The null backend never supplies GPU durations.</summary>
[MemoryDiagnoser]
public class InstanceSubmissionScalingBenchmarks : IDisposable
{
    private PresentationBenchmarks.NullGraphicsDevice _graphics = null!;
    private SimpleInstanceRenderer _renderer = null!;
    private RenderWorld _world = null!;
    private RtsCamera _camera = null!;
    private readonly CountingContext _context = new();
    private int _frame;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Params(1000, 10000, 100000)] public int Roots { get; set; } = 1000;
    [Params(false, true)] public bool LowVisibility { get; set; }
    [Params(false, true)] public bool MixedMechanical { get; set; }
    [Params(1, 3, 7)] public int FramesPerTick { get; set; } = 3;

    [GlobalSetup]
    public void Setup() => Setup(Environment.GetEnvironmentVariable("FORGELINE_RUNTIME_ROOT"));

    private void Setup(string? runtimeRoot)
    {
        var assets = runtimeRoot is null ? PresentationBenchmarks.LoadRuntimeAssets() : RuntimeAssetCatalog.Load(runtimeRoot);
        _graphics = new();
        _renderer = new(_graphics, assets);
        _camera = new(new() { InitialDistance = 420, MaximumDistance = 1200 });
        _world = CreateWorld(Roots, LowVisibility, MixedMechanical);
        _camera.ApplyState(_camera.CaptureState() with { Target = new(650, 0, 520) });
    }

    [Benchmark]
    public InstanceRenderDiagnostics PrepareAndRecord()
    {
        float alpha = Alpha(_frame++);
        _renderer.Prepare(_camera, _world, new(1920, 1080, alpha, MetricsEnabled: true));
        _context.Reset();
        _renderer.Render(_context, _camera, _world, alpha);
        _renderer.Submit();
        _renderer.Retire();
        return _renderer.LastDiagnostics;
    }

    private float Alpha(int frame) => (frame / FramesPerTick % 4) switch { 0 => 0, 1 => .25f, 2 => .5f, _ => 1 };

    /// <summary>Writes chronological CPU samples and distributions without BenchmarkDotNet host changes.</summary>
    public static void RunQualification(string runtimeRoot, string outputPath, string sourceRevision,
        int warmups = 128, int samples = 512)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(warmups, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        var runs = new List<object>();
        foreach (int roots in new[] { 1000, 10000, 100000 })
        foreach (bool low in new[] { false, true })
        foreach (bool mixed in new[] { false, true })
        {
            using var bench = new InstanceSubmissionScalingBenchmarks
            { Roots = roots, LowVisibility = low, MixedMechanical = mixed };
            bench.Setup(runtimeRoot);
            for (int i = 0; i < warmups; i++) bench.PrepareAndRecord();
            var rows = new Sample[samples];
            for (int i = 0; i < samples; i++)
            {
                float alpha = bench.Alpha(bench._frame++);
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                bench._renderer.Prepare(bench._camera, bench._world, new(1920, 1080, alpha, MetricsEnabled: true));
                double prepare = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var prepared = bench._renderer.WorkMetrics;
                long bytes = bench._graphics.UploadedBytes;
                bench._context.Reset();
                start = Stopwatch.GetTimestamp();
                bench._renderer.Render(bench._context, bench._camera, bench._world, alpha);
                double record = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var work = bench._renderer.WorkMetrics;
                var submission = bench._renderer.SubmissionMetrics;
                var reuse = bench._renderer.PreparationReuse;
                bench._renderer.Submit();
                bench._renderer.Retire();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
                rows[i] = new(i, alpha, prepare, record, allocated, bench._context.Draws,
                    work.DrawnRoots, work.DrawnParts, submission.MechanicalPartInstances,
                    bench._graphics.UploadedBytes - bytes, bench._context.InstanceCapacityBytes,
                    work.PipelineBindings, work.TextureBindings, prepared.MeshResolutions, prepared.MaterialResolutions,
                    prepared.Interpolations, work.Interpolations, work.PoseCacheHits, reuse);
            }
            runs.Add(new { roots, lowVisibility = low, mixedMechanical = mixed, framesPerTick = bench.FramesPerTick,
                prepareMilliseconds = Distribution(rows.Select(r => r.PrepareMilliseconds)),
                recordMilliseconds = Distribution(rows.Select(r => r.RecordMilliseconds)),
                allocatedBytes = Distribution(rows.Select(r => (double)r.AllocatedBytes)),
                draws = Distribution(rows.Select(r => (double)r.Draws)),
                uploadedBytes = Distribution(rows.Select(r => (double)r.UploadedBytes)),
                rows });
        }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new { schemaVersion = 1, sourceRevision,
            backend = "CPU/null", gpuMilliseconds = (double?)null, gpuUnavailableReason = "No native command queue or timestamp queries",
            width = 1920, height = 1080, warmups, samples, placementSeed = 2026,
            fixture = "CPU-only planar protocol projection; y=0; mixed vehicles use (4,3,7) scale",
            cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), runtime = Environment.Version.ToString(),
            runs }, JsonOptions));
    }

    private static object Distribution(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray();
        double Percentile(double p) => ordered[(int)Math.Ceiling(p * ordered.Length) - 1];
        return new { mean = ordered.Average(), p50 = Percentile(.5), p95 = Percentile(.95), p99 = Percentile(.99) };
    }

    private sealed record Sample(int Index, float Alpha, double PrepareMilliseconds, double RecordMilliseconds,
        long AllocatedBytes, int Draws, int DrawnRoots, int DrawnParts, int MechanicalParts,
        long UploadedBytes, ulong InstanceCapacityBytes, int PipelineBindings, int TextureBindings,
        int PrepareMeshResolutions, int PrepareMaterialResolutions, int PrepareInterpolations,
        int RecordInterpolations, int RecordPoseCacheHits, RenderPreparationReuseMetrics Reuse);

    private static RenderWorld CreateWorld(int count, bool lowVisibility, bool mixed)
    {
        var previous = new RenderInstance[count];
        var current = new RenderInstance[count];
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        UnitId[] units = [UnitIds.MainBattleTank, UnitIds.ScoutVehicle, UnitIds.CargoTruck, UnitIds.SupplyTruck];
        for (int i = 0; i < count; i++)
        {
            uint h = (uint)i + 2026;
            unchecked { h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16; }
            float x = i % side - (side - 1) * .5f + ((int)(h & 1023) - 512) / 1024f * .01f;
            float z = i / side - (side - 1) * .5f + ((int)((h >> 10) & 1023) - 512) / 1024f * .01f;
            var position = new Vector3(650 + x + (lowVisibility && i % 10 != 0 ? 4000 : 0), 0, 520 + z);
            previous[i] = new(new EntityId((uint)i + 1, 1), new(position, Quaternion.Identity, mixed ? new(4, 3, 7) : new(.1f, .12f, .1f)),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                WorldFeature: mixed ? default : new(WorldVisualId.PropCrate, WorldPresentationKind.Prop, default, false),
                UnitFeature: mixed ? new(units[i % units.Length], UnitPresentationDamageState.Intact, .3f) : default,
                MechanicalPose: mixed ? new(true, 2.3, 1.6, 1.6, 1.55, .15f, 0) : default);
            current[i] = previous[i] with { Transform = previous[i].Transform with
            { Position = position + new Vector3(.02f, 0, .03f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .01f) } };
        }
        var world = new RenderWorld();
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(31), TimeSpan.FromMilliseconds(50), count, previous)); world.Update(buffer);
        buffer.Publish(new(new SimulationTick(32), TimeSpan.FromMilliseconds(50), count, current)); world.Update(buffer);
        return world;
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();
    public void Dispose() { _renderer?.Dispose(); _graphics?.Dispose(); GC.SuppressFinalize(this); }

    private sealed class CountingContext : IGraphicsCommandContext
    {
        public int Width => 1920;
        public int Height => 1080;
        public int FrameIndex => 0;
        public int Draws { get; private set; }
        public ulong InstanceCapacityBytes { get; private set; }
        public void Reset() { Draws = 0; InstanceCapacityBytes = 0; }
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { }
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0)
        { if (inputSlot == 1) InstanceCapacityBytes = buffer.Description.SizeInBytes; }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) => Draws++;
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) => Draws++;
    }
}
