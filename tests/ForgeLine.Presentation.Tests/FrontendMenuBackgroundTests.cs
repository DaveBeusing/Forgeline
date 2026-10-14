using ForgeLine.Assets;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class FrontendMenuBackgroundTests
{
    [Fact]
    public void DeferredLoadNeverBlocksFirstMenuAndCancellationCreatesNoGpuResource()
    {
        using var release = new ManualResetEventSlim(); using var entered = new ManualResetEventSlim();
        using var device = new Device();
        using var renderer = new FrontendMenuBackground(device, () => { entered.Set(); release.Wait(); return Data(); });
        Assert.Equal("Deferred", renderer.Diagnostics.State);
        renderer.Prepare(RenderQuality.High);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("Loading", renderer.Diagnostics.State);
        Assert.Empty(device.Textures);
        renderer.Dispose(); release.Set();
        Assert.Equal("Disposed", renderer.Diagnostics.State);
        Assert.Empty(device.Textures);
    }

    [Fact]
    public void QualityMipsUploadOnlyWhenChangedAndAllResourcesRetire()
    {
        using var device = new Device();
        using var renderer = new FrontendMenuBackground(device, Data);
        Complete(renderer, RenderQuality.High);
        Assert.True(renderer.Ready); Assert.Equal(64, device.Textures[^1].Description.Width);
        for (int i = 0; i < 50; i++) renderer.Prepare(RenderQuality.High);
        Assert.Single(device.Textures);
        renderer.Prepare(RenderQuality.Low);
        Assert.Equal(32, device.Textures[^1].Description.Width);
        Assert.True(device.Textures[0].Disposed);
        Assert.Equal(2, renderer.Diagnostics.Uploads);
        Assert.True(renderer.Diagnostics.ResidentBytes < 4096);
        renderer.Dispose(); Assert.All(device.Textures, texture => Assert.True(texture.Disposed));
        Assert.All(device.Pipelines, pipeline => Assert.True(pipeline.Disposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndCorruptOptionalArtUseFallbackOnce(bool corrupt)
    {
        using var device = new Device(); int reads = 0;
        using var renderer = new FrontendMenuBackground(device, () => { reads++; if (corrupt) throw new InvalidDataException("Reinstall optional menu image."); return null; });
        Complete(renderer, RenderQuality.Medium);
        Assert.Equal("Fallback", renderer.Diagnostics.State); Assert.NotNull(renderer.Diagnostics.Failure);
        for (int i = 0; i < 20; i++) renderer.Prepare(RenderQuality.High);
        Assert.Equal(1, reads); Assert.Empty(device.Textures);
    }

    [Fact]
    public void UnavailableTextureBackendCleansPipelineAndKeepsFallback()
    {
        using var device = new Device { FailTexture = true };
        using var renderer = new FrontendMenuBackground(device, Data);
        Complete(renderer, RenderQuality.Low);
        Assert.False(renderer.Ready); Assert.Equal("Fallback", renderer.Diagnostics.State);
        Assert.All(device.Pipelines, pipeline => Assert.True(pipeline.Disposed));
    }

    private static void Complete(FrontendMenuBackground renderer, RenderQuality quality)
    {
        Assert.True(SpinWait.SpinUntil(() => { renderer.Prepare(quality); return renderer.Diagnostics.State is "Ready" or "Fallback"; }, TimeSpan.FromSeconds(10)));
    }
    private static RuntimeTextureData Data()
    {
        var mips = new List<RuntimeTextureMipLevel>();
        for (int size = 64; size >= 1; size /= 2)
        { int pitch = ((size + 3) / 4) * 16; mips.Add(new(size, size, pitch, new byte[pitch * ((size + 3) / 4)])); }
        return new(64, 64, RuntimeTextureFormat.Bc7Unorm, RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage, mips);
    }
    private sealed class Device : IGraphicsDevice
    {
        public bool FailTexture { get; init; }
        public List<Texture> Textures { get; } = [];
        public List<Pipeline> Pipelines { get; } = [];
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsTexture CreateTexture(GraphicsTextureData data)
        { if (FailTexture) throw new NotSupportedException("Texture unavailable."); var texture = new Texture(data.Description); Textures.Add(texture); return texture; }
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description)
        { var pipeline = new Pipeline(description); Pipelines.Add(pipeline); return pipeline; }
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => throw new NotSupportedException();
        public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null) => throw new NotSupportedException();
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Texture(GraphicsTextureDescription description) : IGraphicsTexture
    { public GraphicsTextureDescription Description => description; public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
}
