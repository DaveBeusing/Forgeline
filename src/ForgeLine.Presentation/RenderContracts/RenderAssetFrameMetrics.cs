using ForgeLine.Simulation;

namespace ForgeLine.Presentation.RenderContracts;

public readonly record struct RenderAssetFrameMetrics(
    ulong FrameSequence, SimulationSessionId SessionId, ulong SnapshotTick,
    double? PrepareMilliseconds, double? RecordMilliseconds,
    double? SubmitMilliseconds, double? RetireMilliseconds, ulong? SubmissionFence,
    double? PresentMilliseconds = null, double? WaitMilliseconds = null,
    long? UploadedTextureCount = null, long? UploadedTextureBytes = null,
    long? LogicalTextureBytes = null, ulong? ObservedDxgiLocalUsageBytes = null,
    string? FailureStatus = null);
