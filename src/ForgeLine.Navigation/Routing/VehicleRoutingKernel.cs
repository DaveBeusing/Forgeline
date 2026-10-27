using System.Numerics;
using System.Text.Json;

namespace ForgeLine.Navigation;

/// <summary>Detached, bounded continuation. Snapshot identity is checked before restoring.</summary>
public sealed class VehicleRoutingCheckpoint
{
    internal VehicleRoutingCheckpoint(NavigationSearchSpecification request, VehicleSearchState state)
    { Request = request; State = state; }
    public NavigationSearchSpecification Request { get; }
    internal VehicleSearchState State { get; }
    public string Json => JsonSerializer.Serialize(State, VehicleRoutingKernel.JsonOptions);
}

internal enum VehicleSearchStage : byte
{
    Start, StartCandidate, StartConnection, Candidate, EndConnection, BeginHigh, High, HighEdges,
    HighBacktrack, Corridor, BeginLocal, Local, Neighbors, Clearance,
    Backtrack, Publish, Copy, Complete
}
internal readonly record struct VehicleSearchNode(float Cost, int Previous);
internal readonly record struct VehicleFrontierEntry(int Index, float Cost, float Remaining);
internal readonly record struct VehicleClearance(int X, int Z, int MaximumX, int MaximumZ);

internal sealed class VehicleSearchState
{
    public VehicleSearchState() { }
    public VehicleSearchStage Stage { get; set; }
    public NavigationSearchStatus Status { get; set; } = NavigationSearchStatus.Pending;
    public NavigationFailureReason Failure { get; set; }
    public int Work { get; set; }
    public int AllocatedEntries { get; set; }
    public int HighExpanded { get; set; }
    public int LocalExpanded { get; set; }
    public int StartIndex { get; set; }
    public int GoalIndex { get; set; }
    public int Current { get; set; }
    public int Neighbor { get; set; }
    public int Next { get; set; }
    public float NextCost { get; set; }
    public int Expansion { get; set; }
    public int Cursor { get; set; }
    public int CopyRemaining { get; set; }
    public Vector3 Destination { get; set; }
    public VehicleClearance Clearance { get; set; }
    public Vector2 SweepMinimum { get; set; }
    public Vector2 SweepMaximum { get; set; }
    public int Obstacle { get; set; }
    public bool CellsChecked { get; set; }
    public bool ExactEnd { get; set; }
    public bool HasConnectedStart { get; set; }
    public Vector3 CandidatePosition { get; set; }
    public EndpointCandidateCursorState StartEndpoint { get; set; }
    public EndpointCandidateCursorState Endpoint { get; set; }
    public SortedDictionary<int, VehicleSearchNode> Nodes { get; set; } = new();
    public VehicleFrontierEntry[] Frontier { get; set; } = [];
    public List<int> HighRoute { get; set; } = new();
    public SortedSet<int> Corridor { get; set; } = new();
    public List<int> Reversed { get; set; } = new();
    public List<Vector3> Points { get; set; } = new();
    public List<NavigationRegionRevision> Regions { get; set; } = new();
    public float Length { get; set; }
    public Vector3 PreviousPoint { get; set; }
}

/// <summary>
/// Resumable hierarchy, refinement and bounded endpoint search over a pinned grid.
/// Clearance uses a conservative rotation-independent vehicle envelope. Dynamic
/// collisions, steering and exact service motion remain with the movement owner.
/// </summary>
public sealed class VehicleRoutingKernel : INavigationSearchKernel<VehicleRoutingCheckpoint>
{
    public const int AlgorithmRevision = 1;
    internal static JsonSerializerOptions JsonOptions { get; } = new() { IncludeFields = true };
    public NavigationModuleAvailability Availability => NavigationModuleAvailability.Available;

    public INavigationSearchSession<VehicleRoutingCheckpoint> CreateSession(
        NavigationWorldSnapshot snapshot, NavigationSearchSpecification request) => new Session(snapshot, request);

    public INavigationSearchSession<VehicleRoutingCheckpoint> RestoreSession(
        NavigationWorldSnapshot snapshot, VehicleRoutingCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new Session(snapshot, checkpoint.Request, checkpoint.State);
    }

