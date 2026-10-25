using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

// Private storage keys. The shared contract adapter supplies its source domain,
// value and complete generation; this type is not a second public contract.
internal readonly record struct RegionalObstacleKey(byte Source, ulong Value, ulong Generation)
    : IComparable<RegionalObstacleKey>
{
    public bool IsValid => (Source is 1 or 2) && Generation != 0;

    public int CompareTo(RegionalObstacleKey other)
    {
        int result = Source.CompareTo(other.Source);
        if (result == 0) result = Value.CompareTo(other.Value);
        return result == 0 ? Generation.CompareTo(other.Generation) : result;
    }

    public static bool operator <(RegionalObstacleKey left, RegionalObstacleKey right) => left.CompareTo(right) < 0;
    public static bool operator >(RegionalObstacleKey left, RegionalObstacleKey right) => left.CompareTo(right) > 0;
    public static bool operator <=(RegionalObstacleKey left, RegionalObstacleKey right) => left.CompareTo(right) <= 0;
    public static bool operator >=(RegionalObstacleKey left, RegionalObstacleKey right) => left.CompareTo(right) >= 0;
}

internal enum RegionalObstacleLayer : byte
{
    Authored = 1,
    Structure = 2,
    PhysicalWreck = 3
}

internal readonly record struct RegionalObstacleRecord(
    RegionalObstacleKey Key,
    NavigationVersion SourceRevision,
    RegionalObstacleLayer Layer,
    AxisAlignedBounds? Bounds);

internal readonly record struct RegionalObstacleEdit(
    RegionalObstacleKey Key,
    NavigationVersion SourceRevision,
    RegionalObstacleLayer Layer,
    AxisAlignedBounds? PreviousBounds,
    AxisAlignedBounds? CurrentBounds);

internal enum RegionalQueueStatus : byte
{
    Accepted,
    NoGeometryChange,
    Stale,
    InvalidInput,
    CapacityExceeded,
    ResynchronizationRequired
}

internal enum RegionalRebuildReason : byte
{
    None,
    InitialPopulation,
    LargeDirtyCoverage,
    DirtyAggregationLimit,
    ConflictingSource,
    CapacityExceeded
}

// MaximumSweptRadiusMeters includes the complete horizontal vehicle envelope
// and its additional clearance, not only the margin of a vehicle profile.
internal sealed record RegionalUpdateLimits(
    int MaximumWorkPerTick,
    int MaximumChangesPerTick,
    int MaximumTrackedObstacles,
    int MaximumPendingBounds,
    int MaximumRetainedSnapshots,
    int MaximumDiagnosticRegions,
    int MaximumRouteDependencyRegions,
    int FullRebuildRegionPercent,
    float MaximumSweptRadiusMeters)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumWorkPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumChangesPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTrackedObstacles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPendingBounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRetainedSnapshots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDiagnosticRegions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRouteDependencyRegions);
        ArgumentOutOfRangeException.ThrowIfLessThan(FullRebuildRegionPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FullRebuildRegionPercent, 100);
        if (!float.IsFinite(MaximumSweptRadiusMeters) || MaximumSweptRadiusMeters < 0)
            throw new InvalidOperationException("Maximum swept footprint radius must be finite and non-negative.");
    }
}

internal readonly record struct RegionalCellRange(int MinimumX, int MinimumZ, int EndX, int EndZ)
{
    public int Width => Math.Max(0, EndX - MinimumX);
    public int Height => Math.Max(0, EndZ - MinimumZ);
    public bool IsEmpty => MinimumX >= EndX || MinimumZ >= EndZ;
}
