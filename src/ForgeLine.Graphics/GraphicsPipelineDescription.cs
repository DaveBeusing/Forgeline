namespace ForgeLine.Graphics;

public sealed record GraphicsPipelineDescription(
    GraphicsShaderBytecode VertexShader,
    GraphicsShaderBytecode PixelShader)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(VertexShader);
        ArgumentNullException.ThrowIfNull(PixelShader);

        if (VertexShader.Stage != GraphicsShaderStage.Vertex)
        {
            throw new ArgumentException(
                "The vertex-shader bytecode must use the vertex stage.",
                nameof(VertexShader));
        }

        if (PixelShader.Stage != GraphicsShaderStage.Pixel)
        {
            throw new ArgumentException(
                "The pixel-shader bytecode must use the pixel stage.",
                nameof(PixelShader));
        }
    }
}
