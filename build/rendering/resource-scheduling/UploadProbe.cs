using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using Vortice.Direct3D12;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;

if (args.Length is < 2 or > 3 || File.Exists(args[0])) throw new ArgumentException("Supply a fresh output file and expected graphics source SHA.");
if (args.Length == 3 && args[2] == "static-geometry") { MeasureStaticGeometry(args[0], args[1]); return; }
if (args.Length == 3 && args[2] != "texture") throw new ArgumentException("Unknown upload workload.");
DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
Assembly graphicsAssembly = typeof(IGraphicsDevice).Assembly;
string source = graphicsAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
if (!source.Contains(args[1], StringComparison.Ordinal)) throw new InvalidOperationException("Graphics binary source differs from the requested identity: " + source);
using var platform = new WindowsPlatform();
using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Upload Measurement", 320, 240));
using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window,
    new GraphicsConfiguration { AllowSoftwareAdapterFallback = false, EnableVSync = false, EnableDebugLayer = false, EnableMemoryDiagnostics = true });
byte[] pixels = new byte[256 * 256 * 4];
for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 31 + 17);
var texture = new GraphicsTextureData(new(256, 256, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1) { DeferUploadWait = true },
    [new(256, 256, 1024, pixels)]);
for (int i = 0; i < 64; i++) { using IGraphicsTexture warm = graphics.CreateTexture(texture); }
graphics.WaitForIdle();
const int count = 256;
double[] intervals = new double[count];
long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
long started = Stopwatch.GetTimestamp();
for (int i = 0; i < count; i++)
{
    long sample = Stopwatch.GetTimestamp();
    using (IGraphicsTexture upload = graphics.CreateTexture(texture)) { }
    intervals[i] = Stopwatch.GetElapsedTime(sample).TotalMilliseconds;
}
double recordingMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
long waitStarted = Stopwatch.GetTimestamp();
graphics.WaitForIdle();
double drainMilliseconds = Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds;
for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear);
graphics.WaitForIdle();
long frameBytesBefore = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < count; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear);
long frameBytes = GC.GetAllocatedBytesForCurrentThread() - frameBytesBefore;
graphics.WaitForIdle();
GraphicsDiagnostics diagnostics = graphics.Diagnostics;
if (diagnostics.Health.PendingRetirementCount != 0 || diagnostics.Resources.ShaderResourceDescriptorsUsed != 0 || diagnostics.Debug.ErrorCount != 0)
    throw new InvalidOperationException("Resources or debug errors did not retire to the empty-workload plateau.");
