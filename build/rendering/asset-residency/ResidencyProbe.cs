using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.World;

if (args.Length is < 3 or > 4 || File.Exists(args[0])) throw new ArgumentException("Supply a fresh result file, runtime root and expected source SHA.");
string source = typeof(SimpleInstanceRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
if (!source.EndsWith(args[2],StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Frozen presentation source differs from the requested source.");
int cycleCount = args.Length == 4 ? int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture) : 3;
if (cycleCount is < 3 or > 12) throw new ArgumentOutOfRangeException(nameof(args),"Cycle count must be between three and twelve.");
DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
long catalogStart = Stopwatch.GetTimestamp();
var catalog = RuntimeAssetCatalog.LoadCancellable(args[1],CancellationToken.None,requireIntegrity:true);
double catalogMilliseconds = Stopwatch.GetElapsedTime(catalogStart).TotalMilliseconds;
var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.IndustrialBase,terrain,runtimeAssets:catalog);
var snapshots = new PresentationSnapshotBuffer(); snapshots.Publish(scene.Snapshot);
var world = new RenderWorld(); world.Update(snapshots);
using var platform = new WindowsPlatform();
using var window = platform.CreateWindow(new("FORGELINE Residency Probe",320,240));
using var graphics = GraphicsDeviceFactory.CreateForWindow(window,new()
{ EnableVSync=false,AllowSoftwareAdapterFallback=false,EnableDebugLayer=false,EnableMemoryDiagnostics=true });
var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target));
var initial = graphics.Diagnostics;
var cycles = new List<object>();
for (int cycle = 0; cycle < cycleCount; cycle++)
{
    long setupStart = Stopwatch.GetTimestamp();
    long setupAllocations = GC.GetAllocatedBytesForCurrentThread();
    using var ground = new TerrainRenderer(graphics,terrain,runtimeAssets:catalog,residency:new(true,128)) { DebugChunksEnabled=false };
    using var meshes = new SimpleInstanceRenderer(graphics,catalog);
    void Prepare()
    {
        ground.Prepare(camera,320,240);
        if (!meshes.AssetsPrepared) meshes.Prepare(camera,world,new RenderPreparationRequest(320,240,1,PresentationSeconds:1));
    }
    Action<IGraphicsCommandContext> record = context => { ground.Render(context,camera); meshes.Render(context,camera,world,1,presentationSeconds:1); };
    void Frame() { Prepare(); graphics.RenderFrame(GraphicsColor.ForgeLineClear,record); meshes.Submit(); meshes.Retire(); }
    Prepare();
    double setupMilliseconds = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds;
    long setupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()-setupAllocations;
    long drainStart = Stopwatch.GetTimestamp(); graphics.WaitForIdle();
    double drainMilliseconds = Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds;
    object terrainFacade = Field(ground,"_runtimeAssets")!;
    object meshFacade = Field(Field(meshes,"_runtimeAssets")!,"_resources")!;
    object terrainStore = Store(terrainFacade), meshStore = Store(meshFacade);
    string[] commonIds = TextureIds(terrainStore).Intersect(TextureIds(meshStore),StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    bool sameStore = ReferenceEquals(terrainStore,meshStore);
    for (int warmup = 0; warmup < 64; warmup++) Frame();
    graphics.WaitForIdle();
    var before = graphics.Diagnostics;
    object? uploadBefore = UploadArena(graphics);
    object? residencyBefore = Progress(meshFacade);
    long? readsBefore = Reads(residencyBefore);
    var raw = new double[256];
    long allocationStart = GC.GetAllocatedBytesForCurrentThread();
    long recordStart = Stopwatch.GetTimestamp();
    for (int i = 0; i < raw.Length; i++) { long stamp = Stopwatch.GetTimestamp(); Frame(); raw[i]=Stopwatch.GetElapsedTime(stamp).TotalMilliseconds; }
    double recordingMilliseconds = Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread()-allocationStart;
    graphics.WaitForIdle();
    var after = graphics.Diagnostics;
    object? uploadAfter = UploadArena(graphics);
    if (UploadCounter(uploadBefore,"Transfers") != UploadCounter(uploadAfter,"Transfers") || UploadCounter(uploadBefore,"BackpressureWaits") != UploadCounter(uploadAfter,"BackpressureWaits"))
        throw new InvalidOperationException("Warm frames issued new static-buffer transfers or upload backpressure waits.");
    object? residencyAfter = Progress(meshFacade);
    long? readsAfter = Reads(residencyAfter);
    if (before.Resources.TextureUploadCount != after.Resources.TextureUploadCount || before.Resources.LoadedTextureCount != after.Resources.LoadedTextureCount || before.Health.LiveResourceCount != after.Health.LiveResourceCount || readsBefore != readsAfter)
        throw new InvalidOperationException("Warm residency changed file-read, texture-upload or resource counts.");
    Prepare();
    byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear,record); meshes.Submit(); meshes.Retire();
    string pixelSha256 = Convert.ToHexStringLower(SHA256.HashData(pixels));
    meshes.Dispose();
    object? survivingOwner = Progress(terrainFacade);
    ground.Prepare(camera,320,240);
    _ = graphics.CaptureFrame(GraphicsColor.ForgeLineClear,c => ground.Render(c,camera));
    ground.Dispose(); graphics.WaitForIdle();
    Thread.Sleep(300); // Outside all timed work; expire the existing 250 ms DXGI sample cache.
    var retired = graphics.Diagnostics;
    if (retired.Resources.LoadedTextureCount != initial.Resources.LoadedTextureCount || retired.Resources.ShaderResourceDescriptorsUsed != initial.Resources.ShaderResourceDescriptorsUsed || retired.Health.LiveResourceCount != initial.Health.LiveResourceCount || retired.Health.PendingRetirementCount != 0)
        throw new InvalidOperationException("Residency did not return to its registered-resource plateau after owner disposal.");
    cycles.Add(new { cycle,setupMilliseconds,setupAllocatedBytes,drainMilliseconds,sameStore,commonTextureCacheIds=commonIds,
        residencyBefore,residencyAfter,survivingOwner,pixelSha256,samples=raw.Length,allocatedBytes,recordingMilliseconds,raw,
        uploadBefore,uploadAfter,beforeResources=before.Resources,afterResources=after.Resources,retiredResources=retired.Resources,retiredHealth=retired.Health,
        preparedPayloadBytesAfterRetirement=PreparedBytes(meshStore),nativeUploadArena=UploadArena(graphics),
        observedMemory=retired.Memory,unavailableWarmShaderCompilationCounter=(long?)null,missingGpuAllocationInformation="Default-heap committed allocation bytes are unavailable through the frozen public graphics API; logical cache data and DXGI process usage are distinct." });
}
File.WriteAllText(args[0],JsonSerializer.Serialize(new {schemaVersion=1,startedUtc,completedUtc=DateTimeOffset.UtcNow,source,
    runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    adapter=graphics.Diagnostics.Device,catalogSignature=catalog.Manifest.ContentSignature,catalogMilliseconds,
    presentationSha256=Hash(typeof(SimpleInstanceRenderer).Assembly.Location),graphicsSha256=Hash(typeof(IGraphicsDevice).Assembly.Location),
    probeSha256=Hash(Assembly.GetExecutingAssembly().Location),runtimeManifestSha256=Hash(Path.Combine(args[1],"manifest.json")),
    protocol=$"{cycleCount} complete load/warm/unload cycles; cold setup includes constructors and first Prepare, GPU drain separate; 64 warmups/256 frame samples; no native timing gate inferred from this functional/plateau probe.",cycles},new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine);
static object? Field(object owner,string name) => owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(owner);
static object Store(object facade) => facade.GetType().GetProperty("Store",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(facade) ?? facade;
static object? Progress(object facade) => facade.GetType().GetProperty("Residency",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(facade);
static long? Reads(object? progress) => progress?.GetType().GetProperty("ContainerReads")?.GetValue(progress) is long value ? value : null;
static string[] TextureIds(object store) => Field(store,"_textures") is IDictionary map ? map.Keys.Cast<object>().Select(static id=>id.ToString()!).ToArray() : [];

static long? PreparedBytes(object store)
{
    object? prepared = store.GetType().GetProperty("Prepared",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(store);
    return prepared?.GetType().GetProperty("PayloadBytes",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(prepared) is long value ? value : null;
}

static object? UploadArena(IGraphicsDevice graphics) => graphics.GetType().GetProperty("UploadDiagnostics",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(graphics);

static long? UploadCounter(object? diagnostics,string name) => diagnostics?.GetType().GetProperty(name)?.GetValue(diagnostics) is long value ? value : null;

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
