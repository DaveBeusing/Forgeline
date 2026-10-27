using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed partial class LogisticsNavigationCoordinator
{
    internal NavigationDiagnosticSnapshot? CaptureDiagnostics(MatchRuntime runtime, SimulationContext context,
        PlayerId viewer, ReadOnlySpan<EntityId> visible)
    {
        if (!viewer.IsSpecified || context.Tick.Value != _tick || visible.Length > 4096 ||
            !ReferenceEquals(context.Entities, runtime.Simulation.Entities)) return null;
        var entities = context.Entities;
        var authorized = new HashSet<EntityId>();
        foreach (var entity in visible)
            if (entities.IsAlive(entity) && entities.TryGetComponent(entity, out ControllableEntity control) &&
                control.Owner == viewer && control.IsControllable &&
                (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted))
                authorized.Add(entity);
        var revision = _snapshot is { } currentSnapshot && currentSnapshot.Revision.Geometry == _navigation.World.Version
            ? currentSnapshot.Revision : new NavigationSnapshotRevision(_navigation.World.Version, 0);
        var observations = new List<NavigationDiagnosticVehicle>();
        var traffic = _traffic.CaptureState();
        foreach (var entity in authorized.Order())
        {
            if (!entities.TryGetComponent(entity, out UnitIdentity unit) ||
                unit.UnitId != UnitIds.CargoTruck && unit.UnitId != UnitIds.SupplyTruck ||
                !NavigationMotionCapture.TryCaptureVehicle(entities, entity, _movementOptions, out var vehicle)) continue;
            if (observations.Count == 128) return null;
            bool hasActor = _actors.TryGetValue(entity, out var actor) && actor.Active && actor.LastTick == _tick &&
                actor.OriginalOrder == vehicle.RouteOrder && actor.OriginalOrder.Issuer == viewer;
            PredictiveTrafficVehicleState? wait = null;
            foreach (var state in traffic.Vehicles)
                if (state.Entity == entity && hasActor && state.CommandGeneration == actor.Generation && state.LastTick == _tick)
                    wait = state;
            var root = vehicle.Pending?.OriginalOrder ?? vehicle.RouteOrder ?? vehicle.DirectOrder;
            bool ownedPending = vehicle.Pending is { } pending && pending.OriginalOrder.Issuer == viewer;
            if (root is { } foreignRoot && foreignRoot.Issuer != viewer) root = null;
            ulong? request = ownedPending ? vehicle.Pending!.Value.Request.RequestId : null;
            var route = Array.Empty<Vector3>();
            bool completeRoute = false;
            NavigationSearchStatus? search = ownedPending ? NavigationSearchStatus.Pending : null;
            if (entities.TryGetComponent(entity, out NavigationRouteState routeState) && routeState.SourceRequest is { } source &&
                routeState.OriginalOrder.Issuer == viewer && source.Requester == entity && source.Destination == routeState.OriginalOrder.WorldTarget &&
                source.NavigationVersion == revision.Geometry && routeState.Path.Version == revision.Geometry)
            {
                request = source.RequestId;
                search = NavigationSearchStatus.Found;
                int firstWaypoint = Math.Max(0, routeState.NextWaypointIndex - 1);
                completeRoute = routeState.Path.Waypoints.Count - firstWaypoint <= 128;
                route = routeState.Path.Waypoints.Skip(firstWaypoint).Take(128).ToArray();
            }
            else if (root is null && entities.TryGetComponent(entity, out NavigationFailureState failed) &&
                failed.OriginalOrder.Issuer == viewer && failed.FailedAtTick <= context.Tick)
            {
                root = failed.OriginalOrder;
                search = failed.NavigationVersion != revision.Geometry ? NavigationSearchStatus.Stale :
                    failed.FailureReason == NavigationFailureReason.SearchBudgetExhausted ? NavigationSearchStatus.BudgetExhausted :
                    failed.FailureReason == NavigationFailureReason.Canceled ? NavigationSearchStatus.Canceled : NavigationSearchStatus.Unreachable;
            }
            var queue = vehicle.QueueEntry is { } entry && entry.Owner == viewer && entry.Generation > 0 && entry.Position > 0 && authorized.Contains(entry.Building)
                ? entry with { Predecessor = authorized.Contains(entry.Predecessor) ? entry.Predecessor : default } : (VehicleQueueEntry?)null;
            var dock = vehicle.DockVisit is { } visit && authorized.Contains(visit.Building) ? visit : (VehicleDockVisit?)null;
            bool? owns = dock is { Generation: > 0 } current
                ? vehicle.BuildingSlot is { } slot && slot.Truck == entity && slot.Generation == current.Generation && slot.Owner == viewer
                : dock.HasValue ? false : null;
            var (maximum, canMove) = CaptureConstraints(context, entity, vehicle.Profile.MaximumSpeed);
            double? fuel = null, capacity = null;
            if (entities.TryGetComponent(entity, out UnitFuelState fuelState) && runtime.Inventories.Contains(fuelState.InventoryId))
            {
                fuel = runtime.Inventories.GetQuantity(fuelState.InventoryId, ResourceIds.Fuel);
                capacity = fuelState.Capacity;
            }
            EntityId? blocker = wait is { } waiting && authorized.Contains(waiting.WaitingFor) ? waiting.WaitingFor : null;
            NavigationBlockReason? reason = hasActor ? actor.LastReason : null;
            if (_legacyVetoes.TryGetValue(entity, out var veto) && veto.Tick == context.Tick)
            {
                reason = veto.Reason;
                if (veto.Blocker is { Source: NavigationObstacleSource.Entity } identity &&
                    identity.Value <= uint.MaxValue && identity.Generation <= uint.MaxValue)
                {
                    var candidate = new EntityId((uint)identity.Value, (uint)identity.Generation);
                    if (authorized.Contains(candidate)) blocker = candidate;
                }
            }
            NavigationDiagnosticConflict? conflict = null;
            if (blocker is { } visibleBlocker && reason is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle &&
                entities.TryGetComponent(visibleBlocker, out WorldTransform blockerPose) &&
                entities.TryGetComponent(visibleBlocker, out SpatialPresence blockerPresence) &&
                NavigationMotionCapture.TryCaptureBody(entities, blockerPresence.CreateEntry(visibleBlocker, blockerPose), out var body))
                conflict = new(new(NavigationModuleAvailability.Available, context.Tick, revision),
                    visibleBlocker, body.Pose, body.HalfExtents, body.Kind);
            var ownedLeases = traffic.Leases.Where(l => l.Owner == entity).ToArray();
            var scopedLeases = ownedLeases.Where(l => actor.Entity == entity && actor.OriginalOrder.Issuer == viewer &&
                l.CommandGeneration == actor.Generation && l.LastTick == _tick).ToArray();
            bool completeReservations = scopedLeases.Length == ownedLeases.Length && scopedLeases.Length <= 32;
            var passages = scopedLeases.Take(32).Select(l => new NavigationDiagnosticReservation(
                new(NavigationModuleAvailability.Available, context.Tick, revision), l.Owner, l.CommandGeneration,
                new(l.GrantedAt), new(l.LastTick), l.Entered, hasActor, l.Passage,
                l.GrantedAt == _tick ? revision.Geometry : null)).ToArray();
            NavigationMotionProposal? actualMotion = hasActor &&
                _diagnosticMotions.TryGetValue(entity, out var proposal) && proposal.Tick == context.Tick &&
                proposal.Binding.CommandGeneration == actor.Generation && proposal.Binding.RequestId == request &&
                proposal.Binding.OriginalOrder == root ? proposal : null;
            NavigationDiagnosticReplan? lastReplan = _diagnosticReplans.TryGetValue(entity, out var replan) &&
                replan.OriginalOrder == root && replan.RequestId == request && replan.Source.SourceTick <= context.Tick &&
                replan.Source.Revision == revision ? replan : null;
            NavigationCheckedHorizon? checkedHorizon = actualMotion is { } motion &&
                _diagnosticHorizons.TryGetValue(entity, out var horizon) && horizon.Binding == motion.Binding &&
                horizon.Source.SourceTick == context.Tick && horizon.Source.Revision == revision ? horizon : null;
            var facts = new NavigationDiagnosticVehicleFacts(viewer, entity, unit.UnitId,
                new(NavigationModuleAvailability.Available, context.Tick, revision), vehicle.Pose, vehicle.Profile,
                vehicle.Movement.Status, root, request, hasActor ? actor.Generation : ownedPending ? vehicle.Pending!.Value.RootGeneration : null,
                new(hasActor ? NavigationModuleAvailability.Available : NavigationModuleAvailability.Unavailable, context.Tick, revision),
                hasActor ? actor.LastAction : null, reason, hasActor ? new SimulationTick(actor.LastProgressTick) : null,
                wait is { } w ? _tick - w.WaitingSince : hasActor ? 0UL : null,
                hasActor ? actor.ReplanCount : lastReplan?.ActorReplans, wait?.RecoveryTarget, blocker, search, queue, dock, owns,
                canMove, maximum, fuel, capacity, actualMotion, lastReplan, checkedHorizon, conflict, _navigation.CaptureFleetRequestState(entity));
            observations.Add(new(facts, route, completeRoute, passages, completeReservations));
        }
        return new(runtime.Simulation.SessionId, viewer, context.Tick, revision,
            new(NavigationModuleAvailability.Available, NavigationModuleAvailability.Available,
                _navigation.VehicleRoutingConnected ? NavigationModuleAvailability.Available : NavigationModuleAvailability.NotConnected,
                _navigation.VehicleSchedulingConnected ? NavigationModuleAvailability.Available : NavigationModuleAvailability.NotConnected,
                NavigationModuleAvailability.NotConnected, NavigationModuleAvailability.Available),
            Limits, observations.ToArray(), runtime.Progress.CaptureNavigationDeliveryWindow(viewer, context.Tick), _navigation.CaptureFleetDiagnostics(viewer, context.Tick));
    }
}
