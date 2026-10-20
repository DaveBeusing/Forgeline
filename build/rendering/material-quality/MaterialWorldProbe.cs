using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

if(args.Length!=3 || File.Exists(args[0]))throw new ArgumentException("Supply fresh report path, runtime root and expected source SHA.");
string source=typeof(SimpleInstanceRenderer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
if(!source.EndsWith(args[2],StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Frozen assembly source mismatch.");
var catalog=RuntimeAssetCatalog.LoadCancellable(args[1],CancellationToken.None,requireIntegrity:true);
var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
using var powerRequirement=new ProbePowerRequirement();
using var platform=new WindowsPlatform();
using var window=platform.CreateWindow(new("FORGELINE Material World Probe",1920,1080));
using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new() {EnableVSync=false,EnableDebugLayer=false,AllowSoftwareAdapterFallback=false,EnableMemoryDiagnostics=true});
if(graphics.Diagnostics.Device.IsSoftwareAdapter)throw new InvalidOperationException("Hardware adapter required.");
var rows=new List<object>();
foreach(RenderQuality quality in Enum.GetValues<RenderQuality>())
foreach(var sceneKind in new[] {VisualReferenceSceneKind.MainBattleTankProduction,VisualReferenceSceneKind.IndustrialBase})
foreach(var zoom in new[] {RtsReferenceZoom.NormalGameplay})
{
    var profile=new RenderQualitySettings {Quality=quality}.Resolve(true,false,1920,1080,3);
    graphics.ConfigurePostProcessing(profile.PostProcessing);
    graphics.ConfigureSceneOutput(new(profile.LinearScene,SceneLightingSettings.Default.Exposure,true));
    graphics.ConfigureDirectionalShadows(profile.Shadows);
    var scene=VisualReferenceScene.Create(sceneKind,terrain,runtimeAssets:catalog);
    var buffer=new PresentationSnapshotBuffer();buffer.Publish(scene.Snapshot);
    var world=new RenderWorld();world.Update(buffer);
    var camera=new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target,zoom));
    var cameraStart=camera.CaptureState();
    var cpu=new List<double>();var worldGpu=new List<double>();var shadowGpu=new List<double>();var totalGpu=new List<double>();
    var completedFences=new List<ulong>();var shadowFences=new List<ulong>();ulong previousFence=0;
    GraphicsDiagnostics warmStart,warmEnd;
    using(var ground=new TerrainRenderer(graphics,terrain,runtimeAssets:catalog,surfaceDetail:profile.TerrainDetail,
        textureDetail:profile.Textures,residency:new(true,128)) {DebugChunksEnabled=false})
    using(var meshes=new SimpleInstanceRenderer(graphics,catalog,lodDiameterScale:profile.LodDiameterScale,textureDetail:profile.Textures))
    {
        GraphicsDirectionalShadowFrame? shadow=null;
        void Prepare()
        {
            shadow=profile.Shadows.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(),SceneLightingSettings.Default,profile.Shadows):null;
            ground.Prepare(camera,1920,1080,shadow);
            meshes.Prepare(camera,world,new RenderPreparationRequest(1920,1080,1,shadow,profile.Shadows.MaximumCasters,1));
        }
        void Frame()
        {
            Prepare();
            graphics.RenderFrame(GraphicsColor.ForgeLineClear,c=> {
                if(shadow is {} frame)
                {
                    c.BeginDirectionalShadowPass(frame);int draws=ground.RenderShadow(c,frame,128);
                    meshes.RenderShadow(c,camera,world,1,frame,profile.Shadows.MaximumCasters-draws,profile.Shadows.MaximumDraws-draws,1);
                    c.EndDirectionalShadowPass();
                }
                ground.Render(c,camera);meshes.Render(c,camera,world,1,presentationSeconds:1);c.BeginPass(GraphicsFramePass.Overlay);
            });
            meshes.Submit();meshes.Retire();
        }
        for(int i=0;i<64;i++)
        {
            if(!platform.PumpEvents())throw new OperationCanceledException("Probe window closed during warmup.");
            Frame();
        }
        graphics.WaitForIdle();warmStart=graphics.Diagnostics;
        for(int i=0;i<256;i++)
        {
            if(!platform.PumpEvents())throw new OperationCanceledException("Probe window closed during measurement.");
            camera.ApplyState(cameraStart with {Target=cameraStart.Target+new Vector3(i*.005f,0,i*.003f)});
            long stamp=Stopwatch.GetTimestamp();Frame();cpu.Add(Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
            var d=graphics.Diagnostics;
            if(d.Frame is {} f && f.GpuSubmissionFence>previousFence)
            {
                previousFence=f.GpuSubmissionFence;completedFences.Add(previousFence);
                if(f.WorldGpuMilliseconds is double w)worldGpu.Add(w);
                if(d.DirectionalShadows is {} sd && profile.Shadows.Enabled && sd.GpuSubmissionFence==f.GpuSubmissionFence && sd.GpuMilliseconds is double s)
                {shadowGpu.Add(s);shadowFences.Add(sd.GpuSubmissionFence);}
                if(d.GpuFrameMilliseconds is double g)totalGpu.Add(g);
            }
        }
        graphics.WaitForIdle();warmEnd=graphics.Diagnostics;
        if(warmStart.Resources.TextureUploadCount!=warmEnd.Resources.TextureUploadCount || warmStart.Resources.LoadedTextureCount!=warmEnd.Resources.LoadedTextureCount)
            throw new InvalidOperationException("Warm material residency changed.");
    }
    graphics.WaitForIdle();
    var retired=graphics.Diagnostics;
    if(retired.Health.PendingRetirementCount!=0 || retired.Resources.LoadedTextureCount!=0 || retired.Health.SubmissionFaultCount!=0)
        throw new InvalidOperationException("Native resource retirement or device health failed.");
    rows.Add(new {quality,scene=sceneKind,zoom,scene.Fingerprint,profile,warmups=64,samples=256,cpuMilliseconds=cpu,
        worldGpuMilliseconds=worldGpu,shadowGpuMilliseconds=shadowGpu,totalGpuMilliseconds=totalGpu,completedFences,shadowFences,
        warmStart,warmEnd,retiredResources=retired.Resources,retiredHealth=retired.Health,
        interpretation="Fixed mechanical pose at one second; 0.005/0.003 metre target pan per frame; GPU timestamps are completed distinct fences. World and shadow intervals remain separate. No atmosphere or particles are recorded by this focused probe; full-client references qualify those paths separately."});
    Console.WriteLine($"Completed {quality}/{sceneKind}/{zoom}");
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0],JsonSerializer.Serialize(new {schemaVersion=2,windowEventsPumped=true,powerRequirement="thread-scoped-display-and-system",source,catalogSignature=catalog.Manifest.ContentSignature,
    manifestSha256=Hash(Path.Combine(args[1],"manifest.json")),presentationSha256=Hash(typeof(SimpleInstanceRenderer).Assembly.Location),
    driver=Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),graphics.Diagnostics.Device,graphics.Diagnostics.Surface,rows},
    new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

// The native window must remain responsive for every independent component run.
// Match the client capture's scoped display/system execution requirement.
internal sealed class ProbePowerRequirement : IDisposable
{
    private const uint Continuous = 0x80000000;
    private readonly uint _previous;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public ProbePowerRequirement()
    {
        _previous = SetThreadExecutionState(Continuous | 1 | 2);
        if (_previous == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Restore the execution requirement on its owning thread.");
        _disposed = true;
        GC.SuppressFinalize(this);
        if (SetThreadExecutionState(_previous | Continuous) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint state);
}
