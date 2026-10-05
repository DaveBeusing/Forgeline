namespace ForgeLine.Graphics;

public interface IGraphicsDevice : IDisposable
{
    GraphicsDiagnostics Diagnostics { get; }

    IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description);

    IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description);

    IGraphicsTexture CreateTexture(
        GraphicsTextureData texture) =>
        throw new NotSupportedException(
            "This graphics device does not support textures.");

    void RenderFrame(
        GraphicsColor clearColor,
        Action<IGraphicsCommandContext>? recordCommands = null);

    void Resize(int width, int height);

    void WaitForIdle();
}
