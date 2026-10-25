using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class GroundMovementSystem
{
    private LogisticsNavigationCoordinator? _navigationControl;
    internal NavigationLegacyVeto[] CaptureLegacyNavigationVetoes() => _navigationControl?.CaptureLegacyVetoes() ?? [];
    internal bool NavigationConnected => _navigationControl is not null;
    internal void ConnectNavigation(HierarchicalNavigationSystem navigation, TerrainWorld terrain,
        IReadOnlyList<AxisAlignedBounds> authoredObstacles) =>
        _navigationControl = new(navigation, terrain, _options, authoredObstacles);
    internal LogisticsNavigationCheckpoint CaptureNavigationState() =>
        _navigationControl?.CaptureState() ?? throw new InvalidOperationException("Navigation motion is not connected.");

    public void SetNavigationDiagnosticObservations(bool enabled)
    {
        if (_navigationControl is { } coordinator) coordinator.SetDiagnosticObservations(enabled);
    }

    /// <summary>Simulation-thread, viewer-scoped copied diagnostics. Never executes a query or command.</summary>
    public NavigationDiagnosticSnapshot? CaptureNavigationDiagnostics(MatchRuntime runtime, SimulationContext context,
        PlayerId viewer, ReadOnlySpan<ForgeLine.Core.EntityId> visible)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(runtime.Services.GroundMovement, this)) return null;
        return _navigationControl?.CaptureDiagnostics(runtime, context, viewer, visible);
    }

    private bool ApproveLegacyPose(SimulationContext context, EntityId entity, in WorldTransform start,
        in WorldTransform end, in GroundMovementState state, in GroundMovement movement, in MovementOrder order)
    {
        if (_navigationControl is null || _navigationControl.TryApproveLegacyPose(context, entity, start, end)) return true;
        var held = state with { Velocity = Vector3.Zero, Status = GroundMovementStatus.TacticallyPaused,
            ObservedOrderTick = order.AcceptedAtTick, StalledTicks = 0 };
        context.Entities.SetComponent(entity, held);
        AddDebugAgent(entity, start.Position, held, movement, order.WorldTarget, hasTarget: true);
        return false;
    }

    private void ExecuteNavigationMovement(SimulationContext context, float deltaSeconds,
        ref int movingUnits, ref int stuckUnits, ref int arrivedUnits, ref int terrainBlockedUnits)
    {
        if (_navigationControl is not { } coordinator) return;
        var plan = coordinator.Prepare(context);
        var motions = plan.Motions.ToDictionary(static m => m.Input.Binding.Requester);
        var participants = new List<EntityId>(plan.Motions.Count);
        foreach (var entity in context.Entities.Query<WorldTransform, GroundMovementState>(ForgeLine.Ecs.QueryIterationOrder.StableByEntityIndex))
            if (LogisticsNavigationCoordinator.Controls(context.Entities, entity)) participants.Add(entity);
        foreach (var entity in participants)
        {
            if (!LogisticsNavigationCoordinator.Controls(context.Entities, entity) ||
                !context.Entities.TryGetComponent(entity, out GroundMovement movement) ||
                !context.Entities.TryGetComponent(entity, out MovementOrder order)) continue;
            var transform = context.Entities.GetComponent<WorldTransform>(entity);
            var previous = context.Entities.GetComponent<GroundMovementState>(entity);
            if (!motions.TryGetValue(entity, out var accepted) || !CurrentBinding(context, accepted.Input))
            {
                var reason = plan.Reason == NavigationBlockReason.None ? NavigationBlockReason.StaleSnapshot : plan.Reason;
                HoldNavigation(context, entity, transform, previous, movement, order, reason);
                coordinator.ObserveUnavailable(entity, reason);
                continue;
            }
            var proposal = accepted.Proposal;
            if (proposal.Availability != NavigationModuleAvailability.Available)
            {
                HoldNavigation(context, entity, transform, previous, movement, order, proposal.Reason);
                coordinator.ObserveCommit(context, accepted, false);
                continue;
            }
            var route = context.Entities.GetComponent<NavigationRouteState>(entity);
            bool ordinary = accepted.Input.Traffic.Action == NavigationTrafficAction.Proceed;
            int next = accepted.CurrentWaypoint;
            if (ordinary && proposal.End != proposal.Start &&
                !NavigationAcceptedMotionProgress.TryGetNextWaypoint(accepted.Input, proposal, accepted.CurrentWaypoint,
                    accepted.UsedWaypoint, 17, out _, out next))
            {
                HoldNavigation(context, entity, transform, previous, movement, order, NavigationBlockReason.StaleSnapshot);
                coordinator.ObserveUnavailable(entity, NavigationBlockReason.StaleSnapshot);
                continue;
            }
            if (ordinary && next > accepted.CurrentWaypoint && next < route.Path.Waypoints.Count)
            {
                order = new(route.OriginalOrder.Issuer, route.Path.Waypoints[next], route.OriginalOrder.SubmittedAtTick, context.Tick);
                context.Entities.SetComponent(entity, order);
                context.Entities.SetComponent(entity, route with { NextWaypointIndex = next + 1, ActiveWaypointTick = context.Tick });
            }
            Vector3 position = proposal.End.Position;
            Vector3 velocity = (position - transform.Position) / deltaSeconds;
            float heading = MathF.Atan2(Vector3.Transform(Vector3.UnitZ, proposal.End.Rotation).X,
                Vector3.Transform(Vector3.UnitZ, proposal.End.Rotation).Z);
            float oldDistance = Distance(transform.Position, accepted.Input.Binding.OriginalOrder.WorldTarget);
            float distance = Distance(position, accepted.Input.Binding.OriginalOrder.WorldTarget);
            float best = previous.ObservedOrderTick == order.AcceptedAtTick ? previous.PreviousDistanceToTarget : oldDistance;
            if (!float.IsFinite(best)) best = oldDistance;
            bool progressed = best - distance >= _options.ProgressEpsilonMeters;
            int stalled = progressed ? 0 : Math.Min(int.MaxValue - 1, previous.StalledTicks) + 1;
            bool changed = proposal.End != proposal.Start;
            bool arrived = accepted.Input.CanMove && accepted.Input.Traffic.Action != NavigationTrafficAction.Recovery &&
                Distance(position, order.WorldTarget) <= movement.StopRadius + 0.01f;
            var status = arrived ? GroundMovementStatus.Arrived : changed ? GroundMovementStatus.Moving : GroundMovementStatus.TacticallyPaused;
            if (!accepted.Input.CanMove && context.Entities.TryGetComponent(entity, out SupplyMovementConstraint supply) && !supply.CanMove)
                status = GroundMovementStatus.OutOfFuel;
            var state = previous with { Velocity = arrived ? Vector3.Zero : velocity, HeadingRadians = heading,
                Status = status, ObservedOrderTick = order.AcceptedAtTick,
                PreviousDistanceToTarget = progressed ? distance : best,
                StalledTicks = accepted.Input.Traffic.Action == NavigationTrafficAction.Recovery || !changed ? 0 : stalled };
            context.Entities.SetComponent(entity, transform with { Position = position, Rotation = proposal.End.Rotation });
            context.Entities.SetComponent(entity, state);
            if (arrived) { _completedOrders.Add(entity); arrivedUnits++; }
            else if (changed) movingUnits++;
            if (proposal.Reason == NavigationBlockReason.Terrain) terrainBlockedUnits++;
            if (status == GroundMovementStatus.Stuck) stuckUnits++;
            AddDebugAgent(entity, position, state, movement, order.WorldTarget, hasTarget: true);
            coordinator.ObserveCommit(context, accepted, progressed || arrived);
        }
    }

    private static bool CurrentBinding(SimulationContext context, in NavigationMotionInput input) =>
        input.Tick == context.Tick && context.Entities.IsAlive(input.Binding.Requester) &&
        NavigationReadAdapter.TryCaptureVehicle(context.Entities, input.Binding.Requester, input.Vehicle.Profile.ClearanceMeters, out var current) &&
        current.Pose == input.Vehicle.Pose && current.Profile == input.Vehicle.Profile && current.Owner == input.Vehicle.Owner &&
        current.RouteOrder == input.Binding.OriginalOrder && NavigationPrecisionOwnership.Matches(current, input.Binding) &&
        context.Entities.TryGetComponent(input.Binding.Requester, out NavigationRouteState route) &&
        route.SourceRequest?.RequestId == input.Binding.RequestId &&
        context.Entities.TryGetComponent(input.Binding.Requester, out MovementOrder direct) &&
        HierarchicalNavigationSystem.IsManagedWaypoint(direct, route);

    private void HoldNavigation(SimulationContext context, EntityId entity, in WorldTransform transform,
        in GroundMovementState previous, in GroundMovement movement, in MovementOrder order, NavigationBlockReason reason)
    {
        var status = context.Entities.TryGetComponent(entity, out SupplyMovementConstraint supply) && !supply.CanMove
            ? GroundMovementStatus.OutOfFuel : GroundMovementStatus.TacticallyPaused;
        var held = previous with { Velocity = Vector3.Zero, Status = status, ObservedOrderTick = order.AcceptedAtTick, StalledTicks = 0 };
        context.Entities.SetComponent(entity, held);
        _navigationControl?.ObserveUnavailable(entity, reason);
        AddDebugAgent(entity, transform.Position, held, movement, order.WorldTarget, hasTarget: true);
    }

    private static float Distance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
