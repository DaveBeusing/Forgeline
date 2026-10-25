using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

internal enum RegionalObservationStatus : byte
{
    Unavailable,
    NonBlocking,
    Blocking
}

internal readonly record struct RegionalObstacleObservation(
    EntityId Entity,
    RegionalObservationStatus Status,
    AxisAlignedBounds? Bounds,
    bool IsInfrastructure = false);

/// <summary>Read-only source helpers; source revisions and removal events belong to integration.</summary>
internal static class RegionalObstacleCapture
{
    public static RegionalObstacleObservation CaptureStaticEntity(EntityRegistry entities, EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(entities);
        // Missing components are not evidence for an authoritative removal.
        if (!entity.IsValid || !entities.TryGetComponent(entity, out WorldTransform transform) ||
            !entities.TryGetComponent(entity, out SpatialPresence presence))
            return new RegionalObstacleObservation(entity, RegionalObservationStatus.Unavailable, null);

        if (presence.Metadata.Mobility != SpatialMobility.Static ||
            (entities.TryGetComponent(entity, out ConveyorPiece conveyor) && !conveyor.Placement.IsSupport))
            return new RegionalObstacleObservation(entity, RegionalObservationStatus.NonBlocking, null);

        // Use the existing physical AABB. Render rotation/scale is not a new
        // collision model, and decorative wreck meshes are not physical sources.
        return new RegionalObstacleObservation(entity, RegionalObservationStatus.Blocking,
            presence.CreateEntry(entity, transform).Bounds);
    }

    public static RegionalObstacleObservation CaptureInfrastructure(EntityRegistry entities, EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (!entity.IsValid || !entities.TryGetComponent(entity, out StrategicInfrastructure infrastructure) ||
            !entities.TryGetComponent(entity, out StrategicInfrastructureState state))
            return new RegionalObstacleObservation(entity, RegionalObservationStatus.Unavailable, null, IsInfrastructure: true);

        return state.State == StrategicInfrastructureOperationalState.Operational
            ? new RegionalObstacleObservation(entity, RegionalObservationStatus.NonBlocking, null, IsInfrastructure: true)
            : new RegionalObstacleObservation(entity, RegionalObservationStatus.Blocking, infrastructure.NavigationBlocker, IsInfrastructure: true);
    }

    public static bool TryCreateChange(in RegionalObstacleObservation observation,
        NavigationObstacleIdentity identity, NavigationVersion sourceRevision,
        AxisAlignedBounds? previousBounds, out NavigationObstacleChange change)
    {
        change = default;
        if (!observation.Entity.IsValid || !sourceRevision.IsValid || identity.Generation == 0 ||
            observation.Status is not (RegionalObservationStatus.NonBlocking or RegionalObservationStatus.Blocking) ||
            (observation.Status == RegionalObservationStatus.Blocking) != observation.Bounds.HasValue)
            return false;
        // Infrastructure blockers need a stable authored identity separate from
        // the same entity's physical shape; the source owner supplies that key.
        if (observation.IsInfrastructure
            ? identity.Source != NavigationObstacleSource.AuthoredGeometry
            : identity != NavigationReadAdapter.GetObstacleIdentity(observation.Entity))
            return false;
        change = new NavigationObstacleChange(identity, sourceRevision, previousBounds, observation.Bounds);
        return true;
    }
}
