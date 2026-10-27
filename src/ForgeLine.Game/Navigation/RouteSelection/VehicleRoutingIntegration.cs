using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class HierarchicalNavigationSystem
{
    private const int VehicleWorkPerTick = 16_384, VehicleWorkPerSlice = 2_048;
    private const int VehicleWorkPerSearch = 1_048_576, MaximumVehicleSearches = 8;
    private const long VehicleWorkspaceBytes = 8 * 1024 * 1024;
    private readonly VehicleRoutingKernel _vehicleKernel = new();
    private readonly Dictionary<EntityId, VehicleSearchWork> _vehicleSearches = new();
    private readonly Dictionary<ulong, NavigationWorldSnapshot> _routingSnapshots = new();
    private readonly HashSet<ulong> _incompleteCosts = new();
    private readonly Dictionary<ulong, CongestionPenaltyMemory> _congestion = new();
    private readonly Dictionary<ulong, List<CongestionObservation>> _observations = new();
    private TerrainWorld? _routingTerrain;
    private float _routingClearance;
    private SimulationSessionId _routingSession;
    private EntityRegistry? _routingEntities;
    private NavigationWorld? _costWorld;
    private ulong _nextCommandGeneration = 1;
    private ulong _lastServedRequest;
    private ulong _costTick;

    public bool VehicleRoutingConnected => _routingTerrain is not null;

    internal void ConnectVehicleRouting(TerrainWorld terrain, SimulationCoordinator simulation, float clearanceMeters)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (_routingTerrain is not null) throw new InvalidOperationException("Vehicle routing is already connected.");
        _routingClearance = clearanceMeters;
        _routingTerrain = terrain; _routingSession = simulation.SessionId; _routingEntities = simulation.Entities;
        // Graph preparation belongs to world publication, not a search work slice.
        World.GetSectorGraph(NavigationCapabilities.For(NavigationMovementClass.Wheeled));
    }

    private bool UsesVehicleKernel(SimulationContext context, EntityId entity) =>
        VehicleRoutingConnected && context.Entities.TryGetComponent(entity, out UnitIdentity unit) &&
        (unit.UnitId == UnitIds.CargoTruck || unit.UnitId == UnitIds.SupplyTruck) &&
        context.Entities.TryGetComponent(entity, out NavigationAgent agent) &&
        agent.MovementClass == NavigationMovementClass.Wheeled;

    private void UpdateRoutingCosts(SimulationContext context)
    {
        if (!VehicleRoutingConnected) return;
        if (!ReferenceEquals(context.Entities, _routingEntities))
            throw new InvalidOperationException("Routing is bound to a different simulation session.");
        if (!ReferenceEquals(_costWorld, World))
        {
            _congestion.Clear(); _observations.Clear(); _costWorld = World;
            World.GetSectorGraph(NavigationCapabilities.For(NavigationMovementClass.Wheeled));
        }
        _costTick = context.Tick.Value;
        _routingSnapshots.Clear(); _incompleteCosts.Clear();
        foreach (var observations in _observations.Values) observations.Clear();
        // Only owned logistics observations enter a scope. Physical collision
        // captures and hidden enemy states never become route costs.
        int inspected = 0;
        bool complete = true;
        foreach (var entity in context.Entities.Query<ControllableEntity, GroundMovementState>(QueryIterationOrder.StableByEntityIndex))
        {
            if (++inspected > 4096) { complete = false; break; }
            if (!UsesVehicleKernel(context, entity) ||
                !context.Entities.TryGetComponent(entity, out WorldTransform transform)) continue;
            var owner = context.Entities.GetComponent<ControllableEntity>(entity).Owner;
            if (!owner.IsSpecified) continue;
            ulong scope = owner.Value;
            if (!_congestion.TryGetValue(scope, out var memory))
            {
                if (_congestion.Count == 16) continue;
                memory = new(scope, new(128, 128, 20, 80, 40, 200, 12, 0.1f));
                _congestion.Add(scope, memory); _observations.Add(scope, new());
            }
            var batch = _observations[scope];
            if (batch.Count == 128) { _incompleteCosts.Add(scope); continue; }
            if (!World.Grid.TryWorldToCell(transform.Position, out var cell)) continue;
            var state = context.Entities.GetComponent<GroundMovementState>(entity);
            bool delayed = state.StalledTicks > 0 && state.Status is GroundMovementStatus.Moving or GroundMovementStatus.Stuck;
            var kind = !delayed ? CongestionObservationKind.Clear :
                context.Entities.HasComponent<VehicleQueueEntry>(entity) ? CongestionObservationKind.DockQueueWait : CongestionObservationKind.TrafficDelay;
            int existing = batch.FindIndex(o => o.Cell == cell);
            if (existing < 0) batch.Add(new(cell, kind));
            else if ((byte)kind > (byte)batch[existing].Kind) batch[existing] = new(cell, kind);
        }
        foreach (var pair in _congestion)
        {
            var batch = _observations[pair.Key].OrderBy(o => o.Cell.Z).ThenBy(o => o.Cell.X).ToArray();
            if (pair.Value.Record(pair.Key, _costTick, batch) != 0 || !complete)
                _incompleteCosts.Add(pair.Key);
        }
    }

    private NavigationWorldSnapshot CaptureRoutingWorld(in NavigationPendingPath pending)
    {
        ulong scope = pending.OriginalOrder.Issuer.Value;
        if (_routingSnapshots.TryGetValue(scope, out var cached) && cached.Capabilities == pending.Request.Capabilities)
            return cached;
        bool costsAvailable = _congestion.TryGetValue(scope, out var memory) && !_incompleteCosts.Contains(scope);
        var penalties = costsAvailable
            ? memory!.CaptureState().Entries.Select(entry => new NavigationCellPenalty(entry.Cell,
                memory!.GetAdditionalCost(scope, entry.Cell, _costTick))).Where(p => p.AdditionalCost > 0).ToArray()
            : Array.Empty<NavigationCellPenalty>();
        var snapshot = new NavigationWorldSnapshot(World, World.GetSectorGraph(pending.Request.Capabilities), pending.Request.Capabilities,
            _routingTerrain, costsAvailable ? Math.Max(1UL, _costTick) : 0, scope,
            costsAvailable ? NavigationModuleAvailability.Available : NavigationModuleAvailability.Unavailable, [], penalties);
        if (_routingSnapshots.Count < 16) _routingSnapshots[scope] = snapshot;
        return snapshot;
    }

    private void TryStartVehicleSearch(SimulationContext context, EntityId entity, in NavigationPendingPath pending)
    {
        if (_vehicleSearches.ContainsKey(entity) || _vehicleSearches.Count == MaximumVehicleSearches) return;
        if (!NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, _routingClearance, out var vehicle) ||
            vehicle.Owner != pending.OriginalOrder.Issuer ||
            vehicle.Pose.Position != pending.Request.Start || vehicle.Pose.Rotation != pending.StartRotation)
        { CompleteVehicleFailure(pending, NavigationFailureReason.Canceled); return; }
        var snapshot = CaptureRoutingWorld(pending);
        bool service = vehicle.QueueEntry is { Position: 1, Generation: > 0 } queue &&
            queue.Target == pending.OriginalOrder.WorldTarget &&
            vehicle.DockVisit is { Generation: > 0, LaneEntered: false } visit &&
            vehicle.BuildingSlot is { } slot && slot.Generation == visit.Generation && slot.Truck == entity;
        float stopRadius = context.Entities.GetComponent<GroundMovement>(entity).StopRadius;
        float projection = service ? vehicle.PrecisionApproachRadius : MathF.Max(stopRadius, snapshot.Grid.Settings.CellSizeMeters * 3);
        var limits = new NavigationIntegrationLimits(VehicleWorkPerTick, VehicleWorkPerSearch, MaximumVehicleSearches,
            VehicleWorkspaceBytes, 1, 16, 60, 3, 128, 128, 128);
        var goal = new NavigationGoalConstraints(projection, stopRadius, null, 0);
        var bound = NavigationReadAdapter.AdaptPendingRequest(pending, pending.StartRotation, pending.RootGeneration,
            vehicle.DockVisit?.Generation ?? 0, vehicle.QueueEntry?.Generation ?? 0,
            service ? NavigationTargetSemantics.DockAccess : NavigationTargetSemantics.Transit, goal, vehicle.Profile, snapshot, limits);
        context.Entities.SetComponent(entity, pending with { BoundSearch = bound });
        _vehicleSearches.Add(entity, new(bound, snapshot, _routingSession,
            _vehicleKernel.CreateSession(snapshot, bound.Search), Stopwatch.GetTimestamp()));
    }

    private void AdvanceVehicleSearches(SimulationContext context)
    {
        UpdateRoutingCosts(context);
        int budget = VehicleWorkPerTick;
        var entries = _vehicleSearches.OrderBy(p => p.Value.Bound.Binding.RequestId <= _lastServedRequest)
            .ThenBy(p => p.Value.Bound.Binding.RequestId).ToArray();
        foreach (var pair in entries)
        {
            var work = pair.Value;
            if (!MatchesVehicleSearch(context, pair.Key, work))
            {
                work.Session.Cancel(); work.Session.Dispose(); _vehicleSearches.Remove(pair.Key);
                CompleteVehicleFailure(work, NavigationFailureReason.StaleNavigationVersion);
                continue;
            }
            if (budget == 0) break;
            int allowance = Math.Min(VehicleWorkPerSlice, budget);
            var advance = work.Session.Advance(allowance);
            if (advance.WorkConsumed < 0 || advance.WorkConsumed > allowance)
                throw new InvalidOperationException("Vehicle search exceeded its work grant.");
            budget -= advance.WorkConsumed; _lastServedRequest = work.Bound.Binding.RequestId;
            if (advance.Status == NavigationSearchStatus.Pending) continue;
            NavigationPath? path = advance.Status == NavigationSearchStatus.Found && advance.Route is { } proposal
                ? NavigationPath.FromProposal(proposal, work.Snapshot.Grid) : null;
            var request = context.Entities.GetComponent<NavigationPendingPath>(pair.Key).Request;
            _completed.Enqueue(new(request, new(request.NavigationVersion, advance.FailureReason, path), Stopwatch.GetElapsedTime(work.StartedAt)));
            work.Session.Dispose(); _vehicleSearches.Remove(pair.Key);
        }
    }

    private bool MatchesVehicleSearch(SimulationContext context, EntityId entity, VehicleSearchWork work)
    {
        var binding = work.Bound.Binding;
        return work.SessionId == _routingSession && context.Entities.IsAlive(entity) &&
            work.Snapshot.Revision.Geometry == World.Version && ReferenceEquals(work.Snapshot.Grid, World.Grid) &&
            context.Entities.TryGetComponent(entity, out NavigationPendingPath pending) &&
            pending.BoundSearch == work.Bound && pending.RootGeneration == binding.CommandGeneration &&
            !context.Entities.HasComponent<MovementOrder>(entity) &&
            NavigationReadAdapter.TryCaptureVehicle(context.Entities, entity, _routingClearance, out var vehicle) &&
            vehicle.Profile == work.Bound.Search.Vehicle &&
            vehicle.Pose == work.Bound.Search.Start && NavigationPrecisionOwnership.Matches(vehicle, binding);
    }

    private void CompleteVehicleFailure(VehicleSearchWork work, NavigationFailureReason failure)
    {
        var binding = work.Bound.Binding;
        var request = new NavigationPathRequest(binding.RequestId, binding.Requester, work.Bound.Search.Start.Position,
            binding.OriginalOrder.WorldTarget, work.Bound.Search.Capabilities, work.Bound.Search.Revision.Geometry);
        _completed.Enqueue(new(request, new(request.NavigationVersion, failure, null), TimeSpan.Zero));
    }
    private void CompleteVehicleFailure(in NavigationPendingPath pending, NavigationFailureReason failure) =>
        _completed.Enqueue(new(pending.Request, new(pending.Request.NavigationVersion, failure, null), TimeSpan.Zero));

    internal object CaptureRoutingState() => new
    {
        Algorithm = VehicleRoutingKernel.AlgorithmRevision, NextRequest = _nextRequestId,
        NextCommandGeneration = _nextCommandGeneration, LastServed = _lastServedRequest, CostTick = _costTick,
        IncompleteCostScopes = _incompleteCosts.Order().ToArray(),
        Congestion = _congestion.OrderBy(p => p.Key).Select(p => p.Value.CaptureState()).ToArray(),
        Searches = _vehicleSearches.OrderBy(p => p.Value.Bound.Binding.RequestId)
            .Select(p => new { p.Value.Bound, Penalties = p.Value.Snapshot.Penalties, State = p.Value.Session.CaptureState().Json }).ToArray()
    };

    private sealed record VehicleSearchWork(NavigationBoundSearch Bound, NavigationWorldSnapshot Snapshot,
        SimulationSessionId SessionId, INavigationSearchSession<VehicleRoutingCheckpoint> Session, long StartedAt);
}
