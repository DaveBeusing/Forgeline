using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed partial class LogisticsNavigationCoordinator
{
    internal static bool IsLogisticsVehicle(EntityRegistry entities, EntityId entity) =>
        entities.TryGetComponent(entity, out UnitIdentity identity) &&
        (identity.UnitId == UnitIds.CargoTruck || identity.UnitId == UnitIds.SupplyTruck);

    internal bool TryApproveLegacyPose(SimulationContext context, EntityId entity,
        in WorldTransform start, in WorldTransform end)
    {
        if (!IsLogisticsVehicle(context.Entities, entity) || start.Position == end.Position && start.Rotation == end.Rotation) return true;
        if (!_legacyComplete || _legacyWork >= Limits.WorkPerTick ||
            !NavigationMotionCapture.TryCaptureVehicle(context.Entities, entity, _movementOptions, out var vehicle) ||
            VehicleDockingService.BlocksStagingStep(context.Entities, entity, start, end)) return false;
        EnsureWorld();
        var physical = CapturePhysical(context, _snapshot!, Limits.WorkPerTick - _legacyWork);
        _legacyWork += physical.Work;
        if (!physical.Collision.IsComplete) return false;
        var collision = CaptureNeighborhood(physical, vehicle, context.Tick, ref _legacyWork);
        if (collision is null || !collision.IsComplete) return false;
        var from = new NavigationPose(start.Position, start.Rotation);
        var to = new NavigationPose(end.Position, end.Rotation);
        if (!_traffic.TryCheckPassage(entity, from, to, vehicle.Profile, Limits.WorkPerTick - _legacyWork,
            out int passageWork, out _)) { _legacyWork += passageWork; return false; }
        _legacyWork += passageWork;
        int remaining = Limits.WorkPerTick - _legacyWork;
        if (remaining == 0) return false;
        var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(entity), from, to,
            vehicle.Profile, (float)context.TickDuration.TotalSeconds);
        var result = _geometry!.Sweep(collision, sweep, remaining);
        if (result.WorkConsumed <= 0 || result.WorkConsumed > remaining)
        { _legacyWork = Limits.WorkPerTick; return false; }
        _legacyWork += result.WorkConsumed;
        return result.Status == NavigationGeometryStatus.Clear && result.AllowedFraction == 1 &&
            result.FrameTick == context.Tick.Value && result.Revision == collision.World.Revision;
    }

    internal void RecordLegacyMotion(SimulationContext context, EntityId entity, in WorldTransform start, in WorldTransform end)
    {
        if (start.Position == end.Position && start.Rotation == end.Rotation ||
            !context.Entities.HasComponent<SpatialPresence>(entity)) return;
        if (_legacyEnvelopes.Count == 512 || !NavigationMotionCapture.Spend(Limits.WorkPerTick, ref _legacyWork))
        { _legacyComplete = false; return; }
        if (!context.Entities.TryGetComponent(entity, out GroundMovement movement)) { _legacyComplete = false; return; }
        var profile = new NavigationVehicleProfile(start.Scale * 0.5f, _movementOptions.ObstacleClearanceMeters,
            movement.MaximumSpeed, movement.Acceleration, movement.Deceleration, movement.TurnRateRadiansPerSecond);
        var from = new NavigationPose(start.Position, start.Rotation);
        var to = new NavigationPose(end.Position, end.Rotation);
        if (!profile.IsValid || !from.IsValid || !to.IsValid ||
            !NavigationMotionArbitrator.TryBuildSweptBounds(from, to, profile, out var bounds))
        { _legacyComplete = false; return; }
        _legacyEnvelopes.Add(new(new(NavigationObstacleSource.TrafficReservation, (ulong)entity.Index + 1, entity.Generation),
            NavigationObstacleKind.Reservation, new(bounds.Center, Quaternion.Identity), bounds.Extents, Vector3.Zero));
    }

    internal void ObserveUnavailable(EntityId entity, NavigationBlockReason reason)
    {
        if (_actors.TryGetValue(entity, out var actor))
            _actors[entity] = actor with { LastTick = _tick, LastAction = NavigationTrafficAction.Hold, LastReason = reason };
    }
}
