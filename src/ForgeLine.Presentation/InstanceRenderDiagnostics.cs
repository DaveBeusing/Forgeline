namespace ForgeLine.Presentation;

public readonly record struct InstanceRenderDiagnostics(
    int TotalInstances,
    int VisibleInstances,
    int CulledInstances,
    int DrawCalls,
    int HighLodInstances = 0,
    int ReducedLodInstances = 0,
    int RuntimeMeshInstances = 0,
    int TexturedRuntimeMeshInstances = 0,
    int FallbackMeshInstances = 0)
{
    public int Lod1Instances { get; init; }
    public int Lod2Instances { get; init; }
    public int AnimatedVegetationInstances { get; init; }
    public int VisibleVegetationInstances { get; init; }
    public int ClusterTests { get; init; }
    public int RejectedClusters { get; init; }
    public int ClusterCulledInstances { get; init; }
    public int InstanceBoundsTests { get; init; }
    public int SkippedDecalResources { get; init; }
    public ResourceDepositRenderDiagnostics ResourceDeposits { get; init; }
}
