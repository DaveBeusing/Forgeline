using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

internal readonly record struct LogisticsNavigationActorState(
    EntityId Entity, MovementOrder OriginalOrder, ulong Generation, ulong RequestId, bool Active,
    ulong LastTick, ulong LastProgressTick, ulong LastReplanTick, ulong ReplanCount,
    NavigationTrafficAction LastAction, NavigationBlockReason LastReason);
internal sealed record LogisticsNavigationCheckpoint(
    int Revision, ulong Tick, LogisticsNavigationActorState[] Actors, PredictiveTrafficCheckpoint Traffic);
internal readonly record struct NavigationApprovedMotion(
    NavigationMotionInput Input, NavigationMotionProposal Proposal, int CurrentWaypoint, int UsedWaypoint);
internal sealed record LogisticsNavigationPlan(
    IReadOnlyList<NavigationApprovedMotion> Motions, bool Complete, int WorkConsumed, NavigationBlockReason Reason);

/// <summary>Simulation-thread coordinator. GroundMovement retains every transform/order commit.</summary>
internal sealed partial class LogisticsNavigationCoordinator
{
    internal const int MaximumVehicles = 128, MaximumBodies = 4096, MaximumLocalBodies = 128;
    private const int MaximumHistory = 512, MaximumWaypoints = 4096;
    private readonly HierarchicalNavigationSystem _navigation;
    private readonly TerrainWorld _terrain;
    private readonly GroundMovementSystemOptions _movementOptions;
    private readonly AxisAlignedBounds[] _authored;
    private readonly bool _authoredOverflow;
    private readonly PredictiveTrafficPlanner _traffic = new();
    private readonly ContinuousNavigationMotionPlanner _motion = new(new(MaximumWaypoints, 16, 24, 2, 12));
    private readonly NavigationMotionArbitrator _arbitrator = new(MaximumVehicles, MaximumBodies);
    private readonly Dictionary<EntityId, LogisticsNavigationActorState> _actors = new();
    private readonly List<EntityId> _active = new(MaximumVehicles);
    private readonly Dictionary<EntityId, RouteCache> _routes = new();
    private NavigationWorld? _sourceWorld;
    private NavigationWorldSnapshot? _snapshot;
    private NavigationVehicleGeometry? _geometry;
    private readonly List<NavigationCollisionBody> _legacyEnvelopes = new(128);
    private bool _legacyComplete = true;
    private readonly List<NavigationCollisionBody> _protectedLogistics = new(MaximumVehicles);
    private bool _protectedComplete;
    private int _legacyWork;
    private bool _overflow;
    private ulong _tick;

    internal NavigationIntegrationLimits Limits { get; } = new(
        262144, 32768, MaximumVehicles, 16 * 1024 * 1024, 16 * 1024 * 1024,
        25, 60, 3, MaximumVehicles, 128, 128);