    private sealed class Session : INavigationSearchSession<VehicleRoutingCheckpoint>
    {
        private static readonly (int X, int Z)[] s_neighbors =
            [(0,-1),(1,0),(0,1),(-1,0),(1,-1),(1,1),(-1,1),(-1,-1)];
        private readonly NavigationWorldSnapshot _snapshot;
        private readonly NavigationGrid _grid;
        private readonly NavigationSectorGraph _graph;
        private readonly EndpointCandidateCursor _endpoints;
        private readonly EndpointCandidateCursor _starts;
        private readonly EndpointCandidateCursorState _initialEndState;
        private PriorityQueue<VehicleFrontierEntry, (float Score, float Remaining, int Index)> _frontier = new();
        private readonly Dictionary<NavigationCellCoordinate, float> _penalties;
        private readonly int _entryLimit;
        private readonly Dictionary<int, float> _sectorPenalties = new();
        private VehicleSearchState _state;
        private NavigationRouteProposal? _route;
        private bool _disposed;

        internal Session(NavigationWorldSnapshot snapshot, NavigationSearchSpecification request, VehicleSearchState? state = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();
            if (snapshot.Revision != request.Revision || snapshot.KnowledgeScope != request.KnowledgeScope ||
                snapshot.Capabilities != request.Capabilities)
                throw new ArgumentException("Search and pinned snapshot must match.", nameof(snapshot));
            if (request.MaximumWorkspaceBytes < 8192 + snapshot.Penalties.Count * 256L)
                throw new ArgumentException("The request cannot reserve the fixed routing workspace.", nameof(request));
            _snapshot = snapshot; _grid = snapshot.Grid; _graph = snapshot.SectorGraph; Request = request;
            _entryLimit = (int)Math.Min(request.MaximumWork,
                Math.Max(0, (request.MaximumWorkspaceBytes - 8192 - snapshot.Penalties.Count * 256L) / 1024));
            _penalties = snapshot.Penalties.ToDictionary(p => p.Cell, p => p.AdditionalCost);
            foreach (var penalty in snapshot.Penalties)
            {
                int sector = SectorIndex(_graph.GetSector(penalty.Cell));
                _sectorPenalties[sector] = MathF.Max(_sectorPenalties.GetValueOrDefault(sector), penalty.AdditionalCost);
            }
            // Invalid endpoints are reported by Advance rather than silently projected.
            Vector3 cursorTarget = _grid.TryWorldToCell(request.OriginalDestination, out _)
                ? request.OriginalDestination : _grid.GetCellCenter(new(0, 0));
            _endpoints = new(_grid, request.Revision.Geometry, request.Capabilities, cursorTarget,
                request.Goal.MaximumProjectionDistance, request.MaximumEndpointCandidates);
            _initialEndState = _endpoints.CaptureState();
            _starts = new(_grid, request.Revision.Geometry, request.Capabilities,
                _grid.TryWorldToCell(request.Start.Position, out _) ? request.Start.Position : cursorTarget,
                _grid.Settings.CellSizeMeters * 3, request.MaximumEndpointCandidates);
            _state = state is null ? new() : Clone(state);
            if (state is not null)
            {
                if (_state.Work > request.MaximumWork || _state.AllocatedEntries > _entryLimit)
                    throw new ArgumentException("Checkpoint exceeds the original request limits.", nameof(state));
                _endpoints.RestoreState(_state.Endpoint);
                _starts.RestoreState(_state.StartEndpoint);
                foreach (var entry in _state.Frontier) Enqueue(entry);
                _state.Frontier = [];
                if (_state.Status == NavigationSearchStatus.Found) PublishRoute();
            }
        }

        public NavigationSearchSpecification Request { get; }

        public NavigationSearchAdvance Advance(int workAllowance)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
            ObjectDisposedException.ThrowIf(_disposed, this);
            int used = 0;
            while (_state.Status == NavigationSearchStatus.Pending && used < workAllowance)
            {
                if (_state.Work == Request.MaximumWork || _state.AllocatedEntries + 8 >= _entryLimit)
                { Finish(NavigationSearchStatus.BudgetExhausted, NavigationFailureReason.SearchBudgetExhausted); break; }
                used++; _state.Work++;
                Step();
            }
            return new(_state.Status, used, WorkspaceBytes, _route, _state.Failure);
        }

        private long WorkspaceBytes => Math.Min(Request.MaximumWorkspaceBytes,
            8192L + _snapshot.Penalties.Count * 256L + _state.AllocatedEntries * 1024L);

