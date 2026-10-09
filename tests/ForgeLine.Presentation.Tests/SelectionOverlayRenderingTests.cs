using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectionOverlayRenderingTests
{
    [Theory]
    [InlineData(96, false)]
    [InlineData(144, true)]
    [InlineData(192, false)]
    public void MarqueeIsClippedBlendedAndOutlinedWithReusableBuffer(uint dpi, bool reverse)
    {
        using var graphics = new RecordingDevice();
        using var renderer = new RtsInformationOverlayRenderer(graphics);
        var context = new RecordingContext();
        var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, []);
        AxisAlignedBounds bounds = new(new(-100f), new(100f));
        var camera = new RtsCamera();
        Vector2 first = new(-100f, -100f);
        Vector2 second = new(1700f, 1000f);
        var view = RtsInformationLayerView.Empty with
        {
            MinimapEnabled = false, IsDragSelecting = true,
            DragStart = reverse ? second : first, DragCurrent = reverse ? first : second
        };
        renderer.Render(context, camera, snapshot, bounds, view, CombatGroupOverviewView.Empty, dpi);
        Assert.True(graphics.Pipeline!.AlphaBlendEnabled);
        Vertex[] vertices = graphics.Buffer!.Vertices;
        Vertex[] fill = vertices.Where(v => v.Color.W == 0.10f).ToArray();
        Assert.Equal(6, fill.Length);
        Assert.Contains(fill, v => v.Position == new Vector2(-1, 1));
        Assert.Contains(fill, v => v.Position == new Vector2(1, -1));
        Assert.All(vertices, v =>
        {
            Assert.InRange(v.Position.X, -1f, 1f);
            Assert.InRange(v.Position.Y, -1f, 1f);
        });
        Assert.Equal(24, vertices.Count(v => v.Color.W == 0.95f));
        Assert.True(vertices.Count(v => v.Color.W == 1.0f) >= 24);
        var buffer = graphics.Buffer;
        renderer.Render(context, camera, snapshot, bounds, view with { IsDragSelecting = false }, CombatGroupOverviewView.Empty, dpi);
        if (renderer.LastRenderedVertexCount > 0)
        {
            Assert.DoesNotContain(graphics.Buffer!.Vertices, v => v.Color.W == 0.10f);
        }
        renderer.Render(context, camera, snapshot, bounds, view, CombatGroupOverviewView.Empty, dpi);
        Assert.Same(buffer, graphics.Buffer);
    }

    [Fact]
    public void FeedbackExpiresByFrameTimeAndAttackHasAdditionalShape()
    {
        var feedback = new RtsCommandFeedback();
        var draw = new DebugDraw { Enabled = true };
        feedback.Show(Vector3.Zero, valid: true);
        feedback.Draw(draw);
        Assert.Equal(22, draw.Lines.Length);
        draw.Clear();
        feedback.Show(Vector3.Zero, valid: true, attack: true);
        feedback.Draw(draw);
        Assert.Equal(34, draw.Lines.Length);
        draw.Clear();
        feedback.Show(Vector3.Zero, valid: false);
        feedback.Draw(draw);
        Assert.Equal(2, draw.Lines.Length);
        feedback.Advance(TimeSpan.FromMilliseconds(801));
        draw.Clear();
        feedback.Draw(draw);
        Assert.Empty(draw.Lines.ToArray());
        feedback.Show(Vector3.Zero, true);
        feedback.Clear();
        Assert.False(feedback.IsVisible);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(192)]
    public void SharedHudGeometryBlocksWorldInputButLeavesTheWorldAvailable(uint dpi)
    {
        var layout = GameplayHudLayout.Create(1920, 1080, dpi);
        foreach (var rect in new[] { layout.TopStatusBar, layout.SelectionInspector, layout.ActionDock, layout.Minimap })
        {
            Assert.True(HudInteractionContext.BlocksWorldPointer(new(rect.X + 1, rect.Y + 1), layout, true));
        }
        Assert.False(HudInteractionContext.BlocksWorldPointer(new(960, 540), layout, true));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector2 Position, Vector4 Color);

    private sealed class RecordingDevice : IGraphicsDevice
    {
        public GraphicsPipelineDescription? Pipeline { get; private set; }
        public RecordingBuffer? Buffer { get; private set; }
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description)
        {
            Pipeline = description;
            return new RecordingPipeline(description);
        }
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => Buffer = new(description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) => commands?.Invoke(new RecordingContext());
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }

    private sealed class RecordingPipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    {
        public GraphicsPipelineDescription Description => description;
        public void Dispose() { }
    }

    private sealed class RecordingBuffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        public Vertex[] Vertices { get; private set; } = [];
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged =>
            Vertices = MemoryMarshal.Cast<byte, Vertex>(MemoryMarshal.AsBytes(data)).ToArray();
        public void Dispose() { }
    }

    private sealed class RecordingContext : IGraphicsCommandContext
    {
        public int Width => 1600;
        public int Height => 900;
        public int FrameIndex => 0;
        public void SetViewport(float x, float y, float width, float height) { }
        public void SetScissor(int left, int top, int right, int bottom) { }
        public void SetPipeline(IGraphicsPipeline pipeline) { }
        public void SetVertexBuffer(IGraphicsBuffer buffer, int strideInBytes, int offsetInBytes = 0, int inputSlot = 0) { }
        public void SetIndexBuffer(IGraphicsBuffer buffer, GraphicsIndexFormat format, int offsetInBytes = 0) { }
        public void SetVertexConstants(ReadOnlySpan<float> values) { }
        public void SetPixelTexture(int slot, IGraphicsTexture texture) { }
        public void Draw(int vertexCount, int startVertex = 0) { }
        public void DrawIndexed(int indexCount, int startIndex = 0, int baseVertex = 0) { }
        public void DrawIndexedInstanced(int indexCount, int instanceCount, int startIndex = 0, int baseVertex = 0, int startInstance = 0) { }
    }
}
