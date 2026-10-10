namespace ForgeLine.Graphics;

public interface IGraphicsCommandContext
{
    int Width { get; }

    int Height { get; }

    int FrameIndex { get; }

    // Markers preserve the direct-output submission path. World precedes Overlay;
    // repeating the current marker is harmless. Native contexts validate ordering.
    void BeginPass(GraphicsFramePass pass) { }

    void SetViewport(float x, float y, float width, float height);

    void SetScissor(int left, int top, int right, int bottom);

    void SetPipeline(IGraphicsPipeline pipeline);

    void SetVertexBuffer(
        IGraphicsBuffer buffer,
        int strideInBytes,
        int offsetInBytes = 0,
        int inputSlot = 0);

    void SetIndexBuffer(
        IGraphicsBuffer buffer,
        GraphicsIndexFormat format,
        int offsetInBytes = 0);

    void SetVertexConstants(ReadOnlySpan<float> values);

    void SetPixelTexture(
        int slot,
        IGraphicsTexture texture) =>
        throw new NotSupportedException(
            "This graphics command context does not support texture binding.");

    void Draw(int vertexCount, int startVertex = 0);

    void DrawIndexed(
        int indexCount,
        int startIndex = 0,
        int baseVertex = 0);

    void DrawIndexedInstanced(
        int indexCount,
        int instanceCount,
        int startIndex = 0,
        int baseVertex = 0,
        int startInstance = 0);
}
