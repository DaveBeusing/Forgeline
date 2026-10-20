using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;

namespace ForgeLine.Presentation.Tests.TerrainResidency;

public sealed class TerrainReferenceTests
{
    [Fact]
    public void MainShadowUnionMatchesEagerCoverageAcrossJumpsAndResize()
    {
        using var device = new TerrainTestDevice();
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 3);
        var camera = new RtsCamera(new() { InitialDistance = 120 });
        using var eager = new TerrainRenderer(device, world);
        using var bounded = new TerrainRenderer(device, world, residency: new(true, 49));
        var context = new TerrainTestContext();
        foreach (Vector3 target in new[] { Vector3.Zero, new(640, 0, 640), new(-512, 0, 512), Vector3.Zero })
        {
            camera.ApplyState(camera.CaptureState() with { Target = target });
            var shadow = DirectionalShadowProjection.Create(camera.CaptureState(), SceneLightingSettings.Default, device.DirectionalShadows);
            bounded.Prepare(camera, context.Width, context.Height, shadow);
            eager.Render(context, camera); bounded.Render(context, camera);
            Assert.Equal(eager.LastDiagnostics.VisibleChunks, bounded.LastDiagnostics.VisibleChunks);
            Assert.Equal(eager.RenderShadow(context, shadow, 1000), bounded.RenderShadow(context, shadow, 1000));
            bounded.Prepare(camera, 231, 1901, shadow);
            Assert.Throws<InvalidOperationException>(() => bounded.Render(context, camera));
        }
    }

    [Fact]
    public void CapacityFailureDoesNotCreateOrEvictResources()
    {
        using var device = new TerrainTestDevice();
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 3);
        using var renderer = new TerrainRenderer(device, world, residency: new(true, 1));
        var camera = new RtsCamera(new() { InitialDistance = 900 });
        int buffers = device.Inner.Buffers.Count, textures = device.Textures.Count;
        Assert.Throws<InvalidOperationException>(() => renderer.Prepare(camera, 1600, 900));
        Assert.Equal(buffers, device.Inner.Buffers.Count); Assert.Equal(textures, device.Textures.Count);
    }

    [Fact]
    public void RecordingAndUnpreparedCameraRemainRejected()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1), residency: new(true, 9));
        var camera = new RtsCamera(); var context = new TerrainTestContext();
        Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera));
        device.Recording = true;
        Assert.Throws<InvalidOperationException>(() => renderer.Prepare(camera, 1600, 900));
        device.Recording = false; renderer.Prepare(camera, 1600, 900);
        camera.ApplyState(camera.CaptureState() with { Target = new(256, 0, 256) });
        Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera));
    }

    [Fact]
    public void WarmViewDoesNotRegenerateOrUploadAndDisposalIsExact()
    {
        using var device = new TerrainTestDevice();
        var renderer = new TerrainRenderer(device, DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1), residency: new(true, 9));
        var camera = new RtsCamera(); var context = new TerrainTestContext();
        renderer.Prepare(camera, 1600, 900); renderer.Render(context, camera);
        long uploads = renderer.LastDiagnostics.ChunkUploads;
        for (int i = 0; i < 32; i++) { renderer.Prepare(camera, 1600, 900); renderer.Render(context, camera); }
        Assert.Equal(uploads, renderer.LastDiagnostics.ChunkUploads);
        renderer.Dispose(); renderer.Dispose();
        Assert.All(device.Inner.Buffers, b => Assert.Equal(1, b.DisposeCount));
        Assert.All(device.Textures, t => Assert.Equal(1, t.DisposeCount));
    }
}

internal sealed class TerrainTestDevice : IGraphicsDevice
{
    internal FakeGraphicsDevice Inner { get; } = new() { CaptureUploads = true };
    internal List<TerrainTestTexture> Textures { get; } = [];
    internal bool Recording;
    public bool IsRecording => Recording;
    public GraphicsDirectionalShadowSettings DirectionalShadows => new(DirectionalShadowQuality.High);
    public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => Inner.CreateGraphicsPipeline(description);
    public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => Inner.CreateBuffer(description);
    public IGraphicsTexture CreateTexture(GraphicsTextureData data) { var texture = new TerrainTestTexture(data); Textures.Add(texture); return texture; }
    public void RenderFrame(GraphicsColor clear, Action<IGraphicsCommandContext>? record = null) { Recording = true; try { record?.Invoke(new TerrainTestContext()); } finally { Recording = false; } }
    public void Resize(int width, int height) { }
    public void WaitForIdle() { }
    public void Dispose() { }
}
internal sealed class TerrainTestTexture(GraphicsTextureData data) : IGraphicsTexture
{
    public GraphicsTextureDescription Description => data.Description;
    internal int DisposeCount;
    public void Dispose() => DisposeCount++;
}

internal sealed class TerrainTestContext : IGraphicsCommandContext
{
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public int FrameIndex => 0;
    internal int Draws, TextureBindings, ConstantsBindings;
    internal long Indices;
    internal IGraphicsBuffer? LastIndex;
    public void SetViewport(float x, float y, float width, float height) { }
    public void SetScissor(int left, int top, int right, int bottom) { }
    public void SetPipeline(IGraphicsPipeline pipeline) { }
    public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) { }
    public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) => LastIndex = buffer;
    public void SetVertexConstants(ReadOnlySpan<float> values) => ConstantsBindings++;
    public void SetPixelTexture(int slot, IGraphicsTexture texture)
    {
        if (texture is TerrainTestTexture typed) Assert.Equal(0, typed.DisposeCount);
        TextureBindings++;
    }
    public void Draw(int vertexCount, int startVertex = 0) { }
    public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { Draws++; Indices += indexCount; }
    public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) => DrawIndexed(indexCount * instanceCount);
}
