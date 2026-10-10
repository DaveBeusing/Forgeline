using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ParticleVfxRendererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"forgeline-particle-contract-"+Guid.NewGuid().ToString("N"));
    private readonly RuntimeAssetCatalog _catalog;
    public ParticleVfxRendererTests()
    {
        Directory.CreateDirectory(_root);
        var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0,
            [new(0,0,0,0,1,0,0,0,0,0,0,0),new(1,0,0,0,1,0,1,0,0,0,0,0),new(0,0,1,0,1,0,0,1,0,0,0,0)],
            [0,2,1],["material.vfx.smoke"],[new(0,3,0)]);
        var records = new Dictionary<string,RuntimeAssetRecord>();
        foreach (var definition in VfxPresentationCatalog.All)
        {
            Add(definition.MeshAssetId,RuntimeAssetType.Mesh,mesh.ToPayload());
            Add(definition.MaterialAssetId,RuntimeAssetType.Material,"{}"u8.ToArray());
        }
        File.WriteAllText(Path.Combine(_root,RuntimeAssetCatalog.ManifestFileName),JsonSerializer.Serialize(
            new RuntimeAssetManifest { CompilerVersion="test",Assets=records.Values.ToArray() },RuntimeAssetCatalog.CreateJsonOptions()));
        _catalog=RuntimeAssetCatalog.Load(_root);
        void Add(string id,RuntimeAssetType type,byte[] payload)
        {
            if (records.ContainsKey(id)) return;
            string path=id+".flasset";
            RuntimeAssetFile.Write(Path.Combine(_root,path),type,[],payload);
            records.Add(id,new() { Id=id,Type=type,SourcePath=path,RuntimePath=path,SourceHash="test",BuildHash="test",RuntimeVersion=1,CompilerVersion="test" });
        }
    }

    [Fact]
    public void PartialBufferCreationFailureReleasesEveryCreatedResource()
    {
        using var device=new Device(failBuffer:3);
        Assert.Throws<InvalidOperationException>(()=>new ParticleVfxRenderer(device,_catalog));
        Assert.Equal(0,device.LiveBuffers);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new GpuParticleRenderer(device,4097));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new GpuParticleRenderer(device,0));
    }

    [Fact]
    public void ProductionTerrainAtmosphereAndParticlesRespectHiddenCopiesAndRetirement()
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new WindowConfiguration("FORGELINE Particle Terrain",320,240));
        using var graphics=(D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,new() { ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false });
        graphics.ConfigureSceneOutput(new(true,1.15f,true));
        graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality=AtmosphereQuality.Height });
        var terrain=ForgeLine.Game.CentralDivideTerrainFactory.Create(ForgeLine.Game.CentralDivideBattlefield.Create());
        using var ground=new TerrainRenderer(graphics,terrain) { DebugChunksEnabled=false };
        using var particles=new ParticleVfxRenderer(graphics,_catalog);
        Assert.True(terrain.TrySampleHeight(1536,1536,out float height));
        var camera=new RtsCamera(new() { InitialTarget=new(1536,height,1536),InitialDistance=120 });
        var effect=Effect(1,VfxEffectKind.ExplosionBuilding) with { Transform=new(new(1536,height+3,1536),Quaternion.Identity,Vector3.One) };
        particles.Prepare(World([effect]),camera,320,240,.1);
        particles.Prepare(World([effect]),camera,320,240,.1);
        byte[] Capture(bool includeHidden)
        {
            var world=World(includeHidden ? [effect,effect with { Entity=new(2,1),Visibility=RenderVisibilityMask.Debug,VfxFeature=effect.VfxFeature with { EmitterId=2 } }] : [effect]);
            particles.Prepare(world,camera,320,240,0);
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear,context =>
            {
                context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera,context.Width,context.Height));
                ground.Render(context,camera);particles.Render(context,camera);context.BeginPass(GraphicsFramePass.Overlay);
            });
        }
        byte[] visible=Capture(false);Assert.Equal(visible,Capture(true));
        Assert.Equal(1,particles.Diagnostics.ActiveEmitters);Assert.Equal(1,particles.Diagnostics.DrawCalls);
        Assert.Equal(0,graphics.Diagnostics.Resources.TextureBindingFailureCount);
        particles.Dispose();ground.Dispose();graphics.WaitForIdle();
        Assert.Equal(0,graphics.Diagnostics.Health.PendingRetirementCount);
    }

    [Fact]
    public void SteadyEmissionReusesRenderScratchWithoutPerFrameAllocation()
    {
        using var device=new Device();using var renderer=new ParticleVfxRenderer(device,_catalog);
        var world=World(Enumerable.Range(1,32).Select(i=>Effect((uint)i,VfxEffectKind.PersistentHeavySmoke,true)).ToArray());
        var camera=new RtsCamera(new() { InitialTarget=Vector3.Zero,InitialDistance=420 });
        for(int i=0;i<32;i++)renderer.Prepare(world,camera,1280,720,0);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<128;i++)renderer.Prepare(world,camera,1280,720,0);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
    }

    [Fact]
    public void SameEmitterDoesNotReplayAndPauseDoesNotAdvanceItsGpuAge()
    {
        using var device=new Device();
        using var renderer=new ParticleVfxRenderer(device,_catalog);
        var camera=new RtsCamera(new() { InitialTarget=Vector3.Zero,InitialDistance=120 });
        var effect=Effect(1,VfxEffectKind.ExplosionBuilding);
        var world=World([effect]);
        renderer.Prepare(world,camera,1280,720,.1);
        ulong emitted=renderer.Diagnostics.EmittedParticles;
        var first=renderer.Particles.ToArray();
        renderer.Prepare(world,camera,1280,720,30,true);
        Assert.Equal(first,renderer.Particles.ToArray());
        Assert.Equal(emitted,renderer.Diagnostics.EmittedParticles);
        renderer.Prepare(world,camera,1280,720,.1);
        Assert.NotEqual(first,renderer.Particles.ToArray());
        Assert.Equal(emitted,renderer.Diagnostics.EmittedParticles);
        var context=new Context();renderer.Render(context,camera);
        Assert.Equal(1,context.Draws);Assert.True(context.ParticlePassEnded);
        Assert.Equal(1,renderer.Diagnostics.DrawCalls);
        Assert.Equal(effect,world.GetInterpolatedInstance(0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>renderer.Prepare(world,camera,1280,720,double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>renderer.Prepare(world,camera,0,720,0));
        for(int i=0;i<10;i++) renderer.Prepare(world,camera,1280,720,.1);
        Assert.Empty(renderer.Particles.ToArray());
        Assert.True(renderer.Replaces(effect));
        Assert.Equal(emitted,renderer.Diagnostics.EmittedParticles);
    }

    [Fact]
    public void HiddenEffectsDisappearImmediatelyAndSessionReplacementClearsSeeds()
    {
        using var device=new Device();using var renderer=new ParticleVfxRenderer(device,_catalog);
        var camera=new RtsCamera(new() { InitialTarget=Vector3.Zero,InitialDistance=120 });
        var effect=Effect(1,VfxEffectKind.PersistentHeavySmoke,true);
        renderer.Prepare(World([effect],1),camera,1280,720,.1);
        var initial=renderer.Particles.ToArray();Assert.NotEmpty(initial);
        renderer.Prepare(World([effect with { Visibility=RenderVisibilityMask.Debug }],1),camera,1280,720,.1);
        Assert.Empty(renderer.Particles.ToArray());Assert.False(renderer.Replaces(effect));
        renderer.Prepare(World([effect],2),camera,1280,720,.1);
        Assert.Equal(initial,renderer.Particles.ToArray());
        Assert.Equal(8UL,renderer.Diagnostics.EmittedParticles);
    }

    [Fact]
    public void RepeatedDestructionAndQualityCapsRetainBoundedBuffersAndMeshFallback()
    {
        using var device=new Device();using var renderer=new ParticleVfxRenderer(device,_catalog,new(ParticleQuality.Low));
        var camera=new RtsCamera(new() { InitialTarget=Vector3.Zero,InitialDistance=420 });
        var effects=Enumerable.Range(1,600).Select(i=>Effect((uint)i,VfxEffectKind.ExplosionBuilding)).ToArray();
        long bytes=0;
        for(int frame=0;frame<100;frame++)
        {
            var current=effects.Select(x=>x with { VfxFeature=x.VfxFeature with { EmitterGeneration=(uint)frame+1 } }).ToArray();
            renderer.Prepare(World(current),camera,1280,720,.05);
            Assert.InRange(renderer.Diagnostics.ActiveParticles,1,512);
            Assert.InRange(renderer.Diagnostics.ScreenCoverage,0,.5f);
            Assert.True(renderer.Diagnostics.FallbackEffects>0);
            if(frame==0) bytes=renderer.Diagnostics.BufferBytes;
            Assert.Equal(bytes,renderer.Diagnostics.BufferBytes);
            Assert.Equal(4,device.Buffers);
        }
        Assert.True(renderer.Diagnostics.DroppedParticles>0);
        renderer.Dispose();Assert.Equal(0,device.LiveBuffers);
    }

    [Theory]
    [InlineData(ParticleQuality.Disabled,true)]
    [InlineData(ParticleQuality.Medium,false)]
    public void DisabledAndUnsupportedDevicesKeepLegacyEffects(ParticleQuality quality,bool supported)
    {
        using var device=new Device(supported);using var renderer=new ParticleVfxRenderer(device,_catalog,new(quality));
        var effect=Effect(1,VfxEffectKind.ExplosionBuilding);
        renderer.Prepare(World([effect]),new RtsCamera(),1280,720,.1);
        Assert.False(renderer.Replaces(effect));Assert.Equal(0,device.Buffers);
        Assert.Equal(1,renderer.Diagnostics.FallbackEffects);
    }

    [Fact]
    public void MissingOrInvalidAssetsPreserveFallbackAndBurstAgeFollowsCopiedTick()
    {
        File.WriteAllBytes(Path.Combine(_root,"vfx.combat.explosion.building.flasset"),[0]);
        using var device=new Device();using var renderer=new ParticleVfxRenderer(device,_catalog);
        var effect=Effect(1,VfxEffectKind.ExplosionBuilding);
        renderer.Prepare(World([effect]),new RtsCamera(),1280,720,.1);
        Assert.False(renderer.Replaces(effect));
        var old=Effect(2,VfxEffectKind.ExplosionMedium) with { VfxFeature=new(VfxEffectKind.ExplosionMedium,2,1,false,1) };
        renderer.Prepare(World([old],tick:100),new RtsCamera(),1280,720,.1);
        Assert.Empty(renderer.Particles.ToArray());Assert.True(renderer.Replaces(old));
    }

    private static RenderInstance Effect(uint id,VfxEffectKind kind,bool continuous=false) => new(new(id,1),new(Vector3.Zero,Quaternion.Identity,Vector3.One),
        new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,VfxFeature:new(kind,id,1,continuous));
    private static RenderWorld World(RenderInstance[] effects,ulong session=1,ulong tick=1)
    {
        var snapshots=new PresentationSnapshotBuffer();snapshots.Publish(new(new SimulationTick(tick),TimeSpan.FromMilliseconds(50),0,effects,sessionId:new(session)));
        var world=new RenderWorld();Assert.True(world.Update(snapshots));return world;
    }
    public void Dispose()=>Directory.Delete(_root,true);
    private sealed class Device(bool supported=true,int failBuffer=0) : IGraphicsDevice
    {
        public bool SupportsParticleRendering=>supported;
        public int Buffers { get; private set; } public int LiveBuffers { get; private set; }
        public GraphicsDiagnostics Diagnostics=>new(new("test",0,false,"test",false),new(1280,720,3,0,false,"test"));
        public void ConfigureParticleDepth(bool enabled) { }
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description)=>new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) { Buffers++;if(Buffers==failBuffer)throw new InvalidOperationException("Injected buffer creation failure.");LiveBuffers++;return new Buffer(description,()=>LiveBuffers--); }
        public void RenderFrame(GraphicsColor color,Action<IGraphicsCommandContext>? record=null)=>throw new NotSupportedException();
        public void Resize(int width,int height) { } public void WaitForIdle() { } public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline { public GraphicsPipelineDescription Description=>description;public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription description,Action release) : IGraphicsBuffer
    {
        private bool _disposed;public GraphicsBufferDescription Description=>description;
        public void SetData<T>(ReadOnlySpan<T> data,int offsetInBytes=0) where T:unmanaged { Assert.True(MemoryMarshal.AsBytes(data).Length<= (int)description.SizeInBytes); }
        public void Dispose() { if(_disposed)return;release();_disposed=true; }
    }
    private sealed class Context : IGraphicsCommandContext
    {
        public int Width=>1280;public int Height=>720;public int FrameIndex=>0;public int Draws { get; private set; } public bool ParticlePassEnded { get; private set; }
        public void BeginParticlePass() { } public void EndParticlePass()=>ParticlePassEnded=true;
        public void SetViewport(float x,float y,float width,float height) { } public void SetScissor(int left,int top,int right,int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { } public void SetVertexBuffer(IGraphicsBuffer buffer,int strideInBytes,int offsetInBytes=0,int inputSlot=0) { }
        public void SetIndexBuffer(IGraphicsBuffer buffer,GraphicsIndexFormat format,int offsetInBytes=0) { }public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void Draw(int count,int startVertex=0) { }public void DrawIndexed(int count,int startIndex=0,int baseVertex=0) { }
        public void DrawIndexedInstanced(int count,int instances,int startIndex=0,int baseVertex=0,int startInstance=0)=>Draws++;
    }
}
