using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Jobs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class HierarchicalNavigationSystem
{
    private const int VehicleWorkPerTick = 16_384, VehicleWorkPerSlice = 2_048;
    private const int VehicleWorkPerSearch = 1_048_576, MaximumVehicleSearches = 8;
    private const long VehicleWorkspaceBytes = 8 * 1024 * 1024;
    private VehicleFleetKernel? _fleetKernel;
    private NavigationSearchScheduler<VehicleFleetCheckpoint>? _fleetScheduler;
    private readonly Dictionary<ulong, NavigationWorldSnapshot> _publishedFleetSnapshots = new();
    private int _fleetSubmissionsThisTick;
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
    private ulong _costTick;

    public bool VehicleRoutingConnected => _routingTerrain is not null;
    public bool VehicleSchedulingConnected => _fleetScheduler?.Availability == NavigationModuleAvailability.Available;
    private const int MaximumFleetEntries = 128, MaximumSubmissionsPerTick = 16;
    private NavigationIntegrationLimits FleetLimits => new(VehicleWorkPerTick, VehicleWorkPerSearch,
        MaximumVehicleSearches, MaximumVehicleSearches * VehicleWorkspaceBytes + _fleetKernel!.RetainedReservationBytes,
        2 * 1024 * 1024, 16, 60, 3, 128, 128, 128);

    internal void ConnectVehicleRouting(TerrainWorld terrain, SimulationCoordinator simulation, float clearanceMeters,
        JobScheduler? jobs = null, string contentIdentity = "navigation")
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (_routingTerrain is not null) throw new InvalidOperationException("Vehicle routing is already connected.");
        _routingClearance = clearanceMeters;
        _routingTerrain = terrain; _routingSession = simulation.SessionId; _routingEntities = simulation.Entities;
        _fleetKernel = new(ContentGeneration(contentIdentity), MaximumVehicleSearches, 2 * 1024 * 1024);
        _fleetScheduler = new(_fleetKernel, FleetLimits,
            new(MaximumFleetEntries, MaximumSubmissionsPerTick, MaximumVehicleSearches,
                VehicleWorkPerTick, VehicleWorkPerSearch, VehicleWorkPerSlice, 60, 60),
            new(16, 40 * 1024 * 1024, 3 * 1024 * 1024, _fleetKernel.RetainedReservationBytes),
            new(contentIdentity, 1, VehicleRoutingKernel.AlgorithmRevision),
            new(1, VehicleFleetKernel.Encode, VehicleFleetKernel.Decode), ResolveFleetSnapshot,
            bound => bound.Search.TargetSemantics == NavigationTargetSemantics.DockAccess
                ? NavigationServiceClass.Recovery : NavigationServiceClass.PlayerOrder,
            jobs, preservePinnedCosts: true);
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
            _congestion.Clear(); _observations.Clear(); _publishedFleetSnapshots.Clear(); _costWorld = World;
            World.GetSectorGraph(NavigationCapabilities.For(NavigationMovementClass.Wheeled));
        }
        _costTick = context.Tick.Value;
        _routingSnapshots.Clear(); _incompleteCosts.Clear(); _fleetSubmissionsThisTick = 0;
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
            _routingTerrain, Math.Max(1UL, _costTick), scope,
            costsAvailable ? NavigationModuleAvailability.Available : NavigationModuleAvailability.Unavailable, [], penalties);
        if (_routingSnapshots.Count < 16) _routingSnapshots[scope] = snapshot;
        return snapshot;
    }

    private void TryStartVehicleSearch(SimulationContext context, EntityId entity, in NavigationPendingPath pending)
    {
        if (_fleetScheduler is null || _vehicleSearches.ContainsKey(entity) ||
            _vehicleSearches.Count == MaximumFleetEntries || _fleetSubmissionsThisTick == MaximumSubmissionsPerTick) return;
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
        var limits = FleetLimits with { MaximumSearchBytes = VehicleWorkspaceBytes };
        var goal = new NavigationGoalConstraints(projection, stopRadius, null, 0);
        var bound = NavigationReadAdapter.AdaptPendingRequest(pending, pending.StartRotation, pending.RootGeneration,
            vehicle.DockVisit?.Generation ?? 0, vehicle.QueueEntry?.Generation ?? 0,
            service ? NavigationTargetSemantics.DockAccess : NavigationTargetSemantics.Transit, goal, vehicle.Profile, snapshot, limits);
        if (!_publishedFleetSnapshots.ContainsKey(snapshot.KnowledgeScope) && _publishedFleetSnapshots.Count == 16) return;
        if (!_publishedFleetSnapshots.TryGetValue(snapshot.KnowledgeScope, out var published) ||
            !ReferenceEquals(published, snapshot))
        {
            _fleetScheduler.UpdateSnapshot(snapshot);
            _publishedFleetSnapshots[snapshot.KnowledgeScope] = snapshot;
        }
        var submitted = _fleetScheduler.Submit(bound, snapshot);
        if (submitted == NavigationSubmissionStatus.CapacityExhausted) return;
        if (submitted is not NavigationSubmissionStatus.Accepted and not NavigationSubmissionStatus.AlreadyPending)
        { CompleteVehicleFailure(pending, NavigationFailureReason.Canceled); return; }
        _fleetSubmissionsThisTick++;
        context.Entities.SetComponent(entity, pending with { BoundSearch = bound });
        _vehicleSearches.Add(entity, new(bound, snapshot, _routingSession, Stopwatch.GetTimestamp()));
    }

    private void AdvanceVehicleSearches(SimulationContext context)
    {
        UpdateRoutingCosts(context);
        if (_fleetScheduler is null || _fleetKernel is null) return;
        foreach (var pair in _vehicleSearches.OrderBy(p => p.Key).ToArray())
        {
            if (MatchesVehicleSearch(context, pair.Key, pair.Value)) continue;
            _fleetScheduler.Cancel(pair.Value.Bound.Binding);
            _vehicleSearches.Remove(pair.Key);
            CompleteVehicleFailure(pair.Value, NavigationFailureReason.StaleNavigationVersion);
        }
        int maintenance = _fleetKernel.BeginTick(context.Tick.Value, 128);
        var batch = _fleetScheduler.Advance(context.Tick, VehicleWorkPerTick - maintenance);
        foreach (var completed in batch.Results)
        {
            if (completed.Search.Status == NavigationSearchStatus.Pending) continue;
            EntityId entity = completed.Binding.Requester;
            if (!_vehicleSearches.TryGetValue(entity, out var work) || work.Bound.Binding != completed.Binding) continue;
            var advance = completed.Search;
            if (!MatchesVehicleSearch(context, entity, work))
            {
                _fleetScheduler.Cancel(work.Bound.Binding);
                CompleteVehicleFailure(work, NavigationFailureReason.StaleNavigationVersion);
            }
            else
            {
                NavigationPath? path = advance.Status == NavigationSearchStatus.Found && advance.Route is { } proposal
                    ? NavigationPath.FromProposal(proposal, work.Snapshot.Grid) : null;
                var request = context.Entities.GetComponent<NavigationPendingPath>(entity).Request;
                _completed.Enqueue(new(request, new(request.NavigationVersion, advance.FailureReason, path),
                    Stopwatch.GetElapsedTime(work.StartedAt)));
                if (advance.Status == NavigationSearchStatus.BudgetExhausted)
                    _fleetScheduler.Cancel(work.Bound.Binding);
            }
            _vehicleSearches.Remove(entity);
        }
    }

    private void AdmitWaitingVehicleSearches(SimulationContext context)
    {
        if (_fleetScheduler is null) return;
        var oldest = new List<(EntityId Entity, NavigationPendingPath Pending)>(MaximumSubmissionsPerTick);
        int inspected = 0;
        foreach (var entity in context.Entities.Query<NavigationPendingPath>(QueryIterationOrder.StableByEntityIndex))
        {
            if (++inspected > 4096) break;
            var pending = context.Entities.GetComponent<NavigationPendingPath>(entity);
            if (!pending.VehicleRouting || pending.BoundSearch is not null ||
                context.Entities.HasComponent<MovementOrder>(entity)) continue;
            int at = oldest.FindIndex(p => p.Pending.Request.RequestId > pending.Request.RequestId);
            if (at < 0) at = oldest.Count;
            if (at >= MaximumSubmissionsPerTick) continue;
            oldest.Insert(at, (entity, pending));
            if (oldest.Count > MaximumSubmissionsPerTick) oldest.RemoveAt(MaximumSubmissionsPerTick);
        }
        foreach (var waiting in oldest) TryStartVehicleSearch(context, waiting.Entity, waiting.Pending);
    }

    private NavigationWorldSnapshot? ResolveFleetSnapshot(NavigationSchedulerSnapshotKey key)
    {
        foreach (var work in _vehicleSearches.Values)
            if (work.Snapshot.Revision == key.Revision && work.Snapshot.Capabilities == key.Capabilities &&
                work.Snapshot.KnowledgeScope == key.KnowledgeScope && work.Snapshot.CostAvailability == key.CostAvailability)
                return work.Snapshot;
        return _publishedFleetSnapshots.TryGetValue(key.KnowledgeScope, out var current) &&
            current.Revision == key.Revision && current.Capabilities == key.Capabilities &&
            current.CostAvailability == key.CostAvailability ? current : null;
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
            vehicle.Owner == binding.OriginalOrder.Issuer && vehicle.Profile == work.Bound.Search.Vehicle &&
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
        NextCommandGeneration = _nextCommandGeneration, CostTick = _costTick,
        Submissions = _fleetSubmissionsThisTick,
        IncompleteCostScopes = _incompleteCosts.Order().ToArray(),
        Congestion = _congestion.OrderBy(p => p.Key).Select(p =>
        {
            var state = p.Value.CaptureState();
            return new { state.Revision, state.Settings, state.KnowledgeScope,
                state.HasTick, state.Tick, Entries = state.Entries.ToArray() };
        }).ToArray(),
        Scheduler = _fleetScheduler?.CaptureAuthoritativeState(),
        Cache = _fleetKernel?.CaptureCacheState(),
        Snapshots = _publishedFleetSnapshots.Values.Concat(_vehicleSearches.Values.Select(w => w.Snapshot))
            .DistinctBy(s => new { s.Revision, s.KnowledgeScope, s.Capabilities, s.CostAvailability })
            .OrderBy(s => s.KnowledgeScope).ThenBy(s => s.Revision.Costs)
            .Select(s => new { s.Revision, s.Capabilities, s.KnowledgeScope, s.CostAvailability, Penalties = s.Penalties }).ToArray()
    };

    internal NavigationFleetDiagnostics? CaptureFleetDiagnostics(PlayerId viewer, SimulationTick tick)
    {
        if (!viewer.IsSpecified || !VehicleSchedulingConnected || tick.Value != _costTick) return null;
        var scheduler = _fleetScheduler!.CaptureViewer(viewer);
        var storage = _fleetKernel!.CaptureScopeDiagnostics(viewer.Value);
        return new(viewer, tick, NavigationModuleAvailability.Available, scheduler.Retained, scheduler.Active,
            scheduler.Retained - scheduler.Active, scheduler.Reserved, scheduler.Wait, scheduler.Work,
            scheduler.TickWork, storage.Rents, storage.Reuses, storage.Returns, storage.PositiveHits,
            storage.NegativeHits, storage.Misses, storage.Writes, storage.BudgetDeferrals,
            VehicleWorkPerTick, VehicleWorkPerSearch, MaximumFleetEntries, MaximumVehicleSearches, FleetLimits.MaximumCacheBytes);
    }

    internal NavigationFleetRequestState? CaptureFleetRequestState(EntityId entity)
    {
        if (_routingEntities is null || !_routingEntities.TryGetComponent(entity, out NavigationPendingPath pending) ||
            !pending.VehicleRouting) return null;
        if (!_vehicleSearches.TryGetValue(entity, out var work)) return NavigationFleetRequestState.WaitingForAdmission;
        return _fleetScheduler!.IsActive(work.Bound.Binding)
            ? NavigationFleetRequestState.Searching : NavigationFleetRequestState.QueuedForBudget;
    }

    private static ulong ContentGeneration(string identity)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char value in identity) hash = unchecked((hash ^ value) * 1099511628211UL);
        return hash == 0 ? 1 : hash;
    }

    public void Dispose()
    {
        _fleetScheduler?.Dispose();
        _fleetKernel?.Dispose();
        _vehicleSearches.Clear(); _publishedFleetSnapshots.Clear(); _routingSnapshots.Clear();
        GC.SuppressFinalize(this);
    }

    private sealed record VehicleSearchWork(NavigationBoundSearch Bound, NavigationWorldSnapshot Snapshot,
        SimulationSessionId SessionId, long StartedAt);
}
