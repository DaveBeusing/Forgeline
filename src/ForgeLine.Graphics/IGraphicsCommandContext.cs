namespace ForgeLine.Graphics;

public interface IGraphicsCommandContext
{
    int Width { get; }

    int Height { get; }

    int FrameIndex { get; }

    void SetViewport(float x, float y, float width, float height);

    void SetScissor(int left, int top, int right, int bottom);
}
