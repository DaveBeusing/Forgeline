using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfantrySkinRendererTests
{
    [Fact]
    public void ThousandsOfFiguresSharePoseUploadsAndShadowColorAndRemainBounded()
    {
        RuntimeMeshVertex[] vertices=[new(.1f,0,0,0,1,0,0,0,1,0,0,1),new(0,.1f,0,0,1,0,0,0,1,0,0,1),new(0,0,.1f,0,1,0,0,0,1,0,0,1)];
        var data=new RuntimeMeshData(RuntimeMeshAttributes.Normal|RuntimeMeshAttributes.Uv0|RuntimeMeshAttributes.Tangent,vertices,[0,1,2],[],[new(0,3,-1)]);
        SkinJoint[] joints=[new("root",-1,Vector3.Zero,Quaternion.Identity,Matrix4x4.Identity)];
        var skin=new RuntimeSkinData(data,joints,Enumerable.Repeat(new SkinInfluence(0,0,0,0,new(1,0,0,0)),3).ToArray(),[]);
        var names=new[]{"idle","walk","run","aim","fire","reload","hit","suppression","death"};
        var clips=names.Select(name=>new RuntimeAnimationData(name,[new AnimationNode("root",-1)],
            [new AnimationChannel(0,AnimationPath.Translation,AnimationInterpolation.Linear,[0,1],[Vector4.Zero,new(.1f,0,0,0)])],RuntimeSkinData.RigSignature(joints)));
        var device=new FakeDevice();var bindVertex=device.CreateBuffer(new(144,GraphicsBufferMemory.Upload));var index=device.CreateBuffer(new(12,GraphicsBufferMemory.Upload));
        var mesh=new RuntimeMeshBuffers(bindVertex,index,48,3,data.Attributes,default,false,false,1){Skin=new(skin,clips)};
        using(var cache=new SkinFrameCache(device))
        {
            var pose=new InfantryAnimationPose(true,InfantryAction.Walk,.5,true);var id=AssetId.Parse("unit.test.infantry");
            cache.Begin(0,true);var first=cache.Resolve(id,mesh,pose,1,out int variant);Assert.Equal(0,variant);
            for(int i=0;i<4000;i++)Assert.Same(first.VertexBuffer,cache.Resolve(id,mesh,pose,1,out _).VertexBuffer);
            Assert.Equal(1,cache.EvaluatedPoses);Assert.Equal(144,cache.ResidentBytes);
            cache.Begin(0,false);Assert.Same(first.VertexBuffer,cache.Resolve(id,mesh,pose,1,out _).VertexBuffer);Assert.Equal(1,cache.EvaluatedPoses);
            Assert.Same(mesh.VertexBuffer,cache.Resolve(id,mesh,pose,0,out _).VertexBuffer);
            for(int i=0;i<40;i++)cache.Resolve(id,mesh,pose with{Phase=i/48.0},1,out _);
            Assert.Equal(SkinFrameCache.MaximumVariants,cache.EvaluatedPoses);Assert.True(cache.StaticFallbacks>0);
            var other=first.VertexBuffer;cache.Begin(1,false);Assert.NotSame(other,cache.Resolve(id,mesh,pose,1,out _).VertexBuffer);
            cache.Begin(0,false);Assert.Same(other,cache.Resolve(id,mesh,pose,1,out _).VertexBuffer);
            Assert.Throws<ArgumentOutOfRangeException>(()=>cache.Begin(4,false));
        }
        Assert.All(device.Buffers.Skip(2),b=>Assert.True(b.Disposed));Assert.False(((FakeBuffer)bindVertex).Disposed);Assert.False(((FakeBuffer)index).Disposed);
        mesh.Dispose();Assert.All(device.Buffers,b=>Assert.True(b.Disposed));
    }
    private sealed class FakeDevice : IGraphicsDevice
    {
        public List<FakeBuffer> Buffers { get; } = [];
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new FakePipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) { var buffer = new FakeBuffer(description); Buffers.Add(buffer); return buffer; }
        public IGraphicsTexture CreateTexture(GraphicsTextureData texture) => new FakeTexture(texture.Description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) => throw new NotSupportedException();
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class FakeBuffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        public byte[] Upload { get; private set; } = [];
        public bool Disposed { get; private set; }
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => Upload = MemoryMarshal.AsBytes(data).ToArray();
        public void Dispose() => Disposed = true;
    }
    private sealed class FakePipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class FakeTexture(GraphicsTextureDescription description) : IGraphicsTexture
    { public GraphicsTextureDescription Description => description; public void Dispose() { } }
    private sealed class FakeContext : IGraphicsCommandContext
    {
        public int Width => 1920; public int Height => 1080; public int FrameIndex => 0;
        public int Instances { get; private set; }
        public FakeBuffer? InstanceBuffer { get; private set; }
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { }
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) { if (inputSlot == 1) InstanceBuffer = (FakeBuffer)buffer; }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) => Instances += instanceCount;
    }
}
