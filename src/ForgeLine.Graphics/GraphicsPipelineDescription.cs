namespace ForgeLine.Graphics;

public enum GraphicsPrimitiveTopology
{
    TriangleList,
    LineList
}

public enum GraphicsCullMode
{
    CounterClockwise,
    None,
    Clockwise
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

    public GraphicsCullMode CullMode { get; init; } =
        GraphicsCullMode.CounterClockwise;

    public bool DepthEnabled { get; init; }

    public bool AlphaBlendEnabled { get; init; }

    public bool SceneDepthReceiver { get; init; }

    public bool VertexConstantsInBuffer { get; init; }
    public bool DirectionalShadowReceiver { get; init; }

    public GraphicsFrameTargetFormat TargetFormat { get; init; } = GraphicsFrameTargetFormat.Rgba8Unorm;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(VertexShader);
        ArgumentNullException.ThrowIfNull(PixelShader);
        ArgumentNullException.ThrowIfNull(VertexElements);
        if (TargetFormat is not (GraphicsFrameTargetFormat.Rgba8Unorm or GraphicsFrameTargetFormat.Rgba16Float or GraphicsFrameTargetFormat.Depth32Float))
            throw new ArgumentOutOfRangeException(nameof(TargetFormat));
        if (TargetFormat == GraphicsFrameTargetFormat.Depth32Float && (!DepthEnabled || AlphaBlendEnabled || DirectionalShadowReceiver))
            throw new ArgumentException("Shadow depth pipelines require depth writes and cannot blend or sample themselves.");
        if (DirectionalShadowReceiver && (!VertexConstantsInBuffer || VertexRootConstantCount == 0 || PixelTextureCount > 15))
            throw new ArgumentException("Shadow receivers use buffered frame constants and reserve texture register t15.");
        if (SceneDepthReceiver && (DepthEnabled || DirectionalShadowReceiver || PixelTextureCount > 15 || !AlphaBlendEnabled || TargetFormat == GraphicsFrameTargetFormat.Depth32Float))
            throw new ArgumentException("Scene-depth receivers require a blended color pipeline without depth writes and reserve t15.");
        if (TargetFormat == GraphicsFrameTargetFormat.Rgba16Float && AlphaBlendEnabled && !SceneDepthReceiver)
            throw new NotSupportedException("Linear blending requires the explicit depth-aware particle path.");

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

        if ((VertexConstantsInBuffer ? 2 : VertexRootConstantCount) +
            PixelTextureCount + (DirectionalShadowReceiver ? 3 : 0) + (SceneDepthReceiver ? 1 : 0) >
            64)
        {
            throw new ArgumentException(
                "D3D12 root-signature cost exceeds 64 DWORDs; root constants and pixel descriptor tables must fit within the hardware root-signature budget.");
        }

        if (!Enum.IsDefined(CullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CullMode),
                CullMode,
                "Unsupported graphics cull mode.");
        }

        foreach (GraphicsVertexElement element in VertexElements)
        {
            element.Validate();
        }
    }
}
