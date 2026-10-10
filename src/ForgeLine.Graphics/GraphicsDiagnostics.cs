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
    public GraphicsFrameDiagnostics? Frame { get; init; }
    public GraphicsMemoryDiagnostics? Memory { get; init; }
    public GraphicsHealthDiagnostics Health { get; init; } = new(0, 0, 0, 0, 0, 0, null);
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

public readonly record struct GraphicsFrameDiagnostics(
    GraphicsFramePlan Plan,
    ulong CpuSubmission,
    double WorldCpuMilliseconds,
    double? OverlayCpuMilliseconds,
    ulong GpuSubmissionFence,
    double? WorldGpuMilliseconds,
    double? OverlayGpuMilliseconds)
{
    public double? CompositeCpuMilliseconds { get; init; }
    public int CompositeDrawCalls => CompositeCpuMilliseconds.HasValue ? 1 : 0;
    public double? CompositeGpuMilliseconds { get; init; }
    public string? GpuTimingUnavailableReason { get; init; }
    public string? IntermediateUnavailableReason { get; init; } = "Linear scene composition is disabled.";
}

public sealed record GraphicsMemoryDiagnostics(
    bool Available,
    ulong LocalBudgetBytes,
    ulong LocalUsageBytes,
    ulong NonLocalBudgetBytes,
    ulong NonLocalUsageBytes,
    string? FailureReason);

public readonly record struct GraphicsHealthDiagnostics(
    int LiveResourceCount,
    int PendingRetirementCount,
    int PeakPendingRetirementCount,
    long ReleasedResourceCount,
    long FenceWaitCount,
    long SubmissionFaultCount,
    string? FailureReasonCode);
