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
}
