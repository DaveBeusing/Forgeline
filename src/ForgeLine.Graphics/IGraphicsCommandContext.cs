namespace ForgeLine.Graphics;

public interface IGraphicsCommandContext
{
    int Width { get; }

    int Height { get; }

    int FrameIndex { get; }

    void SetViewport(float x, float y, float width, float height);

    void SetScissor(int left, int top, int right, int bottom);

    void SetPipeline(IGraphicsPipeline pipeline);

    void SetVertexBuffer(
        IGraphicsBuffer buffer,
        int strideInBytes,
        int offsetInBytes = 0);

    void SetIndexBuffer(
        IGraphicsBuffer buffer,
        GraphicsIndexFormat format,
        int offsetInBytes = 0);

    void SetVertexConstants(ReadOnlySpan<float> values);

    void Draw(int vertexCount, int startVertex = 0);

    void DrawIndexed(
        int indexCount,
        int startIndex = 0,
        int baseVertex = 0);
}
