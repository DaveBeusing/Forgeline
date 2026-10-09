using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DebugDrawRendererTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics =
        new();

    public void Dispose()
    {
        _graphics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RendererUsesRequestedDepthPolicy(
        bool depthEnabled)
    {
        using var renderer =
            new DebugDrawRenderer(
                _graphics,
                depthEnabled);

        Assert.Equal(
            depthEnabled,
            _graphics.LastPipelineDescription?.DepthEnabled);
    }

    [Fact]
    public void ThousandPlayerRingsUseOneBlendedBatchAndReuseTheirBuffer()
    {
        using var renderer = new DebugDrawRenderer(_graphics, depthEnabled: false, lineWidthPixels: 3.0f);
        var draw = new DebugDraw { Enabled = true };
        for (int index = 0; index < 1000; index++)
        {
            draw.Circle(Vector3.Zero, 4.0f, Vector4.One, 24);
        }
        var context = new FakeGraphicsCommandContext();
        renderer.Render(context, new RtsCamera(), draw);
        Assert.Equal(1, context.DrawCalls);
        Assert.Equal(24_000, renderer.LastDiagnostics.RenderedLines);
        Assert.Equal(0, renderer.LastDiagnostics.DroppedLines);
        Assert.Equal(GraphicsPrimitiveTopology.TriangleList, _graphics.LastPipelineDescription!.PrimitiveTopology);
        Assert.True(_graphics.LastPipelineDescription.AlphaBlendEnabled);
        Assert.False(_graphics.LastPipelineDescription.DepthEnabled);
        var buffer = _graphics.LastBuffer;
        Assert.Equal(24_000 * 6 * 40, buffer!.Data.Length);
        renderer.Render(context, new RtsCamera(), draw, uiScale: 2.0f);
        Assert.Same(buffer, _graphics.LastBuffer);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void PlayerLineWidthIsMeasuredInPixels(float scale)
    {
        using var renderer = new DebugDrawRenderer(_graphics, false, 3.0f);
        var draw = new DebugDraw { Enabled = true };
        draw.Line(new(-4, 0, 0), new(4, 0, 0), Vector4.One);
        renderer.Render(new FakeGraphicsCommandContext(), new RtsCamera(), draw, scale);
        ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(_graphics.LastBuffer!.Data);
        Vector2 first = new(values[0] / values[3] * 800, values[1] / values[3] * 450);
        Vector2 second = new(values[10] / values[13] * 800, values[11] / values[13] * 450);
        Assert.InRange(Vector2.Distance(first, second), 3.0f * scale - 0.01f, 3.0f * scale + 0.01f);
    }

    [Fact]
    public void DisabledDrawSkipsSubmission()
    {
        using var renderer =
            new DebugDrawRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();
        var draw =
            new DebugDraw();

        renderer.Render(
            context,
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false
                }),
            draw);

        Assert.Equal(
            0,
            context.DrawCalls);
        Assert.Equal(
            default,
            renderer.LastDiagnostics);
    }

    private sealed class FakeGraphicsDevice :
        IGraphicsDevice
    {
        public GraphicsPipelineDescription? LastPipelineDescription
        {
            get;
            private set;
        }

        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public FakeGraphicsBuffer? LastBuffer { get; private set; }

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            LastPipelineDescription =
                description;
            return new FakeGraphicsPipeline(
                description);
        }

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            LastBuffer = new FakeGraphicsBuffer(
                description);

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(
                new FakeGraphicsCommandContext());
        }

        public void Resize(
            int width,
            int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsPipeline :
        IGraphicsPipeline
    {
        public FakeGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description =
                description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsBuffer :
        IGraphicsBuffer
    {
        public FakeGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description =
                description;
        }

        public GraphicsBufferDescription Description { get; }

        public byte[] Data { get; private set; } = [];

        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
            Data = MemoryMarshal.AsBytes(data).ToArray();
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public int DrawCalls { get; private set; }

        public void SetViewport(
            float x,
            float y,
            float width,
            float height)
        {
        }

        public void SetScissor(
            int left,
            int top,
            int right,
            int bottom)
        {
        }

        public void SetPipeline(
            IGraphicsPipeline pipeline)
        {
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
        }

        public void SetVertexConstants(
            ReadOnlySpan<float> values)
        {
        }

        public void Draw(
            int vertexCount,
            int startVertex = 0)
        {
            DrawCalls++;
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
            DrawCalls++;
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
            DrawCalls++;
        }
    }
}
