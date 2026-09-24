namespace ForgeLine.Graphics;

public sealed class GraphicsShaderCompilationException : Exception
{
    public GraphicsShaderCompilationException(string message)
        : base(message)
    {
    }
}
