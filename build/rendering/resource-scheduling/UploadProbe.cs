using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;

if (args.Length != 2 || File.Exists(args[0])) throw new ArgumentException("Supply a fresh output file and expected graphics source SHA.");
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