        private void Step()
        {
            switch (_state.Stage)
            {
                case VehicleSearchStage.Start:
                    if (!_grid.TryWorldToCell(Request.Start.Position, out var start))
                    { Finish(NavigationSearchStatus.InvalidInput, NavigationFailureReason.StartOutsideWorld); break; }
                    if (!_grid.TryWorldToCell(Request.OriginalDestination, out _))
                    { Finish(NavigationSearchStatus.InvalidInput, NavigationFailureReason.DestinationOutsideWorld); break; }
                    // This kernel handles coarse transit only; heading-constrained service poses stay local.
                    if (Request.Goal.RequiredHeadingRadians.HasValue)
                    { Finish(NavigationSearchStatus.InvalidInput, NavigationFailureReason.DestinationBlocked); break; }
                    _state.Stage = VehicleSearchStage.StartCandidate;
                    break;
                case VehicleSearchStage.StartCandidate:
                    var startStep = _starts.MoveNext(1, out var startCandidate, out _);
                    if (startStep == EndpointCandidateStep.Candidate)
                    {
                        _state.StartIndex = _grid.GetIndex(startCandidate.Cell);
                        BeginClearance(Request.Start.Position, _grid.GetCellCenter(startCandidate.Cell), VehicleSearchStage.StartConnection);
                    }
                    else if (startStep == EndpointCandidateStep.CandidateLimitReached)
                        Finish(NavigationSearchStatus.BudgetExhausted, NavigationFailureReason.SearchBudgetExhausted);
                    else if (startStep == EndpointCandidateStep.EnumerationComplete)
                        Finish(NavigationSearchStatus.Unreachable, _state.HasConnectedStart
                            ? NavigationFailureReason.NoLocalRoute : NavigationFailureReason.StartBlocked);
                    break;
                case VehicleSearchStage.StartConnection:
                    if (!InspectClearance(out bool startDone))
                    { _state.Stage = VehicleSearchStage.StartCandidate; break; }
                    if (startDone)
                    {
                        _state.HasConnectedStart = true;
                        _endpoints.RestoreState(_initialEndState);
                        _state.Stage = VehicleSearchStage.Candidate;
                    }
                    break;
                case VehicleSearchStage.Candidate:
                    var step = _endpoints.MoveNext(1, out var candidate, out _);
                    if (step == EndpointCandidateStep.Candidate)
                    {
                        _state.CandidatePosition = candidate.Position; _state.GoalIndex = _grid.GetIndex(candidate.Cell);
                        _state.Destination = Request.OriginalDestination; _state.ExactEnd = true;
                        BeginClearance(_grid.GetCellCenter(candidate.Cell), Request.OriginalDestination, VehicleSearchStage.EndConnection);
                    }
                    else if (step == EndpointCandidateStep.CandidateLimitReached)
                        Finish(NavigationSearchStatus.BudgetExhausted, NavigationFailureReason.SearchBudgetExhausted);
                    else if (step == EndpointCandidateStep.EnumerationComplete)
                        _state.Stage = VehicleSearchStage.StartCandidate;
                    break;
                case VehicleSearchStage.EndConnection:
                    if (!InspectClearance(out bool endDone))
                    {
                        float tolerance = Request.TargetSemantics == NavigationTargetSemantics.DockAccess
                            ? Request.Goal.MaximumProjectionDistance : Request.Goal.PositionTolerance;
                        if (_state.ExactEnd && HorizontalDistance(_state.CandidatePosition, Request.OriginalDestination) <= tolerance)
                        {
                            _state.ExactEnd = false; _state.Destination = _state.CandidatePosition;
                            BeginClearance(_grid.GetCellCenter(_grid.GetCoordinate(_state.GoalIndex)),
                                _state.Destination, VehicleSearchStage.EndConnection);
                        }
                        else _state.Stage = VehicleSearchStage.Candidate;
                        break;
                    }
                    if (endDone) _state.Stage = VehicleSearchStage.BeginHigh;
                    break;
                case VehicleSearchStage.BeginHigh:
                    ResetFrontier();
                    _state.HighRoute = new(); _state.Corridor = new(); _state.Expansion = 0;
                    int highStart = SectorIndex(_graph.GetSector(_grid.GetCoordinate(_state.StartIndex)));
                    int highGoal = SectorIndex(_graph.GetSector(_grid.GetCoordinate(_state.GoalIndex)));
                    AddNode(highStart, 0, -1, SectorDistance(highStart, highGoal));
                    _state.Stage = VehicleSearchStage.High;
                    break;
                case VehicleSearchStage.High:
                    if (!TakeNode()) { _state.Stage = VehicleSearchStage.Candidate; break; }
                    if (_state.Current < 0) break;
                    _state.HighExpanded++;
                    if (_state.Current == SectorIndex(_graph.GetSector(_grid.GetCoordinate(_state.GoalIndex))))
                        _state.Stage = VehicleSearchStage.HighBacktrack;
                    else { _state.Neighbor = 0; _state.Stage = VehicleSearchStage.HighEdges; }
                    break;
                case VehicleSearchStage.HighEdges:
                    var edges = _graph.GetEdges(Sector(_state.Current));
                    if (_state.Neighbor == edges.Length) { _state.Stage = VehicleSearchStage.High; break; }
                    var edge = edges[_state.Neighbor++];
                    int next = SectorIndex(edge.To);
                    float cost = _state.Nodes[_state.Current].Cost + edge.Cost + _sectorPenalties.GetValueOrDefault(next);
                    Relax(next, cost, _state.Current, SectorDistance(next,
                        SectorIndex(_graph.GetSector(_grid.GetCoordinate(_state.GoalIndex)))));
                    break;
                case VehicleSearchStage.HighBacktrack:
                    _state.HighRoute.Add(_state.Current); _state.AllocatedEntries++;
                    int previous = _state.Nodes[_state.Current].Previous;
                    if (previous < 0) { _state.Cursor = 0; _state.Neighbor = 0; _state.Stage = VehicleSearchStage.Corridor; }
                    else _state.Current = previous;
                    break;
                case VehicleSearchStage.Corridor:
                    if (_state.Cursor == _state.HighRoute.Count) { _state.Stage = VehicleSearchStage.BeginLocal; break; }
                    var sector = Sector(_state.HighRoute[_state.Cursor]);
                    int span = _state.Expansion * 2 + 1;
                    var expanded = new NavigationSectorCoordinate(sector.X + _state.Neighbor % span - _state.Expansion,
                        sector.Z + _state.Neighbor / span - _state.Expansion);
                    if (_graph.Contains(expanded)) { _state.Corridor.Add(SectorIndex(expanded)); _state.AllocatedEntries++; }
                    if (++_state.Neighbor == span * span) { _state.Cursor++; _state.Neighbor = 0; }
                    break;
                case VehicleSearchStage.BeginLocal:
                    ResetFrontier();
                    AddNode(_state.StartIndex, 0, -1, CellDistance(_state.StartIndex, _state.GoalIndex));
                    _state.Stage = VehicleSearchStage.Local;
                    break;
                case VehicleSearchStage.Local:
                    if (!TakeNode())
                    {
                        if (++_state.Expansion == 1)
                        { _state.Cursor = 0; _state.Neighbor = 0; _state.Stage = VehicleSearchStage.Corridor; }
                        else if (_state.Expansion == 2) _state.Stage = VehicleSearchStage.BeginLocal;
                        else _state.Stage = VehicleSearchStage.Candidate;
                        break;
                    }
                    if (_state.Current < 0) break;
                    _state.LocalExpanded++;
                    if (_state.Current == _state.GoalIndex) _state.Stage = VehicleSearchStage.Backtrack;
                    else { _state.Neighbor = 0; _state.Stage = VehicleSearchStage.Neighbors; }
                    break;
                case VehicleSearchStage.Neighbors:
                    if (_state.Neighbor == s_neighbors.Length) { _state.Stage = VehicleSearchStage.Local; break; }
                    var delta = s_neighbors[_state.Neighbor++];
                    var from = _grid.GetCoordinate(_state.Current);
                    var to = new NavigationCellCoordinate(from.X + delta.X, from.Z + delta.Z);
                    if (!_grid.IsTraversable(to, Request.Capabilities) ||
                        (_state.Expansion < 2 && !_state.Corridor.Contains(SectorIndex(_graph.GetSector(to))))) break;
                    _state.Next = _grid.GetIndex(to);
                    _state.NextCost = _state.Nodes[_state.Current].Cost +
                        (delta.X != 0 && delta.Z != 0 ? 1.41421356f : 1) *
                        0.5f * (_grid.GetTraversalCost(from, Request.Capabilities) + _grid.GetTraversalCost(to, Request.Capabilities)) +
                        (_penalties.TryGetValue(to, out float penalty) ? penalty : 0);
                    if (_state.Nodes.TryGetValue(_state.Next, out var known) && known.Cost <= _state.NextCost) break;
                    BeginClearance(_grid.GetCellCenter(from), _grid.GetCellCenter(to), VehicleSearchStage.Clearance);
                    break;
                case VehicleSearchStage.Clearance:
                    if (!InspectClearance(out bool clearDone)) { _state.Stage = VehicleSearchStage.Neighbors; break; }
                    if (clearDone)
                    {
                        Relax(_state.Next, _state.NextCost, _state.Current, CellDistance(_state.Next, _state.GoalIndex));
                        _state.Stage = VehicleSearchStage.Neighbors;
                    }
                    break;
                case VehicleSearchStage.Backtrack:
                    _state.Reversed.Add(_state.Current); _state.AllocatedEntries++;
                    int parent = _state.Nodes[_state.Current].Previous;
                    if (parent < 0)
                    {
                        _state.Cursor = _state.Reversed.Count - 1; _state.PreviousPoint = Request.Start.Position;
                        _state.Stage = VehicleSearchStage.Publish;
                    }
                    else _state.Current = parent;
                    break;
                case VehicleSearchStage.Publish:
                    if (_state.Cursor >= 0)
                    {
                        var cell = _grid.GetCoordinate(_state.Reversed[_state.Cursor--]);
                        AppendPoint(_grid.GetCellCenter(cell));
                        var region = new NavigationRegionRevision(_graph.GetSector(cell), Request.Revision.Geometry);
                        if (_state.Regions.Count == 0 || _state.Regions[^1] != region)
                        { _state.Regions.Add(region); _state.AllocatedEntries++; }
                    }
                    else
                    {
                        AppendPoint(_state.Destination);
                        _state.CopyRemaining = checked(_state.Points.Count * 3 + _state.Regions.Count);
                        _state.Stage = VehicleSearchStage.Copy;
                    }
                    break;
                case VehicleSearchStage.Copy:
                    if (_state.CopyRemaining-- > 0) break;
                    PublishRoute();
                    Finish(NavigationSearchStatus.Found, NavigationFailureReason.None);
                    break;
            }
        }

