using System.Collections.Immutable;
using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

internal readonly record struct RegionalPendingBound(
    RegionalObstacleKey Key,
    NavigationVersion SourceRevision,
    AxisAlignedBounds Bounds,
    ulong IntroducedTick);

internal enum RegionalBlockingStatus : byte
{
    InvalidInput = 0,
    Clear = 1,
    Blocked = 2,
    BudgetExhausted = 3,
    Stale = 4
}

internal readonly record struct RegionalBlockingResult(
    RegionalBlockingStatus Status,
    int WorkConsumed,
    RegionalObstacleKey? Blocker)
{
    public bool IsClear => Status == RegionalBlockingStatus.Clear;
}

/// <summary>
/// Immutable additional hard blockers for a still-published older raster.
/// Clear means this overlay only; terrain and current physical collision still apply.
/// </summary>
internal sealed class RegionalPendingBlocking
{
    private readonly RegionalGridLayout _layout;

    internal RegionalPendingBlocking(RegionalGridLayout layout, ulong generation,
        ulong captureTick, bool blockAll, ImmutableArray<RegionalPendingBound> bounds)
    {
        _layout = layout;
        Generation = generation;
        CaptureTick = captureTick;
        BlockAll = blockAll;
        Bounds = bounds;
    }

    public ulong Generation { get; }
    public ulong CaptureTick { get; }
    public bool BlockAll { get; }
    public ImmutableArray<RegionalPendingBound> Bounds { get; }
    public int BoundsCount => Bounds.Length;

    public RegionalBlockingResult CheckSegment(Vector3 start, Vector3 end,
        float sweptRadiusMeters, ulong expectedGeneration, int workAllowance)
    {
        if (!RegionalGridLayout.IsFinite(start) || !RegionalGridLayout.IsFinite(end) ||
            !float.IsFinite(sweptRadiusMeters) || sweptRadiusMeters < 0 ||
            sweptRadiusMeters > _layout.MaximumSweptRadiusMeters || workAllowance < 0)
            return new RegionalBlockingResult(RegionalBlockingStatus.InvalidInput, 0, null);
        if (expectedGeneration != Generation)
            return new RegionalBlockingResult(RegionalBlockingStatus.Stale, 0, null);
        if (workAllowance == 0)
            return new RegionalBlockingResult(RegionalBlockingStatus.BudgetExhausted, 0, null);
        if (BlockAll)
            return new RegionalBlockingResult(RegionalBlockingStatus.Blocked, 1, null);
        if (!ContainsSweep(start, end, sweptRadiusMeters))
            return new RegionalBlockingResult(RegionalBlockingStatus.InvalidInput, 1, null);

        int consumed = 1;
        float inflation = RegionalGridLayout.AddOutward(_layout.Baseline.Settings.StaticObstacleClearanceMeters, sweptRadiusMeters);
        foreach (RegionalPendingBound pending in Bounds)
        {
            if (consumed == workAllowance)
                return new RegionalBlockingResult(RegionalBlockingStatus.BudgetExhausted, consumed, null);
            consumed++;
            if (RegionalGridLayout.SegmentTouches(start, end, pending.Bounds, inflation))
                return new RegionalBlockingResult(RegionalBlockingStatus.Blocked, consumed, pending.Key);
        }
        return new RegionalBlockingResult(RegionalBlockingStatus.Clear, consumed, null);
    }

    private bool ContainsSweep(Vector3 start, Vector3 end, float clearance)
    {
        NavigationGrid grid = _layout.Baseline;
        if (!grid.TryWorldToCell(start, out _) || !grid.TryWorldToCell(end, out _)) return false;
        double maximumX = (double)grid.Origin.X + (double)grid.Width * grid.Settings.CellSizeMeters;
        double maximumZ = (double)grid.Origin.Y + (double)grid.Height * grid.Settings.CellSizeMeters;
        return (double)MathF.Min(start.X, end.X) - clearance >= grid.Origin.X &&
               (double)MathF.Min(start.Z, end.Z) - clearance >= grid.Origin.Y &&
               (double)MathF.Max(start.X, end.X) + clearance <= maximumX &&
               (double)MathF.Max(start.Z, end.Z) + clearance <= maximumZ;
    }
}
