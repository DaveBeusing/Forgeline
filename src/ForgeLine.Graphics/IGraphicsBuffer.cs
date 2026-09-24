namespace ForgeLine.Graphics;

public interface IGraphicsBuffer : IDisposable
{
    GraphicsBufferDescription Description { get; }
}
