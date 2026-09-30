namespace ForgeLine.Presentation;

public readonly record struct InstanceRenderDiagnostics(
    int TotalInstances,
    int VisibleInstances,
    int CulledInstances,
    int DrawCalls,
    int HighLodInstances = 0,
    int ReducedLodInstances = 0);
