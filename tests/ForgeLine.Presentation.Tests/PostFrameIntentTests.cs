using ForgeLine.Graphics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.Presentation.Tests.TerrainResidency;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;

namespace ForgeLine.Presentation.Tests;

public sealed class PostFrameIntentTests
{
    [Fact]
    public void TerrainObservationCopiesRetainedHistoryWithoutPrepareHarvestOrUploads()
    {
        using var device=new TerrainTestDevice();using var renderer=new TerrainRenderer(device,DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius:1));
        var camera=new RtsCamera();var context=new TerrainTestContext();renderer.Prepare(camera,1600,900);renderer.Render(context,camera);
        int buffers=device.Inner.Buffers.Count,textures=device.Textures.Count;var diagnostics=renderer.LastDiagnostics;
        var roster=new GraphicsPostFrameRoster();var first=renderer.CapturePostFrameIntent(roster);
        Assert.NotEmpty(first);Assert.Equal(Enumerable.Range(0,first.Length),first.Select(x=>x.Order));
        Assert.All(first,x=>{Assert.Contains(x.Resources,r=>r.Role=="vertex");Assert.Contains(x.Resources,r=>r.Role=="shared-indices");Assert.NotEmpty(x.IndexRanges);});
        Assert.Equal(diagnostics,renderer.LastDiagnostics);Assert.Equal(buffers,device.Inner.Buffers.Count);Assert.Equal(textures,device.Textures.Count);
        first[0].MaterialKeys[0]="changed copied array";Assert.NotEqual(first[0].MaterialKeys[0],renderer.CapturePostFrameIntent(new())[0].MaterialKeys[0]);
        device.Recording=true;Assert.Throws<InvalidOperationException>(()=>renderer.CapturePostFrameIntent(new()));device.Recording=false;
    }
    [Fact]
    public void EmptyScratchHasNoInventedInstanceResourceAndWrongSlotIsRejected()
    {
        using var device=new Device();using var renderer=new SimpleInstanceRenderer(device);
        Assert.Empty(renderer.CapturePostFrameMainIntent(new(),0));Assert.Empty(renderer.CapturePostFrameDecorationIntent(new(),0));
        Assert.Throws<InvalidOperationException>(()=>renderer.CapturePostFrameMainIntent(new(),3));
        device.Recording=true;Assert.Throws<InvalidOperationException>(()=>renderer.CapturePostFrameMainIntent(new(),0));
    }
    private sealed class Device:IGraphicsDevice
    {
        private readonly FakeGraphicsDevice _inner=new() { CaptureUploads=true };internal bool Recording;
        public bool IsRecording=>Recording;
        public GraphicsDiagnostics Diagnostics=>new(new("fake",0,true,"fake",false),new(1600,900,3,1,false,"fake"));
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription d)=>_inner.CreateGraphicsPipeline(d);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription d)=>_inner.CreateBuffer(d);
        public IGraphicsTexture CreateTexture(GraphicsTextureData d)=>_inner.CreateTexture(d);
        public void RenderFrame(GraphicsColor c,Action<IGraphicsCommandContext>? record=null)=>throw new NotSupportedException();
        public void Resize(int w,int h)=>throw new NotSupportedException();public void WaitForIdle(){}public void Dispose()=>_inner.Dispose();
    }
    [Fact]
    public void NonemptyBatchUsesActualSlotPrefixAndCopiedIntentSurvivesNextRender()
    {
        using var device=new Device();using var renderer=new SimpleInstanceRenderer(device);
        var camera=new RtsCamera();var world=new RenderWorld();var snapshots=new PresentationSnapshotBuffer();
        RenderInstance first=new(new EntityId(1,1),new(camera.Target,Quaternion.Identity,Vector3.One),new RenderMeshHandle(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,1);
        snapshots.Publish(new(new SimulationTick(1),TimeSpan.FromMilliseconds(50),2,[first,first with {Entity=new(2,1)}]));Assert.True(world.Update(snapshots));
        renderer.Render(new FakeGraphicsCommandContext {FrameIndex=1},camera,world,1);
        var roster1=new GraphicsPostFrameRoster();var before=renderer.CapturePostFrameMainIntent(roster1,1);var batch=Assert.Single(before);
        Assert.Equal(2,batch.InstanceCount);Assert.Equal(0,batch.InstanceOffset);Assert.Equal(112,batch.InstanceStride);
        int id=Assert.Single(batch.Resources,r=>r.Role=="captured-slot-instance-prefix").ResourceId;
        var source1=roster1.Sources[id-1];Assert.Equal(224UL,source1.Length);var buffer1=Assert.IsType<FakeGraphicsBuffer>(source1.Resource);byte[] bytes1=buffer1.LastUpload.ToArray();Assert.Equal(224,bytes1.Length);
        snapshots.Publish(new(new SimulationTick(2),TimeSpan.FromMilliseconds(50),1,[first]));Assert.True(world.Update(snapshots));
        renderer.Render(new FakeGraphicsCommandContext {FrameIndex=2},camera,world,1);
        var roster2=new GraphicsPostFrameRoster();var after=Assert.Single(renderer.CapturePostFrameMainIntent(roster2,2));
        var source2=roster2.Sources[Assert.Single(after.Resources,r=>r.Role=="captured-slot-instance-prefix").ResourceId-1];
        Assert.NotSame(source1.Resource,source2.Resource);Assert.Equal(112UL,source2.Length);Assert.Equal(1,after.InstanceCount);
        Assert.Equal(2,batch.InstanceCount);Assert.Equal(bytes1,buffer1.LastUpload);
        batch.Resources[0]=new(4096,"mutated copy");Assert.DoesNotContain(renderer.CapturePostFrameMainIntent(new(),2)[0].Resources,r=>r.Role=="mutated copy");
    }
}
