namespace ForgeLine.Graphics;

public enum GraphicsPrimitiveTopology
{
    TriangleList,
    LineList
}

public sealed record GraphicsPipelineDescription(
    GraphicsShaderBytecode VertexShader,
    GraphicsShaderBytecode PixelShader)
{
    public IReadOnlyList<GraphicsVertexElement> VertexElements { get; init; } =
        Array.Empty<GraphicsVertexElement>();

    public int VertexRootConstantCount { get; init; }

    public int PixelTextureCount { get; init; }

    public GraphicsPrimitiveTopology PrimitiveTopology { get; init; } =
        GraphicsPrimitiveTopology.TriangleList;

    public bool DepthEnabled { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(VertexShader);
        ArgumentNullException.ThrowIfNull(PixelShader);
        ArgumentNullException.ThrowIfNull(VertexElements);

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

        if (VertexRootConstantCount < 0 || VertexRootConstantCount > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VertexRootConstantCount),
                "Vertex root constants must use between zero and 64 32-bit values.");
        }

        if (PixelTextureCount < 0 || PixelTextureCount > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PixelTextureCount),
                "Pixel texture count must be between zero and sixteen.");
        }

        if (VertexRootConstantCount +
            PixelTextureCount >
            64)
        {
            throw new ArgumentException(
                "D3D12 root-signature cost exceeds 64 DWORDs; root constants and pixel descriptor tables must fit within the hardware root-signature budget.");
        }

        foreach (GraphicsVertexElement element in VertexElements)
        {
            element.Validate();
        }
    }
}
