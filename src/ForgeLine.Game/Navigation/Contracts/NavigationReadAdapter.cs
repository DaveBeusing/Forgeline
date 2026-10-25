using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

public readonly record struct NavigationOrderBinding(
    EntityId Requester, MovementOrder OriginalOrder, ulong RequestId, ulong CommandGeneration,
    ulong DockGeneration, ulong QueueGeneration);

public sealed record NavigationBoundSearch(NavigationOrderBinding Binding, NavigationSearchSpecification Search)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Search);
        Search.Validate();
        if (!Binding.Requester.IsValid || !Binding.OriginalOrder.Issuer.IsSpecified ||
            Binding.CommandGeneration == 0 || Binding.RequestId != Search.RequestId ||
            Binding.OriginalOrder.WorldTarget != Search.OriginalDestination)
            throw new InvalidOperationException("The search must retain the complete original Game order binding.");
    }
}

/// <summary>Copied component values, not service authorization or a mutable ECS view.</summary>
public readonly record struct NavigationVehicleObservation(
    EntityId Entity, PlayerId Owner, NavigationPose Pose, NavigationVehicleProfile Profile,
    GroundMovementState Movement, MovementOrder? DirectOrder, MovementOrder? RouteOrder,
    NavigationPendingPath? Pending, VehicleDockVisit? DockVisit, VehicleDockSlot? BuildingSlot,
    VehicleQueueEntry? QueueEntry);

/// <summary>Simulation-thread capture only. These methods never write components or execute a search.</summary>
public static class NavigationReadAdapter
{
    public static bool TryCaptureVehicle(EntityRegistry entities, EntityId entity, float clearanceMeters,
        out NavigationVehicleObservation observation)
    {
        ArgumentNullException.ThrowIfNull(entities);
        observation = default;
        if (!entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out WorldTransform transform) ||
            !entities.TryGetComponent(entity, out GroundMovement movement) ||
            !entities.TryGetComponent(entity, out GroundMovementState state) ||
            !entities.TryGetComponent(entity, out ControllableEntity control)) return false;
        var pose = new NavigationPose(transform.Position, transform.Rotation);
        var profile = new NavigationVehicleProfile(transform.Scale * 0.5f, clearanceMeters,
            movement.MaximumSpeed, movement.Acceleration, movement.Deceleration, movement.TurnRateRadiansPerSecond);
        if (!pose.IsValid || !profile.IsValid || !control.Owner.IsSpecified ||
            !float.IsFinite(state.Velocity.X) || !float.IsFinite(state.Velocity.Y) || !float.IsFinite(state.Velocity.Z) ||
            !float.IsFinite(state.HeadingRadians)) return false;
        VehicleDockVisit? visit = Read<VehicleDockVisit>(entities, entity);
        VehicleDockSlot? slot = visit is VehicleDockVisit current && entities.IsAlive(current.Building)
            ? Read<VehicleDockSlot>(entities, current.Building) : null;
        MovementOrder? routeOrder = entities.TryGetComponent(entity, out NavigationRouteState route)
            ? route.OriginalOrder : null;
        observation = new NavigationVehicleObservation(entity, control.Owner, pose, profile, state,
            Read<MovementOrder>(entities, entity), routeOrder, Read<NavigationPendingPath>(entities, entity),
            visit, slot, Read<VehicleQueueEntry>(entities, entity));
        return true;
    }

    public static NavigationBoundSearch AdaptPendingRequest(in NavigationPendingPath pending,
        Quaternion capturedStartRotation, ulong commandGeneration, ulong dockGeneration, ulong queueGeneration,
        NavigationTargetSemantics semantics, in NavigationGoalConstraints goal, in NavigationVehicleProfile profile,
        NavigationWorldSnapshot snapshot, NavigationIntegrationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        NavigationPathRequest existing = pending.Request;
        if (existing.NavigationVersion != snapshot.Revision.Geometry || existing.Capabilities != snapshot.Capabilities ||
            existing.Destination != pending.OriginalOrder.WorldTarget)
            throw new ArgumentException("Capture must use the matching request, original target and world snapshot.", nameof(pending));
        var search = new NavigationSearchSpecification(existing.RequestId,
            new NavigationPose(existing.Start, capturedStartRotation), pending.OriginalOrder.WorldTarget,
            semantics, goal, existing.Capabilities, profile, snapshot.Revision, snapshot.KnowledgeScope,
            limits.WorkPerSearch, limits.MaximumSearchBytes, limits.MaximumEndpointCandidates);
        var binding = new NavigationOrderBinding(existing.Requester, pending.OriginalOrder,
            existing.RequestId, commandGeneration, dockGeneration, queueGeneration);
        var result = new NavigationBoundSearch(binding, search);
        result.Validate();
        return result;
    }

    public static NavigationObstacleIdentity GetObstacleIdentity(EntityId entity)
    {
        if (!entity.IsValid) throw new ArgumentOutOfRangeException(nameof(entity));
        return new NavigationObstacleIdentity(NavigationObstacleSource.Entity, entity.Index, entity.Generation);
    }

    private static T? Read<T>(EntityRegistry entities, EntityId entity) where T : struct =>
        entities.TryGetComponent(entity, out T value) ? value : null;
}
