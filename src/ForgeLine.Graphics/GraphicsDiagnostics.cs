namespace ForgeLine.Graphics;

public sealed record GraphicsResourceDiagnostics(
    int LoadedTextureCount,
    long ResidentTextureBytes,
    int ShaderResourceDescriptorsUsed,
    int ShaderResourceDescriptorCapacity,
    long TextureBindingFailureCount)
{
    public int PeakLoadedTextureCount { get; init; }

    public long PeakResidentTextureBytes { get; init; }

    public int PeakShaderResourceDescriptorsUsed { get; init; }

    public long TextureUploadCount { get; init; }

    public long TextureReleaseCount { get; init; }
}

public sealed record GraphicsDebugDiagnostics(
    bool Enabled,
    long WarningCount,
    long ErrorCount);

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

    public bool GpuTimingAvailable { get; init; }

    public double? GpuFrameMilliseconds { get; init; }

    public GraphicsDebugDiagnostics Debug { get; init; } =
        new(
            false,
            0,
            0);
}
