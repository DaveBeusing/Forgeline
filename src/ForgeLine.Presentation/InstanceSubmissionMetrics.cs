namespace ForgeLine.Presentation;

// Read on the render owner after a successful submission. No per-frame sampling cost.
public readonly record struct InstanceSubmissionMetrics(
    int StagingCapacity,
    long BatchInstanceCapacity,
    int BatchCapacity,
    int SubmittedInstances,
    long UploadBytes,
    int PipelineBindings,
    int TextureBindings)
{
    public int SkinEvaluatedPoses { get; init; }
    public int SkinStaticFallbacks { get; init; }
    public long SkinUploadBytes { get; init; }
    public long SnapshotIndexBytes { get; init; }
    public int MechanicalPartInstances { get; init; }
    public int MechanicalTransformScratchBytes { get; init; }
}
