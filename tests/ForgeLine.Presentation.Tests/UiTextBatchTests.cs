using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class UiTextBatchTests
{
    [Fact]
    public void AtlasLeaseAndFrameBuffersHaveExplicitLifetimes()
    {
        using var device = new Device();
        var first = new UiTextBatch(device); var second = new UiTextBatch(device);
        Assert.Equal(1, device.TextureCreates);
        first.Begin(); first.Add("Text", 0, 0, 12, Vector4.One, 1280, 720); first.Render(new RecordingContext());
        var original = device.Buffers[0];
        first.Begin(); first.Add(new string('W', 500), 0, 0, 12, Vector4.One, 10000, 720);
        device.FailBuffer = true;
        Assert.Throws<InvalidOperationException>(() => first.Render(new RecordingContext()));
        Assert.False(original.Disposed);
        device.FailBuffer = false; first.Render(new RecordingContext()); Assert.True(original.Disposed);
        first.Dispose(); Assert.False(device.Texture!.Disposed);
        second.Dispose(); Assert.True(device.Texture.Disposed);
        Assert.All(device.Buffers, b => Assert.True(b.Disposed));
        Assert.Throws<ObjectDisposedException>(() => first.Begin());
        using var third = new UiTextBatch(device); Assert.Equal(2, device.TextureCreates);
    }
    [Fact]
    public void MissingGlyphIsVisibleAndGeometryIsClippedBoundedAndAllocationFreeAfterWarmup()
    {
        using var device = new Device(); using var batch = new UiTextBatch(device);
        batch.Begin(); batch.Add("😀", 10, 10, 24, Vector4.One, 1280, 720);
        Assert.Equal(1, batch.Diagnostics.MissingGlyphs); Assert.Equal(6, batch.VertexCount);
        string text = new('W', 9000);
        batch.Begin(); batch.Add(text, 0, 0, 12, Vector4.One, 1000000, 720);
        Assert.Equal(8192 * 6, batch.VertexCount); Assert.True(batch.Diagnostics.DroppedGlyphs > 0);
        batch.Render(new RecordingContext());
        Assert.All(MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes).ToArray(), v =>
        { Assert.InRange(v.Position.X, -1, 1); Assert.InRange(v.Position.Y, -1, 1); Assert.InRange(v.Uv.X, 0, 1); Assert.InRange(v.Uv.Y, 0, 1); });
        batch.Begin(); batch.Add("AV ÄÖÜ äöü ß → 100%", 3, 3, 12, Vector4.One, 1280, 720);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) { batch.Begin(); batch.Add("AV ÄÖÜ äöü ß → 100%", 3, 3, 12, Vector4.One, 1280, 720); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Fact]
    public void AlignmentBaselineAndEllipsisUseSharedMetrics()
    {
        using var device = new Device(); using var batch = new UiTextBatch(device); var context = new RecordingContext();
        float Position(UiTextAlignment alignment, bool baseline = false)
        { batch.Begin(); batch.Add("AV", 100, 100, 24, Vector4.One, 1600, 900, 200, alignment: alignment, baseline: baseline); batch.Render(context); return MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes)[0].Position.X; }
        float left = Position(UiTextAlignment.Left), center = Position(UiTextAlignment.Center), right = Position(UiTextAlignment.Right);
        Assert.Equal(center - left, right - center, 5);
        float top = MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes)[0].Position.Y;
        _ = Position(UiTextAlignment.Right, true);
        Assert.True(MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes)[0].Position.Y > top);
        batch.Begin(); batch.Add("Very long localized value", 100, 100, 24, Vector4.One, 1600, 900, 35); batch.Render(context);
        Assert.InRange(batch.Diagnostics.Glyphs, 1, 3);
        Assert.All(MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes).ToArray(), v => Assert.True(v.Position.X <= (135f / 1600 * 2 - 1) + .0001f));
    }
    [Fact]
    public void UnavailableFontTextureReleasesPipelineAndExplainsFallback()
    {
        using var device = new Device { FailTexture = true }; using var batch = new UiTextBatch(device);
        Assert.False(batch.Ready); Assert.Contains("Reinstall", batch.FailureReason, StringComparison.Ordinal);
        Assert.All(device.Pipelines, p => Assert.True(p.Disposed));
        Assert.False(batch.Add("Error", 0, 0, 12, Vector4.One, 1280, 720));
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vertex(Vector2 Position, Vector4 Color, Vector2 Uv);
    private sealed class Device : IGraphicsDevice
    {
        public bool SupportsUiText => true; public bool FailBuffer { get; set; }
        public bool FailTexture { get; set; }
        public int TextureCreates { get; private set; }
        public Texture? Texture { get; private set; }
        public List<Buffer> Buffers { get; } = []; public List<Pipeline> Pipelines { get; } = [];
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription d) { var p = new Pipeline(d); Pipelines.Add(p); return p; }
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription d) { if (FailBuffer) throw new InvalidOperationException("Upload unavailable."); var b = new Buffer(d); Buffers.Add(b); return b; }
        public IGraphicsTexture CreateTexture(GraphicsTextureData d) { if (FailTexture) throw new NotSupportedException("Font texture unavailable."); TextureCreates++; return Texture = new Texture(d.Description); }
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) => commands?.Invoke(new RecordingContext());
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Texture(GraphicsTextureDescription description) : IGraphicsTexture
    { public GraphicsTextureDescription Description => description; public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description; public bool Disposed { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged => Bytes = MemoryMarshal.AsBytes(data).ToArray();
        public void Dispose() => Disposed = true;
    }
}