        private void ResetFrontier()
        {
            // All allocations across retries are charged monotonically, including
            // discarded dictionary/heap storage and publication/checkpoint copies.
            _state.Nodes = new(); _frontier = new();
        }

        private bool TakeNode()
        {
            if (!_frontier.TryDequeue(out var entry, out _)) return false;
            if (!_state.Nodes.TryGetValue(entry.Index, out var node) || node.Cost != entry.Cost)
                { _state.Current = -1; return true; } // stale entries consume work without expanding neighbors
            _state.Current = entry.Index;
            return true;
        }

        private void AddNode(int index, float cost, int previous, float remaining)
        {
            _state.Nodes[index] = new(cost, previous); _state.AllocatedEntries++;
            Enqueue(new(index, cost, remaining)); _state.AllocatedEntries++;
        }
        private void Relax(int index, float cost, int previous, float remaining)
        {
            if (!float.IsFinite(cost) || _state.Nodes.TryGetValue(index, out var known) && known.Cost <= cost) return;
            AddNode(index, cost, previous, remaining);
        }
        private void Enqueue(VehicleFrontierEntry entry) =>
            _frontier.Enqueue(entry, (entry.Cost + entry.Remaining, entry.Remaining, entry.Index));

        private void BeginClearance(Vector3 from, Vector3 to, VehicleSearchStage stage)
        {
            float radius = Request.Vehicle.HalfExtents.Length() + Request.Vehicle.ClearanceMeters;
            float size = _grid.Settings.CellSizeMeters;
            int minX = (int)MathF.Floor((MathF.Min(from.X, to.X) - radius - _grid.Origin.X) / size);
            int minZ = (int)MathF.Floor((MathF.Min(from.Z, to.Z) - radius - _grid.Origin.Y) / size);
            int maxX = (int)MathF.Floor((MathF.Max(from.X, to.X) + radius - _grid.Origin.X) / size);
            int maxZ = (int)MathF.Floor((MathF.Max(from.Z, to.Z) + radius - _grid.Origin.Y) / size);
            _state.Clearance = new(minX, minZ, maxX, maxZ); _state.Stage = stage;
            _state.SweepMinimum = new(MathF.Min(from.X, to.X) - radius, MathF.Min(from.Z, to.Z) - radius);
            _state.SweepMaximum = new(MathF.Max(from.X, to.X) + radius, MathF.Max(from.Z, to.Z) + radius);
            _state.CellsChecked = false; _state.Obstacle = 0;
        }
        private bool InspectClearance(out bool complete)
        {
            complete = false;
            if (!_state.CellsChecked)
            {
                var scan = _state.Clearance;
                var cell = new NavigationCellCoordinate(scan.X, scan.Z);
                if (!_grid.Contains(cell)) return false;
                var sample = _grid.GetCell(cell);
                if (!sample.HasTerrain || sample.SlopeDegrees > Request.Capabilities.MaximumSlopeDegrees ||
                    (!_grid.HasExactObstacleGeometry && sample.StaticBlocked)) return false;
                if (scan.X == scan.MaximumX && scan.Z == scan.MaximumZ) _state.CellsChecked = true;
                else _state.Clearance = scan.X == scan.MaximumX
                    ? scan with { X = MinimumScanX(), Z = scan.Z + 1 } : scan with { X = scan.X + 1 };
                return true;
            }
            if (_state.Obstacle < _grid.StaticObstacles.Count)
            {
                var obstacle = _grid.StaticObstacles[_state.Obstacle++];
                float padding = _grid.Settings.StaticObstacleClearanceMeters;
                if (obstacle.Maximum.X + padding >= _state.SweepMinimum.X && obstacle.Minimum.X - padding <= _state.SweepMaximum.X &&
                    obstacle.Maximum.Z + padding >= _state.SweepMinimum.Y && obstacle.Minimum.Z - padding <= _state.SweepMaximum.Y)
                    return false;
                return true;
            }
            complete = true;
            return true;
        }
        private int MinimumScanX()
        {
            Vector3 from, to;
            if (_state.Stage == VehicleSearchStage.StartConnection)
            { from = Request.Start.Position; to = _grid.GetCellCenter(_grid.GetCoordinate(_state.StartIndex)); }
            else if (_state.Stage == VehicleSearchStage.EndConnection)
            { from = _grid.GetCellCenter(_grid.GetCoordinate(_state.GoalIndex)); to = _state.Destination; }
            else
            { from = _grid.GetCellCenter(_grid.GetCoordinate(_state.Current)); to = _grid.GetCellCenter(_grid.GetCoordinate(_state.Next)); }
            return (int)MathF.Floor((MathF.Min(from.X, to.X) - Request.Vehicle.HalfExtents.Length() -
                Request.Vehicle.ClearanceMeters - _grid.Origin.X) / _grid.Settings.CellSizeMeters);
        }
        private void AppendPoint(Vector3 point)
        {
            if (_state.Points.Count > 0 && _state.Points[^1] == point) return;
            _state.Length += HorizontalDistance(_state.PreviousPoint, point); _state.PreviousPoint = point;
            _state.Points.Add(point); _state.AllocatedEntries++;
        }
        private void PublishRoute() => _route = new(Request, _state.Destination, _state.Points.ToArray(),
            _state.Regions.ToArray(), new(_state.HighExpanded, _state.LocalExpanded, _state.Reversed.Count,
                _state.Length, false), Request.MaximumWork);
        private void Finish(NavigationSearchStatus status, NavigationFailureReason failure)
        { _state.Status = status; _state.Failure = failure; }
        private int SectorIndex(NavigationSectorCoordinate sector) => sector.Z * _graph.Width + sector.X;
        private NavigationSectorCoordinate Sector(int index) => new(index % _graph.Width, index / _graph.Width);
        private float SectorDistance(int from, int to)
        { var a = Sector(from); var b = Sector(to); return Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z); }
        private float CellDistance(int from, int to)
        {
            var a = _grid.GetCoordinate(from); var b = _grid.GetCoordinate(to);
            int x = Math.Abs(a.X - b.X), z = Math.Abs(a.Z - b.Z);
            return Math.Min(x, z) * 1.41421356f + Math.Abs(x - z);
        }
        private static float HorizontalDistance(Vector3 from, Vector3 to) => new Vector2(to.X - from.X, to.Z - from.Z).Length();

        public VehicleRoutingCheckpoint CaptureState()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var copy = Clone(_state);
            copy.Endpoint = _endpoints.CaptureState();
            copy.StartEndpoint = _starts.CaptureState();
            copy.Frontier = _frontier.UnorderedItems.Select(item => item.Element)
                .OrderBy(e => e.Cost + e.Remaining).ThenBy(e => e.Remaining).ThenBy(e => e.Index).ToArray();
            return new(Request, copy);
        }
        private static VehicleSearchState Clone(VehicleSearchState source) =>
            JsonSerializer.Deserialize<VehicleSearchState>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions) ??
                throw new InvalidOperationException("Invalid routing checkpoint.");
        public void Cancel() => Finish(NavigationSearchStatus.Canceled, NavigationFailureReason.Canceled);
        public void Dispose() { _disposed = true; _frontier.Clear(); }
    }
}
