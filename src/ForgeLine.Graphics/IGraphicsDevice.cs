namespace ForgeLine.Graphics;

public interface IGraphicsDevice : IDisposable
{
    GraphicsDiagnostics Diagnostics { get; }

    void RenderFrame(GraphicsColor clearColor);

    void Resize(int width, int height);

    void WaitForIdle();
}
