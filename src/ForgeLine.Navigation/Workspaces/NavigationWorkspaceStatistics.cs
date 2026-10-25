namespace ForgeLine.Navigation;

/// <summary>
/// Accounted bytes bound reachable managed arena capacity, with conservative overhead.
/// Garbage collection timing, process working set, and unrelated kernel allocations
/// are outside this accounting. The pool object allowance remains after disposal.
/// </summary>
internal readonly record struct NavigationWorkspaceStatistics(
    int ActiveWorkspaces,
    int RetainedWorkspaces,
    int PeakActiveWorkspaces,
    long ActiveAccountedBytes,
    long RetainedAccountedBytes,
    long PoolOverheadBytes,
    long TotalAccountedBytes,
    long PeakAccountedBytes,
    long RentedCount,
    long ReusedCount,
    long ReturnedCount,
    long ReleasedCount,
    long ActiveLimitRejections,
    long MemoryLimitRejections,
    long InitializedBytes,
    bool IsDisposed);
