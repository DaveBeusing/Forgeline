using System.Collections.Immutable;
using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation.Regional;

/// <summary>A conservative proof for actual segments, not their enclosing rectangle.</summary>
internal sealed class RegionalRouteDependencies
{
    private readonly record struct RegionProof(NavigationVersion Revision, object Identity);

    private readonly RegionalGridLayout _layout;
    private readonly ImmutableArray<Vector3> _waypoints;
    private readonly ImmutableSortedDictionary<int, RegionProof> _regions;
    private readonly float _clearance;

    private RegionalRouteDependencies(RegionalGridLayout layout, ImmutableArray<Vector3> waypoints,
        ImmutableSortedDictionary<int, RegionProof> regions, float clearance, bool complete)
    {
        _layout = layout;
        _waypoints = waypoints;
        _regions = regions;
        _clearance = clearance;
        IsComplete = complete;
    }

    public bool IsComplete { get; }
    public int RegionCount => _regions.Count;

    public static RegionalRouteDependencies Capture(RegionalRasterSnapshot snapshot,
        ReadOnlySpan<Vector3> waypoints, float sweptRadiusMeters, int maximumRegions,
        int workAllowance, out int consumed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRegions);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        consumed = 0;
        RegionalGridLayout layout = snapshot.Layout;
        var regions = ImmutableSortedDictionary<int, RegionProof>.Empty;
        if (waypoints.IsEmpty || waypoints.Length > workAllowance ||
            !float.IsFinite(sweptRadiusMeters) || sweptRadiusMeters < 0 ||
            sweptRadiusMeters > layout.MaximumSweptRadiusMeters)
            return new RegionalRouteDependencies(layout, [], regions, sweptRadiusMeters, false);

        foreach (Vector3 point in waypoints)
        {
            consumed++;
            if (!layout.Baseline.TryWorldToCell(point, out _))
                return new RegionalRouteDependencies(layout, [], regions, sweptRadiusMeters, false);
        }

        float halo = RegionalGridLayout.AddOutward(sweptRadiusMeters, layout.Baseline.Settings.StaticObstacleClearanceMeters);
        if (!float.IsFinite(halo))
            return new RegionalRouteDependencies(layout, [], regions, sweptRadiusMeters, false);
        bool complete = true;
        int segmentCount = Math.Max(1, waypoints.Length - 1);
        for (int segment = 0; segment < segmentCount && complete; segment++)
        {
            Vector3 start = waypoints[segment];
            Vector3 end = waypoints[Math.Min(segment + 1, waypoints.Length - 1)];
            var envelope = new AxisAlignedBounds(Vector3.Min(start, end), Vector3.Max(start, end));
            RegionalCellRange cells = layout.GetOverlap(envelope, halo, includeRoundingHalo: true);
            if (cells.IsEmpty)
            {
                complete = false;
                break;
            }
            int firstX = cells.MinimumX / layout.RegionSizeCells;
            int firstZ = cells.MinimumZ / layout.RegionSizeCells;
            int lastX = (cells.EndX - 1) / layout.RegionSizeCells;
            int lastZ = (cells.EndZ - 1) / layout.RegionSizeCells;
            for (int z = firstZ; z <= lastZ && complete; z++)
            {
                for (int x = firstX; x <= lastX; x++)
                {
                    if (consumed == workAllowance)
                    {
                        complete = false;
                        break;
                    }
                    consumed++;
                    int index = z * layout.Width + x;
                    if (!RegionalGridLayout.SegmentTouches(start, end, layout.GetRegionBounds(index), halo))
                        continue;
                    if (!regions.ContainsKey(index))
                    {
                        if (regions.Count == maximumRegions)
                        {
                            complete = false;
                            break;
                        }
                        regions = regions.Add(index,
                            new RegionProof(snapshot.GetRegionRevision(index), snapshot.GetRegionIdentity(index)));
                    }
                }
            }
        }

        return new RegionalRouteDependencies(layout,
            complete ? ImmutableArray.CreateRange(waypoints.ToArray()) : [],
            regions, sweptRadiusMeters, complete);
    }

    public bool Matches(RegionalRasterSnapshot current, int workAllowance, out int consumed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        consumed = 0;
        if (!IsComplete || !ReferenceEquals(current.Layout, _layout)) return false;
        foreach (KeyValuePair<int, RegionProof> region in _regions)
        {
            if (consumed == workAllowance) return false;
            consumed++;
            // Runtime-only immutable page identity prevents a proof from a
            // discarded timeline matching a reused scalar revision after restore.
            if (current.GetRegionRevision(region.Key) != region.Value.Revision ||
                !ReferenceEquals(current.GetRegionIdentity(region.Key), region.Value.Identity)) return false;
        }
        return true;
    }

    // The coordinator must check these segments against its LATEST pending view
    // at adoption/movement time; a worker's earlier overlay is not authority.
    public ReadOnlySpan<Vector3> Waypoints => _waypoints.AsSpan();
    public float SweptRadiusMeters => _clearance;
}
