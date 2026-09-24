namespace ForgeLine.Graphics;

public interface IGraphicsDevice : IDisposable
{
    GraphicsDiagnostics Diagnostics { get; }

    IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description);

    IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description);

    void RenderFrame(
        GraphicsColor clearColor,
        Action<IGraphicsCommandContext>? recordCommands = null);

    void Resize(int width, int height);

    void WaitForIdle();
}
