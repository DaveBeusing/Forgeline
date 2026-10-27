using System.Numerics;

namespace ForgeLine.Navigation;

public enum NavigationFailureReason : byte
{
    None = 0,
    StartOutsideWorld = 1,
    DestinationOutsideWorld = 2,
    StartBlocked = 3,
    DestinationBlocked = 4,
    NoHighLevelRoute = 5,
    NoLocalRoute = 6,
    StaleNavigationVersion = 7,
    Canceled = 8,
    SearchBudgetExhausted = 9
}

public readonly record struct NavigationPathDiagnostics(
    int ExpandedHighLevelNodes,
    int ExpandedLocalNodes,
    int RefinedCellCount,
    float RouteLengthMeters,
    bool HighLevelCacheHit);

public sealed class NavigationPath
{
    private readonly NavigationSectorCoordinate[] _sectors;
    private readonly NavigationPortal[] _portals;
    private readonly NavigationCellCoordinate[] _cells;
    private readonly Vector3[] _waypoints;

    internal NavigationPath(
        NavigationVersion version,
        NavigationMovementClass movementClass,
        Vector3 requestedDestination,
        NavigationSectorCoordinate[] sectors,
        NavigationPortal[] portals,
        NavigationCellCoordinate[] cells,
        Vector3[] waypoints,
        NavigationPathDiagnostics diagnostics)
    {
        Version = version;
        MovementClass = movementClass;
        RequestedDestination = requestedDestination;
        _sectors = sectors;
        _portals = portals;
        _cells = cells;
        _waypoints = waypoints;
        Diagnostics = diagnostics;
    }

    /// <summary>Present only for the connected bounded vehicle search.</summary>
    public NavigationRouteProposal? Proposal { get; private init; }

    public static NavigationPath FromProposal(NavigationRouteProposal proposal, NavigationGrid grid)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(grid);
        var cells = new List<NavigationCellCoordinate>(proposal.Waypoints.Count);
        foreach (var point in proposal.Waypoints)
        {
            if (!grid.TryWorldToCell(point, out var cell))
                throw new ArgumentException("A route point is outside its pinned grid.", nameof(proposal));
            if (cells.Count == 0 || cells[^1] != cell) cells.Add(cell);
        }
        return new(proposal.Request.Revision.Geometry, proposal.Request.Capabilities.MovementClass,
            proposal.Request.OriginalDestination, proposal.Regions.Select(r => r.Region).ToArray(), [],
            cells.ToArray(), proposal.Waypoints.ToArray(), proposal.Diagnostics) { Proposal = proposal };
    }

    public NavigationVersion Version { get; }

    public NavigationMovementClass MovementClass { get; }

    public Vector3 RequestedDestination { get; }

    public IReadOnlyList<NavigationSectorCoordinate> Sectors =>
        _sectors;

    public IReadOnlyList<NavigationPortal> Portals =>
        _portals;

    public IReadOnlyList<NavigationCellCoordinate> RefinedCells =>
        _cells;

    public IReadOnlyList<Vector3> Waypoints =>
        _waypoints;

    public NavigationPathDiagnostics Diagnostics { get; }
}

public readonly record struct NavigationSearchResult(
    NavigationVersion Version,
    NavigationFailureReason FailureReason,
    NavigationPath? Path)
{
    public bool Succeeded =>
        FailureReason == NavigationFailureReason.None &&
        Path is not null;
}
