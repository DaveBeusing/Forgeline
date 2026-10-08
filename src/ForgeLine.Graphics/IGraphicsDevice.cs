namespace ForgeLine.Graphics;

public interface IGraphicsDevice : IDisposable
{
    // The device and its resources have one creating-thread owner. Command contexts
    // are valid only inside RenderFrame's callback. A frame index is reusable only
    // after its previous GPU submission completes. Resource disposal invalidates
    // the wrapper immediately; native release follows its final GPU use.
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
