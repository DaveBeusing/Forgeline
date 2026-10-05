namespace ForgeLine.Graphics;

public sealed record GraphicsResourceDiagnostics(
    int LoadedTextureCount,
    long ResidentTextureBytes,
    int ShaderResourceDescriptorsUsed,
    int ShaderResourceDescriptorCapacity,
    long TextureBindingFailureCount);

public sealed record GraphicsDiagnostics(
    GraphicsDeviceInfo Device,
    GraphicsSurfaceInfo Surface)
{
    public GraphicsResourceDiagnostics Resources { get; init; } =
        new(
            0,
            0,
            0,
            0,
            0);
}
