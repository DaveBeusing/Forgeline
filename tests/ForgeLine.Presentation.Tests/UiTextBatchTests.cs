using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class UiTextBatchTests
{
    [Fact]
    public void ContrastPipelineChangesOnlyPixelsAndReusesGeometryAtlasAndBuffers()
    {
        using var device = new Device();
        var batch = new UiTextBatch(device);
        var context = new RecordingContext();
        batch.Begin(); batch.Add("SUPPLY CRITICAL / ÄÖÜ 125%", 10, 10, 18, Vector4.One, 1280, 720);
        batch.Render(context);
        var normal = device.Buffers[0].Bytes.ToArray();
        var original = device.Buffers[0];
        batch.HighContrast = true;
        batch.Render(context);
        Assert.Equal(normal, device.Buffers[0].Bytes);
        Assert.Equal(2, device.Pipelines.Count);
        Assert.Equal(1, device.TextureCreates);
        Assert.Same(original, Assert.Single(device.Buffers));
        batch.HighContrast = false; batch.Render(context);
        batch.HighContrast = true; batch.Render(context);
        Assert.Equal(2, device.Pipelines.Count);
        Assert.Equal(0, batch.Diagnostics.MissingGlyphs);
        batch.Dispose();
        Assert.All(device.Pipelines, pipeline => Assert.True(pipeline.Disposed));
    }

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
    public void TranslatedGlyphsClipBothGeometryAndAtlasCoordinatesAtTheSharedHintBoundary()
    {
        using var device = new Device(); using var batch = new UiTextBatch(device);
        var clip = new HudRect(100, 110, 48, 16);
        batch.Clip = clip;
        batch.Begin(); batch.Add("MMMM", 80, 100, 32, Vector4.One, 1600, 900);
        Assert.True(batch.VertexCount > 0);
        batch.Render(new RecordingContext());
        var vertices = MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes)[..batch.VertexCount];
        foreach (var vertex in vertices)
        {
            float x = (vertex.Position.X + 1) * 800, y = (1 - vertex.Position.Y) * 450;
            Assert.InRange(x, clip.X - .001f, clip.Right + .001f);
            Assert.InRange(y, clip.Y - .001f, clip.Bottom + .001f);
            Assert.InRange(vertex.Uv.X, 0, 1); Assert.InRange(vertex.Uv.Y, 0, 1);
            Assert.Equal(1, vertex.Color.W);
        }
        batch.Clip = null;
        batch.Begin(); batch.Add("M", 5, 5, 32, Vector4.One, 1600, 900);
        Assert.True(batch.VertexCount > 0); // Subsequent HUD owners retain the full viewport.
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
    public void WarmCenteredControlTextDoesNotAllocateOrGrowResources()
    {
        using var device = new Device(); using var batch = new UiTextBatch(device);
        var rect = new HudRect(100, 100, 240, 64);
        for (int i = 0; i < 128; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, device.TextureCreates);
        Assert.Empty(device.Buffers);
        void Frame() { batch.Begin(); batch.AddCentered("ÄÖÜ / 999.9M\nSHIFT + B", rect, 16,
            Vector4.One, 1600, 900); }
    }

    [Theory]
    [InlineData("I")]
    [InlineData("WWW")]
    [InlineData("ÄÖÜ äöü ß")]
    [InlineData("1234567890")]
    [InlineData("POWER / CONSTRAINED\n999.9M / N/A")]
    public void CenteredControlsUseLogicalLineHeightAndInkOverhang(string text)
    {
        using var device = new Device(); using var batch = new UiTextBatch(device);
        var rect = new HudRect(100, 100, 360, 100);
        const float size = 20;
        batch.Begin(); batch.AddCentered(text, rect, size, Vector4.One, 1600, 900);
        batch.Render(new RecordingContext());
        Assert.Equal(0, batch.Diagnostics.MissingGlyphs);
        var vertices = MemoryMarshal.Cast<byte, Vertex>(device.Buffers[^1].Bytes);
        var font = UiFontMetrics.Default;
        var first = font.Glyph(text[0], false, out _);
        float factor = size / font.Description.PixelSize;
        float actualOriginX = (vertices[0].Position.X + 1) * 800 - first.Left * factor;
        float actualOriginY = (1 - vertices[0].Position.Y) * 450 -
            (font.Description.Ascent + first.Top) * factor;
        var line = font.MeasureLineBounds(text, size);
        Assert.InRange(MathF.Abs(actualOriginX + line.X + line.Width / 2 - (rect.X + rect.Width / 2)), 0, 1);
        int lines = text.Count(c => c == '\n') + 1;
        float blockHeight = lines * font.Description.LineHeight * factor;
        Assert.InRange(MathF.Abs(actualOriginY + blockHeight / 2 - (rect.Y + rect.Height / 2)), 0, 1);
        Assert.All(vertices.ToArray(), v =>
        {
            float x = (v.Position.X + 1) * 800, y = (1 - v.Position.Y) * 450;
            Assert.InRange(x, rect.X, rect.Right); Assert.InRange(y, rect.Y, rect.Bottom);
        });
    }

    [Fact]
    public void UnavailableFontTextureReleasesPipelineAndExplainsFallback()
    {
        using var device = new Device { FailTexture = true }; using var batch = new UiTextBatch(device);
        Assert.False(batch.Ready); Assert.Contains("Reinstall", batch.FailureReason, StringComparison.Ordinal);
        Assert.All(device.Pipelines, p => Assert.True(p.Disposed));
        Assert.False(batch.Add("Error", 0, 0, 12, Vector4.One, 1280, 720));
    }
    [Fact]
    public void FontFailureShowsAnActionableBitmapMessageWithoutReplacingInputGeometry()
    {
        using var device=new Device { FailTexture=true };
        using var frontend=new FrontendOverlayRenderer(device);
        frontend.Render(new RecordingContext(),FrontendSurfaceView.MainMenu([]));
        Assert.False(frontend.TextDiagnostics.AtlasReady);
        var region = frontend.LastLayout.Feedback;
        var context = new RecordingContext();
        Assert.Contains(MemoryMarshal.Cast<byte,SelectionOverlayRenderingTests.Vertex>(device.Buffers[^1].Bytes).ToArray(),
            v => v.Color == ForgeLineUiPalette.Warning &&
                v.Position.Y <= 1 - 2 * region.Y / context.Height &&
                v.Position.Y >= 1 - 2 * region.Bottom / context.Height);
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
