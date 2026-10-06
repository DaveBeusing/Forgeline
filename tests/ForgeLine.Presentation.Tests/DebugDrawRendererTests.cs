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

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            LastPipelineDescription =
                description;
            return new FakeGraphicsPipeline();
        }

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new FakeGraphicsBuffer();

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
        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsBuffer :
        IGraphicsBuffer
    {
        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
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
