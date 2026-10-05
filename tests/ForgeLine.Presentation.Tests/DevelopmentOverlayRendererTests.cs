using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DevelopmentOverlayRendererTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics = new();

    public void Dispose()
    {
        _graphics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DevelopmentMetricsRenderOnlyWhenEnabled()
    {
        using var renderer =
            new DevelopmentOverlayRenderer(
                _graphics);
        var context =
            new FakeGraphicsCommandContext();
        var metrics =
            new DevelopmentOverlayMetrics(
                FramesPerSecond: 60.0,
                FrameMilliseconds: 16.67,
                CpuRenderMilliseconds: 4.0,
                SimulationTick: 120,
                SimulationTickMilliseconds: 2.0,
                SimulationEntityCount: 500,
                VisibleTerrainChunks: 16,
                TotalTerrainChunks: 64,
                DrawCalls: 20,
                RenderedInstances: 300,
                TotalInstances: 500,
                JobExecutionMilliseconds: 1.0,
                TotalAllocatedBytes: 1024,
                HeapSizeBytes: 2048,
                Gen0Collections: 1,
                Gen1Collections: 0,
                Gen2Collections: 0);

        renderer.Render(
            context,
            metrics,
            showDevelopmentMetrics: false);
        int hiddenVertexCount =
            renderer.LastRenderedVertexCount;

        renderer.Render(
            context,
            metrics,
            showDevelopmentMetrics: true);

        Assert.Equal(
            0,
            hiddenVertexCount);
        Assert.True(
            renderer.LastRenderedVertexCount >
            hiddenVertexCount);
    }

    private sealed class FakeGraphicsDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new FakeGraphicsPipeline(description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new FakeGraphicsBuffer(description);

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(
                new FakeGraphicsCommandContext());
        }

        public void Resize(int width, int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsPipeline : IGraphicsPipeline
    {
        public FakeGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description = description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsBuffer : IGraphicsBuffer
    {
        public FakeGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description = description;
        }

        public GraphicsBufferDescription Description { get; }

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
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
        }
    }
}