double[] rawIntervals = (double[])intervals.Clone();
Array.Sort(intervals);
var arena = graphics.GetType().GetProperty("UploadDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(graphics);
File.WriteAllText(args[0], JsonSerializer.Serialize(new
{
    schemaVersion = 1, startedUtc, completedUtc = DateTimeOffset.UtcNow, source, graphicsSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(graphicsAssembly.Location))),
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    gcMode = System.Runtime.GCSettings.LatencyMode.ToString(),
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    adapter = diagnostics.Device, memory = diagnostics.Memory, workload = "64 warmups, 256 deferred 256x256 RGBA8 uploads disposed immediately; final fence drain; 64 warmups and 256 empty frames",
    samples = count, managedUploadBytes = allocatedBytes, managedEmptyFrameBytes = frameBytes,
    recordingMilliseconds, drainMilliseconds,
    uploadCpu = new { mean = intervals.Average(), p50 = intervals[(int)Math.Ceiling(count * .50) - 1], p95 = intervals[(int)Math.Ceiling(count * .95) - 1], p99 = intervals[(int)Math.Ceiling(count * .99) - 1], raw = rawIntervals, rawSorted = intervals },
    gpuUploadMilliseconds = (double?)null, arena,
    pendingRetirement = diagnostics.Health.PendingRetirementCount,
    liveResources = diagnostics.Health.LiveResourceCount,
    descriptors = diagnostics.Resources.ShaderResourceDescriptorsUsed,
    debugErrors = diagnostics.Debug.ErrorCount
}, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

static void MeasureStaticGeometry(string output, string expectedSource)
{
    DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
    Assembly assembly = typeof(IGraphicsDevice).Assembly;
    string source = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    if (!source.EndsWith("+" + expectedSource, StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected graphics source: " + source);
    string? fallbackProcessStart = Environment.GetEnvironmentVariable("FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK");
    using var platform = new WindowsPlatform();
    using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Static Geometry Measurement", 320, 240));
    using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window,
        new GraphicsConfiguration { AllowSoftwareAdapterFallback = false, EnableVSync = false, EnableDebugLayer = false, EnableMemoryDiagnostics = true });
    const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    var native = (ID3D12Device)graphics.GetType().GetField("_device", privateInstance)!.GetValue(graphics)!;
    FeatureDataArchitecture1 architecture = default;
    bool architectureAvailable = native.CheckFeatureSupport(Feature.Architecture1, ref architecture);
    object Arena() => graphics.GetType().GetProperty("UploadDiagnostics", privateInstance)!.GetValue(graphics)!;
    var compiler = new DxcShaderCompiler();
    using var pipeline = graphics.CreateGraphicsPipeline(new(
        compiler.Compile("float4 VSMain(float3 p:POSITION):SV_Position { return float4(p,1); }", GraphicsShaderStage.Vertex, "VSMain", "StaticProbeVertex.hlsl"),
        compiler.Compile("float4 PSMain():SV_Target { return float4(.25,.5,.75,1); }", GraphicsShaderStage.Pixel, "PSMain", "StaticProbePixel.hlsl"))
        { CullMode = GraphicsCullMode.None, VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)] });
    byte[] vertex = new byte[64 * 1024], index = new byte[16 * 1024];
    MemoryMarshal.AsBytes<float>(new float[] { -1,-1,0,-1,3,0,3,-1,0 }.AsSpan()).CopyTo(vertex);
    MemoryMarshal.AsBytes<ushort>(new ushort[] { 0,1,2 }.AsSpan()).CopyTo(index);
    const int count = 256;
    var retained = new List<IGraphicsBuffer>(count);
    var heaps = new HashSet<string>();
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    object before = Arena();
    double[] rawCreateCpu = new double[count];
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < count; i++)
    {
        byte[] bytes = i % 2 == 0 ? vertex : index;
        long sample = Stopwatch.GetTimestamp();
        var buffer = graphics.CreateInitializedStaticBuffer(new((ulong)bytes.Length,
            i % 2 == 0 ? StaticGraphicsBufferUsage.Vertex : StaticGraphicsBufferUsage.Index), bytes);
        rawCreateCpu[i] = Stopwatch.GetElapsedTime(sample).TotalMilliseconds;
        retained.Add(buffer);
    }
    double creationCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long creationManagedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    object afterCreation = Arena();
    // Native heap properties are independent evidence of Default versus Upload, not just wrapper labels.
    foreach (var buffer in retained)
    {
        var resource = (ID3D12Resource)buffer.GetType().GetProperty("Resource", privateInstance)!.GetValue(buffer)!;
        resource.GetHeapProperties(out var heap, out _).CheckError();
        heaps.Add(heap.Type.ToString());
    }
    void Draw(IGraphicsCommandContext context)
    {
        context.SetPipeline(pipeline); context.SetVertexBuffer(retained[0], 12);
        context.SetIndexBuffer(retained[1], GraphicsIndexFormat.SixteenBit); context.DrawIndexed(3);
    }
    start = Stopwatch.GetTimestamp();
    graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
    double firstDrawCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    object afterFirstDraw = Arena();
    start = Stopwatch.GetTimestamp(); graphics.WaitForIdle();
    double initialDrainCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    byte[] image = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
    int center = (graphics.Diagnostics.Surface.Height / 2 * graphics.Diagnostics.Surface.Width + graphics.Diagnostics.Surface.Width / 2) * 4;
    if (image[center + 1] is < 127 or > 128) throw new InvalidOperationException("Indexed first draw did not produce the expected native pixel.");
    for (int i = 0; i < 64; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
    graphics.WaitForIdle();
    object beforeFrames = Arena();
    double[] rawResidentFrameCpu = new double[count];
    allocated = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < count; i++)
    {
        start = Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
        rawResidentFrameCpu[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    long residentFrameManagedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    graphics.WaitForIdle();
    object afterFrames = Arena();
    var residentHealth = graphics.Diagnostics.Health;
    object beforeStreaming = Arena();
    double[] rawStreamingCreateCpu = new double[count];
    start = Stopwatch.GetTimestamp();
    for (int i = 0; i < count; i++)
    {
        byte[] bytes = i % 2 == 0 ? vertex : index;
        long sample = Stopwatch.GetTimestamp();
        using (var buffer = graphics.CreateInitializedStaticBuffer(new((ulong)bytes.Length,
            i % 2 == 0 ? StaticGraphicsBufferUsage.Vertex : StaticGraphicsBufferUsage.Index), bytes)) { }
        rawStreamingCreateCpu[i] = Stopwatch.GetElapsedTime(sample).TotalMilliseconds;
    }
    double streamingCreationCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    object afterStreamingCreation = Arena();
    start = Stopwatch.GetTimestamp(); graphics.WaitForIdle();
    double streamingDrainCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    object afterStreamingDrain = Arena();
    foreach (var buffer in retained) buffer.Dispose();
    graphics.WaitForIdle();
    var diagnostics = graphics.Diagnostics;
    if (diagnostics.Health.PendingRetirementCount != 0 || diagnostics.Health.LiveResourceCount != 1 || diagnostics.Debug.ErrorCount != 0)
        throw new InvalidOperationException("Static geometry failed to retire to its pipeline-only plateau.");
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        schemaVersion = 2, workload = "static-geometry", startedUtc, completedUtc = DateTimeOffset.UtcNow,
        source, graphicsSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
        runtime = RuntimeInformation.FrameworkDescription, adapter = diagnostics.Device,
        architecture = new { available = architectureAvailable, uma = architectureAvailable ? (bool?)architecture.Uma : null,
            cacheCoherentUma = architectureAvailable ? (bool?)architecture.CacheCoherentUMA : null },
        fallbackProcessStart, nativeHeapTypes = heaps.Order().ToArray(), debugLayer = diagnostics.Debug,
        dredProcessStart = Environment.GetEnvironmentVariable("FORGELINE_DRED"),
        memory = diagnostics.Memory, bufferCount = count, destinationBytes = count / 2 * (vertex.Length + index.Length),
        method = "CPU stopwatch: creation, first indexed submission and fence drain separately; 64 warmup/256 resident frames; then 256 disposed streaming replacements. No GPU-copy time or full-scene FPS attribution.",
        startup = new { before, afterCreation, afterFirstDraw, creationCpuMilliseconds, firstDrawCpuMilliseconds,
            initialDrainCpuMilliseconds, creationManagedBytes, rawCreateCpu },
        resident = new { beforeFrames, afterFrames, rawResidentFrameCpu, residentFrameManagedBytes, health = residentHealth },
        streaming = new { beforeStreaming, afterStreamingCreation, afterStreamingDrain, streamingCreationCpuMilliseconds,
            streamingDrainCpuMilliseconds, rawStreamingCreateCpu },
        imageSha256 = Convert.ToHexString(SHA256.HashData(image)), healthAfterGeometryDisposal = diagnostics.Health,
        gpuCopyMilliseconds = (double?)null, liveObjectReport = "Unavailable: engine tracked-resource plateau only; no DXGI live-object report."
    }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
