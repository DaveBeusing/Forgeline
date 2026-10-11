using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class RenderQualityQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented=true, IncludeFields=true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_QUALITY_QUALIFICATION_OUTPUT"));

    [Fact(Skip="Set FORGELINE_QUALITY_QUALIFICATION_OUTPUT for isolated hardware qualification.", SkipUnless=nameof(Enabled))]
    public void CaptureProductionQualityProfilesWithTerrainEffectsAndSelectionOverlays()
    {
        DirectoryInfo? root=new(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"ForgeLine.sln"))) root=root.Parent;
        Assert.NotNull(root);
        string output=Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_QUALITY_QUALIFICATION_OUTPUT")!,root.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var catalog=RuntimeAssetCatalog.Load(Path.Combine(root.FullName,"assets/runtime"));
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        Assert.True(terrain.TrySampleHeight(650,520,out float height));
        Vector3 target=new(650,height,520);
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Quality Qualification",1280,720));
        using var graphics=(D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { EnableVSync=false, EnableDebugLayer=false, AllowSoftwareAdapterFallback=false, EnableMemoryDiagnostics=true });
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        var lighting=SceneLightingSettings.Default;
        var results=new List<object>();
        foreach(RenderQuality quality in Enum.GetValues<RenderQuality>())
        {
            var profile=new RenderQualitySettings { Quality=quality }.Resolve(true,false,1280,720,3);
            graphics.ConfigurePostProcessing(GraphicsPostProcessingSettings.Neutral);
            graphics.ConfigureSceneOutput(new(true,lighting.Exposure,true));
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality=profile.Atmosphere });
            graphics.ConfigureDirectionalShadows(profile.Shadows);
            graphics.ConfigurePostProcessing(profile.PostProcessing);
            using var ground=new TerrainRenderer(graphics,terrain,runtimeAssets:catalog,surfaceDetail:profile.TerrainDetail) { DebugChunksEnabled=false };
            using var meshes=new SimpleInstanceRenderer(graphics,catalog,lodDiameterScale:profile.LodDiameterScale);
            using var particles=new ParticleVfxRenderer(graphics,catalog,profile.Particles);
            using var outlines=new DebugDrawRenderer(graphics,depthEnabled:false,lineWidthPixels:3);
            var indicators=new DebugDraw { Enabled=true };
            indicators.Circle(target+new Vector3(-15,2,0),8,new(0,.8f,.2f,1));
            indicators.Circle(target+new Vector3(15,2,0),8,new(1,.1f,0,1));
            indicators.Line(target+new Vector3(-15,2,0),target+new Vector3(15,2,0),new(0,.4f,1,1));
            var instances=new RenderInstance[25];
            VfxEffectKind[] kinds=[VfxEffectKind.DestructionSmokePlume,VfxEffectKind.DestructionDustCloud,VfxEffectKind.DestructionSparkEmission,VfxEffectKind.ExplosionBuilding];
            for(int i=0;i<16;i++)
            {
                var definition=VfxPresentationCatalog.Get(kinds[i%4]);
                Vector3 position=target+new Vector3((i%4-1.5f)*8,2,(i/4-1.5f)*8);
                Assert.True(terrain.TrySampleHeight(position.X,position.Z,out float localHeight));
                position.Y=localHeight+2;
                instances[i]=new(new((uint)i+1,1),new(position,Quaternion.Identity,definition.BaseScale),new(1),RenderMaterialHandle.Default,
                    RenderVisibilityMask.Default,VfxFeature:new(definition.Kind));
            }
            for(int i=16;i<24;i++)
                instances[i]=new(new((uint)i+1,1),new(target+new Vector3((i-19.5f)*7,6,20),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.3f),new(.3f,12,18)),new(1),RenderMaterialHandle.Default,RenderVisibilityMask.Default);
            instances[24]=instances[0] with { Entity=new(25,1),Visibility=RenderVisibilityMask.Debug };
            var snapshots=new PresentationSnapshotBuffer();var world=new RenderWorld();
            foreach(float distance in new[] {120f,420f,1000f})
            {
                snapshots.Publish(new(new SimulationTick(1),TimeSpan.FromMilliseconds(50),instances.Length,instances,sessionId:new((ulong)distance)));
                Assert.True(world.Update(snapshots));
                var camera=new RtsCamera(new() { InitialTarget=target, InitialDistance=distance, MaximumDistance=2000 });
                var measuredCamera=camera.CaptureState();
                particles.Prepare(world,camera,1280,720,.1);particles.Prepare(world,camera,1280,720,.1);particles.Prepare(world,camera,1280,720,.1);
                Action<IGraphicsCommandContext> record=context=> {
                    if(graphics.Atmosphere.Enabled) context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera,context.Width,context.Height));
                    var shadow=DirectionalShadowProjection.Create(camera.CaptureState(),lighting,profile.Shadows);
                    context.BeginDirectionalShadowPass(shadow);
                    int terrainDraws=ground.RenderShadow(context,shadow,128);
                    meshes.RenderShadow(context,camera,world,1,shadow,profile.Shadows.MaximumCasters-terrainDraws,profile.Shadows.MaximumDraws-terrainDraws);
                    context.EndDirectionalShadowPass();
                    ground.Render(context,camera);
                    particles.Prepare(world,camera,context.Width,context.Height,0);
                    meshes.Render(context,camera,world,1,particles:particles);
                    particles.Render(context,camera);
                    context.BeginPass(GraphicsFramePass.Overlay);outlines.Render(context,camera,indicators);
                };
                for(int i=0;i<64;i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
                var cpu=new double[512];var gpu=new List<double>();var post=new List<double>();ulong fence=0;
                for(int i=0;i<cpu.Length;i++)
                {
                    long started=Stopwatch.GetTimestamp();graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
                    cpu[i]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var d=graphics.Diagnostics;
                    if(d.Frame is {} frame && frame.GpuSubmissionFence>fence)
                    {
                        fence=frame.GpuSubmissionFence;
                        if(d.GpuFrameMilliseconds is {} total) gpu.Add(total);
                        if(frame.CompositeGpuMilliseconds is {} composition) post.Add(composition);
                    }
                }
                graphics.WaitForIdle();
                Capture("still");
                camera=new(new() { InitialTarget=target+new Vector3(2,0,2),InitialYawRadians=.035f,InitialDistance=distance,MaximumDistance=2000 });
                Capture("motion");
                Assert.NotEmpty(gpu);Assert.NotEmpty(post);Array.Sort(cpu);
                results.Add(new { quality,distance,profile,measuredCamera,motionCamera=camera.CaptureState(),cpuMeanMs=cpu.Average(),cpuP99Ms=cpu[506],gpuMeanMs=gpu.Average(),postGpuMeanMs=post.Average(),gpuSamples=gpu.Count,
                    ground.LastDiagnostics,particles=particles.Diagnostics,meshes=meshes.LastDiagnostics,graphics=graphics.Diagnostics });

                void Capture(string pose)
                {
                    byte[] pixels=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,record);
                    Assert.Equal(pixels,graphics.CaptureFrame(GraphicsColor.ForgeLineClear,record));
                    using var file=File.Create(Path.Combine(Path.GetDirectoryName(output)!,$"{Path.GetFileNameWithoutExtension(output)}-{quality}-{distance}-{pose}.ppm"));
                    file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n1280 720\n255\n"));
                    for(int pixel=0;pixel<pixels.Length;pixel+=4) file.Write(pixels.AsSpan(pixel,3));
                }
            }
            graphics.WaitForIdle();Assert.Equal(0,graphics.Diagnostics.Resources.TextureBindingFailureCount);
        }
        graphics.WaitForIdle();Assert.Equal(0,graphics.Diagnostics.Health.PendingRetirementCount);
        File.WriteAllText(output,JsonSerializer.Serialize(new { graphics.Diagnostics.Device,driver=Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            method="Canonical terrain/compiled materials at industrial yard; 16 copied mixed effects at fixed .2s age, eight thin infrastructure proxies, hidden effect and full-resolution selection/command overlays; Low/Medium/High at 120/420/1000m,1280x720,64 warmups/512 samples,unique completed fences,VSync off. CPU includes Present/frame reuse. Separate shifted/yawed captures are not included in timings; no simulation workload.",results },ReportOptions));
    }
}
