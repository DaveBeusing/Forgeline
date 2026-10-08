using ForgeLine.Graphics;
using System.Runtime.InteropServices;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class FrontendOverlayRendererTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics = new();

    public void Dispose()
    {
        _graphics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BootstrapBrandIsVisibleAtTimeZeroWithoutTextures()
    {
        using var renderer = new FrontendOverlayRenderer(_graphics);
        var context = new FakeGraphicsCommandContext();
        renderer.Render(context, FrontendSurfaceView.StudioSplash(0));
        int visibleBrandVertices = _graphics.LastBuffer!.VisibleBrandVertices;
        renderer.Render(context, FrontendSurfaceView.StudioSplashBootstrap());
        Assert.Equal(0, visibleBrandVertices);
        Assert.True(_graphics.LastBuffer.VisibleBrandVertices > 0);
        Assert.True(FrontendSurfaceView.StudioSplashBootstrap().SplashBootstrap);
    }

    [Fact]
    public void BitmapFontRendersEveryUppercaseAsciiLetter()
    {
        using var renderer =
            new FrontendOverlayRenderer(_graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            FrontendSurfaceView.Detail(
                " ",
                [],
                string.Empty,
                secondaryAction: string.Empty));
        int blankVertexCount =
            renderer.LastRenderedVertexCount;

        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            renderer.Render(
                context,
                FrontendSurfaceView.Detail(
                    letter.ToString(),
                    [],
                    string.Empty,
                    secondaryAction: string.Empty));

            Assert.True(
                renderer.LastRenderedVertexCount > blankVertexCount,
                $"Expected bitmap font glyph for '{letter}'.");
        }
    }

    [Fact]
    public void BitmapFontRendersPercentageSymbol()
    {
        using var renderer =
            new FrontendOverlayRenderer(_graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            FrontendSurfaceView.Detail(
                " ",
                [],
                string.Empty,
                secondaryAction: string.Empty));
        int blankVertexCount =
            renderer.LastRenderedVertexCount;

        renderer.Render(
            context,
            FrontendSurfaceView.Detail(
                "%",
                [],
                string.Empty,
                secondaryAction: string.Empty));

        Assert.True(
            renderer.LastRenderedVertexCount >
            blankVertexCount);
    }

    [Fact]
    public void MainMenuRendersProductVersionWhenProvided()
    {
        using var renderer =
            new FrontendOverlayRenderer(_graphics);
        var context =
            new FakeGraphicsCommandContext();

        renderer.Render(
            context,
            FrontendSurfaceView.MainMenu(
                [],
                string.Empty));
        int withoutVersion =
            renderer.LastRenderedVertexCount;

        renderer.Render(
            context,
            FrontendSurfaceView.MainMenu(
                [],
                "0.1.1"));

        Assert.True(
            renderer.LastRenderedVertexCount >
            withoutVersion);
    }

    private sealed class FakeGraphicsDevice : IGraphicsDevice
    {
        internal FakeGraphicsBuffer? LastBuffer { get; private set; }
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new FakeGraphicsPipeline(description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            LastBuffer = new FakeGraphicsBuffer(description);

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
        internal int VisibleBrandVertices { get; private set; }
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
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
            VisibleBrandVertices = 0;
            // Position float2 followed by Color float4; first six vertices are the backdrop.
            for (int offset = 6 * 24; offset < bytes.Length; offset += 24)
                if (BitConverter.ToSingle(bytes.Slice(offset + 8, 4)) > 0.5f)
                    VisibleBrandVertices++;
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

        public void SetPipeline(IGraphicsPipeline pipeline)
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
