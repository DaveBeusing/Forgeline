namespace ForgeLine.Graphics;

public sealed class GraphicsDeviceException : Exception
{
    public string ReasonCode { get; } = "graphics-failure";

    public GraphicsDeviceException(string message, string reasonCode)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public GraphicsDeviceException(string message)
        : base(message)
    {
    }

    public GraphicsDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
