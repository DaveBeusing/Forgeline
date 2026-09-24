namespace ForgeLine.Graphics;

public interface IGraphicsBuffer : IDisposable
{
    GraphicsBufferDescription Description { get; }

    void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0)
        where T : unmanaged;
}
