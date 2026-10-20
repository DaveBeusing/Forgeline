using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.World;

internal static class TerrainProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };
    private static readonly PropertyInfo? WorkProperty = typeof(TerrainRenderer).GetProperty("WorkMetrics");
    public static void Main(string[] args)
    {
        if (args.Length != 3 || args[0] is not ("cpu" or "native")) throw new ArgumentException("cpu|native output.json runtime-root");
        string output = Path.GetFullPath(args[1]), assets = Path.GetFullPath(args[2]);
        if (File.Exists(output)) throw new IOException("Evidence is immutable; use a fresh output.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        object results = args[0] == "cpu" ? Cpu() : Native(output, assets);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Mode = args[0], SourceVersion = typeof(TerrainRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Environment.ProcessorCount,
            AssetManifestSha256 = Hash(File.ReadAllBytes(Path.Combine(assets, "manifest.json"))),
            ProbeSha256 = Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)),
            RendererSha256 = Hash(File.ReadAllBytes(typeof(TerrainRenderer).Assembly.Location)), Results = results
        }, JsonOptions), Encoding.UTF8);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static object? Work(TerrainRenderer renderer) => WorkProperty?.GetValue(renderer);

    // Compile this unchanged source against each revision. Optional parameters are resolved
    // by name to preserve the baseline constructor contract without a product compatibility shim.
    private static TerrainRenderer Create(IGraphicsDevice device, TerrainWorld world, int cap, RuntimeAssetCatalog? assets = null)
    {
        var ctor = typeof(TerrainRenderer).GetConstructors().Single();
        object?[] arguments = ctor.GetParameters().Select(p => p.Name switch
        {
            "graphics" => (object)device, "world" => world, "residency" => new TerrainResidencySettings(true, cap),
            "runtimeAssets" => assets, _ => p.DefaultValue
        }).ToArray();
        var renderer = (TerrainRenderer)ctor.Invoke(arguments); renderer.DebugChunksEnabled = false; return renderer;
    }

    private static object Cpu()
    {
        using var device = new NullDevice();
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 4);
        var camera = new RtsCamera(new() { InitialDistance = 12, MaximumDistance = 2000 });
        var context = new Counter();
        long start = Stopwatch.GetTimestamp(); using var renderer = Create(device, world, 4);
        double constructionMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var points = Enumerable.Range(-2, 5).SelectMany(z => Enumerable.Range(-2, 5).Select(x => new Vector2(x * 256 + 128, z * 256 + 128))).ToArray();
        void Move(int i)
        {
            Vector2 p = points[i % points.Length]; world.TrySampleHeight(p.X, p.Y, out float h);
            camera.ApplyState(camera.CaptureState() with { Target = new(p.X, h, p.Y) });
        }
        Move(0); start = Stopwatch.GetTimestamp(); renderer.Prepare(camera, context.Width, context.Height);
        double firstPrepareMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        renderer.Render(context, camera);
        object first = new { Diagnostics = renderer.LastDiagnostics, Work = Work(renderer), device.UploadCalls, device.UploadBytes };
        var results = new List<object>();
        results.Add(Measure("stable-prepare", 128, 1024, _ => renderer.Prepare(camera, context.Width, context.Height)));
        results.Add(Measure("whole-map-teleport-prepare", 128, 512, i => { Move(i); renderer.Prepare(camera, context.Width, context.Height); }));
        renderer.Render(context, camera);
        object afterTraverse = new { Diagnostics = renderer.LastDiagnostics, Work = Work(renderer), device.UploadCalls, device.UploadBytes };
        results.Add(Measure("warm-main-record", 128, 1024, _ => renderer.Render(context, camera)));
        var plateau = new List<object>();
        for (int lap = 0; lap < 5; lap++)
        {
            for (int i = 0; i < points.Length; i++) { Move(i); renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera); }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            plateau.Add(new { Lap = lap, ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
                Diagnostics = renderer.LastDiagnostics, Work = Work(renderer) });
        }
        return new { Backend = "Null graphics; owner CPU only; GPU timings unavailable", GpuMilliseconds = (double?)null,
            ConstructionMilliseconds = constructionMs, FirstPrepareMilliseconds = firstPrepareMs, First = first,
            AfterTraverse = afterTraverse, Results = results, Plateau = plateau, Frequency = Stopwatch.Frequency,
            Method = "Immutable representative 81-chunk world; 25 target teleports, 12m camera, cap four; unchanged probe against each product. CPU includes managed fallback upload; no hardware inference. Optional CPU work is active in candidate." };
    }

    private static object Measure(string name, int warmups, int samples, Action<int> operation)
    {
        for (int i = 0; i < warmups; i++) operation(i);
        var ticks = new long[samples]; var allocations = new long[samples];
        for (int i = 0; i < samples; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); operation(i);
            ticks[i] = Stopwatch.GetTimestamp() - start; allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        return new { Name = name, Warmups = warmups, Samples = samples, RawTicks = ticks, RawAllocatedBytes = allocations };
    }

    private static object Native(string output, string assets)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Terrain Measurement", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableDebugLayer = false, EnableVSync = false, AllowSoftwareAdapterFallback = false });
        if (graphics.Diagnostics.Device.IsSoftwareAdapter) throw new InvalidOperationException("Hardware adapter required.");
        graphics.ConfigureDirectionalShadows(new(DirectionalShadowQuality.High));
        graphics.ConfigureSceneOutput(new(true, SceneLightingSettings.Default.Exposure, true));
        var world = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var catalog = RuntimeAssetCatalog.Load(assets);
        using var renderer = Create(graphics, world, 128, catalog);
        var camera = new RtsCamera(new() { MaximumDistance = 2000 });
        var cases = new List<object>();
        var poses = new[] { ("ridge-road", new Vector2(1536, 1536)), ("bridge", new Vector2(1536, 920)), ("industrial", new Vector2(650, 520)) };
        foreach (var pose in poses)
            foreach (float distance in new[] { 120f, 420, 1000 })
                foreach (bool moving in new[] { false, true })
                {
                    void Update(int frame)
                    {
                        Vector2 target = pose.Item2 + (moving ? new Vector2(MathF.Sin(frame / 24f) * 48, MathF.Cos(frame / 24f) * 48) : Vector2.Zero);
                        world.TrySampleHeight(target.X, target.Y, out float h);
                        camera.ApplyState(camera.CaptureState() with { Target = new(target.X, h, target.Y), Distance = distance });
                    }
                    GraphicsDirectionalShadowFrame shadow = default;
                    Action<IGraphicsCommandContext> record = context =>
                    {
                        context.BeginDirectionalShadowPass(shadow); renderer.RenderShadow(context, shadow, 4096); context.EndDirectionalShadowPass();
                        renderer.Render(context, camera); context.BeginPass(GraphicsFramePass.Overlay);
                    };
                    for (int i = 0; i < 128; i++)
                    {
                        Update(i); shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                        renderer.Prepare(camera, 1280, 720, shadow); graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                    }
                    var cpuTicks = new long[256]; var prepareTicks = new long[256]; var allocated = new long[256];
                    var completed = new List<object>(); ulong lastFence = 0;
                    for (int i = 0; i < cpuTicks.Length; i++)
                    {
                        Update(i); shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                        renderer.Prepare(camera, 1280, 720, shadow); prepareTicks[i] = Stopwatch.GetTimestamp() - start;
                        start = Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, record);
                        cpuTicks[i] = Stopwatch.GetTimestamp() - start; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                        var diagnostics = graphics.Diagnostics;
                        if (diagnostics.Frame is { } frame && frame.GpuSubmissionFence > lastFence)
                        {
                            lastFence = frame.GpuSubmissionFence;
                            completed.Add(new { Fence = lastFence, TotalMilliseconds = diagnostics.GpuFrameMilliseconds,
                                WorldMilliseconds = frame.WorldGpuMilliseconds, ShadowMilliseconds = diagnostics.DirectionalShadows?.GpuMilliseconds });
                        }
                    }
                    graphics.WaitForIdle(); Update(0);
                    shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, graphics.DirectionalShadows);
                    renderer.Prepare(camera, 1280, 720, shadow);
                    byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record);
                    bool equal = pixels.SequenceEqual(graphics.CaptureFrame(GraphicsColor.ForgeLineClear, record));
                    if (!equal) throw new InvalidOperationException("Repeated native image differs.");
                    if (graphics.Diagnostics.Health.SubmissionFaultCount != 0 || graphics.Diagnostics.Resources.TextureBindingFailureCount != 0)
                        throw new InvalidOperationException("Native submission or texture binding fault.");
                    string image = Path.GetFileNameWithoutExtension(output) + $"-{pose.Item1}-{distance}-{moving}.ppm";
                    using (var file = File.Create(Path.Combine(Path.GetDirectoryName(output)!, image)))
                    {
                        file.Write(Encoding.ASCII.GetBytes("P6\n1280 720\n255\n"));
                        for (int i = 0; i < pixels.Length; i += 4) file.Write(pixels.AsSpan(i, 3));
                    }
                    cases.Add(new { Site = pose.Item1, Distance = distance, Moving = moving, Warmups = 128, Samples = 256,
                        CpuTicks = cpuTicks, PrepareTicks = prepareTicks, RawAllocatedBytes = allocated, CompletedGpuFrames = completed,
                        Diagnostics = renderer.LastDiagnostics, Work = Work(renderer), Resources = graphics.Diagnostics.Resources,
                        Health = graphics.Diagnostics.Health,
                        ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
                        RepeatPixelsEqual = equal, Image = image, RgbaSha256 = Hash(pixels) });
                }
        return new { graphics.Diagnostics.Device, graphics.Diagnostics.Surface, Driver = Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            Frequency = Stopwatch.Frequency, Cases = cases,
            Method = "Hardware D3D12; compiled canonical 144-chunk terrain; High shadows; 1280x720 ACES; 120/420/1000m; fixed and deterministic pan. Unique completed GPU fences. CPU frame wall time includes Present/reuse; Prepare is separate. No object/UI draws. No automatic image approval." };
    }

    private sealed class NullDevice : IGraphicsDevice
    {
        internal long UploadCalls, UploadBytes;
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription d) => new Pipeline(d);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription d) => new Buffer(d, this);
        public IGraphicsTexture CreateTexture(GraphicsTextureData d) => new Texture(d.Description);
        public void RenderFrame(GraphicsColor c, Action<IGraphicsCommandContext>? record = null) => record?.Invoke(new Counter());
        public void Resize(int w, int h) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription d) : IGraphicsPipeline { public GraphicsPipelineDescription Description => d; public void Dispose() { } }
    private sealed class Texture(GraphicsTextureDescription d) : IGraphicsTexture { public GraphicsTextureDescription Description => d; public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription d, NullDevice owner) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => d;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged
        { owner.UploadCalls++; owner.UploadBytes += System.Runtime.InteropServices.MemoryMarshal.AsBytes(data).Length; }
        public void Dispose() { }
    }
    private sealed class Counter : IGraphicsCommandContext
    {
        public int Width => 1600; public int Height => 900; public int FrameIndex => 0;
        public void SetViewport(float x, float y, float w, float h) { }
        public void SetScissor(int l, int t, int r, int b) { }
        public void SetPipeline(IGraphicsPipeline p) { }
        public void SetVertexBuffer(IGraphicsBuffer b, int s, int offsetInBytes = 0, int inputSlot = 0) { }
        public void SetIndexBuffer(IGraphicsBuffer b, GraphicsIndexFormat f, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> v) { }
        public void SetPixelTexture(int s, IGraphicsTexture t) { }
        public void Draw(int c, int startVertex = 0) { }
        public void DrawIndexed(int c, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int c, int n, int startIndex = 0, int baseVertex = 0, int startInstance = 0) { }
    }
}
