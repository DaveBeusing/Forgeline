namespace ForgeLine.Presentation;

public readonly record struct DevelopmentOverlayMetrics(
    double FramesPerSecond,
    double FrameMilliseconds,
    double CpuRenderMilliseconds,
    ulong SimulationTick,
    double SimulationTickMilliseconds,
    int SimulationEntityCount,
    int VisibleTerrainChunks,
    int TotalTerrainChunks,
    int DrawCalls,
    int RenderedInstances,
    int TotalInstances,
    double JobExecutionMilliseconds,
    long TotalAllocatedBytes,
    long HeapSizeBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
