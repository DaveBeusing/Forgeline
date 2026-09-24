namespace ForgeLine.Simulation;

public readonly record struct ManagedRuntimeMetrics(
    long TotalAllocatedBytes,
    long HeapSizeBytes,
    long MemoryLoadBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public static ManagedRuntimeMetrics Capture()
    {
        GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();

        return new ManagedRuntimeMetrics(
            GC.GetTotalAllocatedBytes(precise: false),
            memoryInfo.HeapSizeBytes,
            memoryInfo.MemoryLoadBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }
}
