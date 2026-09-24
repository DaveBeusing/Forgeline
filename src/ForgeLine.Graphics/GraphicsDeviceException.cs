namespace ForgeLine.Graphics;

public sealed class GraphicsDeviceException : Exception
{
    public GraphicsDeviceException(string message)
        : base(message)
    {
    }

    public GraphicsDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