    internal LogisticsNavigationCoordinator(HierarchicalNavigationSystem navigation, TerrainWorld terrain,
        GroundMovementSystemOptions movementOptions, IReadOnlyList<AxisAlignedBounds> authoredObstacles)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(movementOptions);
        ArgumentNullException.ThrowIfNull(authoredObstacles);
        _authoredOverflow = authoredObstacles.Count > MaximumBodies;
        _authored = authoredObstacles.Take(MaximumBodies).ToArray();
        _navigation = navigation; _terrain = terrain; _movementOptions = movementOptions;
        Limits.Validate();
    }

    internal static bool Controls(EntityRegistry entities, EntityId entity)
    {
        return entities.TryGetComponent(entity, out UnitIdentity identity) &&
            (identity.UnitId == UnitIds.CargoTruck || identity.UnitId == UnitIds.SupplyTruck) &&
            entities.TryGetComponent(entity, out NavigationRouteState route) &&
            route.OriginalOrder.Kind == MovementOrderKind.Strategic &&
            entities.TryGetComponent(entity, out MovementOrder direct) &&
            HierarchicalNavigationSystem.IsManagedWaypoint(direct, route) &&
            (!NavigationReadAdapter.TryCaptureVehicle(entities, entity, 0, out var observation) ||
                !NavigationPrecisionOwnership.IsPrecise(observation));
    }

    // Called before legacy movement; identification does not move or replace an order.
    internal void BeginTick(SimulationContext context)
    {
        var previouslyActive = _actors.Values.Where(static a => a.Active).Select(static a => a.Entity).ToHashSet();
        _tick = context.Tick.Value; _active.Clear(); _overflow = false;
        _legacyEnvelopes.Clear(); _legacyVetoes.Clear(); _physicalWaits.Clear(); _legacyComplete = true; _legacyWork = 0;
        _protectedLogistics.Clear(); _protectedComplete = true;
        foreach (var key in _actors.Keys.ToArray())
        {
            if (!context.Entities.IsAlive(key)) { _actors.Remove(key); _routes.Remove(key); }
            else _actors[key] = _actors[key] with { Active = false, LastTick = _tick };
        }
        foreach (var entity in context.Entities.Query<WorldTransform, GroundMovementState>(QueryIterationOrder.StableByEntityIndex))
        {
            if (IsLogisticsVehicle(context.Entities, entity))
            {
                if (_protectedLogistics.Count == MaximumVehicles ||
                    !context.Entities.TryGetComponent(entity, out SpatialPresence presence) ||
                    !NavigationMotionCapture.TryCaptureBody(context.Entities,
                        presence.CreateEntry(entity, context.Entities.GetComponent<WorldTransform>(entity)), out var body))
                    _protectedComplete = false;
                else _protectedLogistics.Add(body);
            }
            if (!Controls(context.Entities, entity)) continue;
            if (_active.Count == MaximumVehicles) { _overflow = true; continue; }
            var route = context.Entities.GetComponent<NavigationRouteState>(entity);
            ulong request = route.SourceRequest?.RequestId ?? 0;
            if (!_actors.TryGetValue(entity, out var actor))
            {
                if (_actors.Count == MaximumHistory) { _overflow = true; continue; }
                actor = new(entity, route.OriginalOrder, 1, request, true, _tick, _tick, 0, 0,
                    NavigationTrafficAction.Hold, NavigationBlockReason.Unknown);
            }
            else if (!previouslyActive.Contains(entity) || actor.OriginalOrder != route.OriginalOrder || actor.RequestId != request)
            {
                actor = actor with { OriginalOrder = route.OriginalOrder, Generation = checked(actor.Generation + 1),
                    RequestId = request, LastProgressTick = _tick, LastAction = NavigationTrafficAction.Hold };
                _routes.Remove(entity);
            }
            _actors[entity] = actor with { Active = true, LastTick = _tick };
            _active.Add(entity);
        }
        var live = _active.ToHashSet();
        foreach (var entity in _routes.Keys.Where(e => !live.Contains(e)).ToArray()) _routes.Remove(entity);
    }

    internal LogisticsNavigationPlan Prepare(SimulationContext context)
    {
        if (_overflow) return Unavailable(NavigationBlockReason.SearchWork);
        if (_active.Count == 0 && !_traffic.HasReservations)
            return new(Array.Empty<NavigationApprovedMotion>(), true, _legacyWork, NavigationBlockReason.None);
        EnsureWorld();
        return PrepareCaptured(context);
    }

    private void EnsureWorld()
    {
        var world = _navigation.World;
        if (ReferenceEquals(world, _sourceWorld)) return;
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        _snapshot = new(world, world.GetSectorGraph(capabilities), capabilities, _terrain,
            0, 1, NavigationModuleAvailability.NotConnected, [], []);
        _geometry = new(_snapshot, _terrain, 256, 1);
        _sourceWorld = world; _routes.Clear();
    }

    internal LogisticsNavigationCheckpoint CaptureState() => new(1, _tick,
        _actors.Values.OrderBy(static a => a.Entity).ToArray(), _traffic.CaptureState());

    internal void RestoreState(LogisticsNavigationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Revision != 1 || checkpoint.Actors is null || checkpoint.Actors.Length > MaximumHistory)
            throw new InvalidOperationException("Invalid logistics navigation checkpoint.");
        var actors = new Dictionary<EntityId, LogisticsNavigationActorState>();
        foreach (var actor in checkpoint.Actors)
            if (!actor.Entity.IsValid || actor.Generation == 0 || !actor.OriginalOrder.Issuer.IsSpecified ||
                !NavigationMotionCapture.Finite(actor.OriginalOrder.WorldTarget) || actor.LastTick > checkpoint.Tick ||
                actor.LastProgressTick > actor.LastTick || actor.LastReplanTick > actor.LastTick ||
                !Enum.IsDefined(actor.LastAction) || !Enum.IsDefined(actor.LastReason) || !actors.TryAdd(actor.Entity, actor))
                throw new InvalidOperationException("Invalid logistics navigation participant.");
        // Validate both domains before replacing either one.
        var validated = new PredictiveTrafficPlanner();
        validated.RestoreState(checkpoint.Traffic);
        _traffic.RestoreState(validated.CaptureState());
        _actors.Clear();
        foreach (var pair in actors) _actors.Add(pair.Key, pair.Value);
        _tick = checkpoint.Tick; _active.Clear(); _routes.Clear(); _overflow = false;
    }

    internal void ObserveCommit(SimulationContext context, in NavigationApprovedMotion accepted, bool progressed)
    {
        var binding = accepted.Input.Binding;
        if (!_actors.TryGetValue(binding.Requester, out var actor) || actor.Generation != binding.CommandGeneration ||
            actor.OriginalOrder != binding.OriginalOrder || actor.RequestId != binding.RequestId) return;
        actor = actor with { LastTick = context.Tick.Value, LastAction = accepted.Input.Traffic.Action,
            LastReason = accepted.Proposal.Reason, LastProgressTick = progressed ? context.Tick.Value : actor.LastProgressTick };
        // Expected FIFO/passage waits are not failed searches. An active escape keeps its route.
        if (context.Tick.Value - actor.LastProgressTick >= (ulong)Limits.RetryIntervalTicks &&
            context.Tick.Value - actor.LastReplanTick >= (ulong)Limits.RetryIntervalTicks &&
            accepted.Input.Traffic.Availability == NavigationModuleAvailability.Available &&
            accepted.Input.Traffic.Action != NavigationTrafficAction.Recovery &&
            accepted.Proposal.End == accepted.Proposal.Start &&
            accepted.Proposal.Reason is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle or NavigationBlockReason.Terrain &&
            _navigation.TryRequestTrafficReplan(context, binding))
            actor = actor with { LastReplanTick = context.Tick.Value, ReplanCount = checked(actor.ReplanCount + 1) };
        _actors[binding.Requester] = actor;
    }

    private static LogisticsNavigationPlan Unavailable(NavigationBlockReason reason, int work = 0) =>
        new(Array.Empty<NavigationApprovedMotion>(), false, work, reason);
    private sealed record RouteCache(NavigationPath Path, NavigationWorldSnapshot Snapshot,
        NavigationVehicleProfile Profile, ulong RequestId, NavigationRouteProposal Proposal);
}
