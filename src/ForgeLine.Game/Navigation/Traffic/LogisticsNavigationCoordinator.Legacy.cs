using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal readonly record struct NavigationLegacyVeto(EntityId Entity, SimulationTick Tick, NavigationBlockReason Reason, NavigationObstacleIdentity? Blocker);

internal sealed partial class LogisticsNavigationCoordinator
{
    private readonly Dictionary<EntityId, NavigationLegacyVeto> _legacyVetoes = new();
    internal NavigationLegacyVeto[] CaptureLegacyVetoes() => _legacyVetoes.Values.OrderBy(static v => v.Entity).ToArray();
    private void ObserveLegacyVeto(EntityId entity, SimulationTick tick, in NavigationSweepResult result)
    {
        if (result.Status == NavigationGeometryStatus.Clear) { _legacyVetoes.Remove(entity); return; }
        if (_legacyVetoes.Count < MaximumVehicles || _legacyVetoes.ContainsKey(entity))
            _legacyVetoes[entity] = new(entity, tick, result.Reason, result.Blocker);
    }

    internal static bool IsLogisticsVehicle(EntityRegistry entities, EntityId entity) =>
        entities.TryGetComponent(entity, out UnitIdentity identity) &&
        (identity.UnitId == UnitIds.CargoTruck || identity.UnitId == UnitIds.SupplyTruck);

    internal bool TryApproveLegacyPose(SimulationContext context, EntityId entity,
        in WorldTransform start, in WorldTransform end)
    {
        if (start.Position == end.Position && start.Rotation == end.Rotation) return true;
        if (!IsLogisticsVehicle(context.Entities, entity)) return TryProtectLogistics(context, entity, start, end);
        if (!_legacyComplete || _legacyWork >= Limits.WorkPerTick ||
            !NavigationMotionCapture.TryCaptureVehicle(context.Entities, entity, _movementOptions, out var vehicle) ||
            VehicleDockingService.BlocksStagingStep(context.Entities, entity, start, end)) return false;
        EnsureWorld();
        var physical = CapturePhysical(context, _snapshot!, Limits.WorkPerTick - _legacyWork);
        _legacyWork += physical.Work;
        if (!physical.Collision.IsComplete) return false;
        var collision = CaptureNeighborhood(physical, vehicle, context.Tick, ref _legacyWork);
        if (collision is null || !collision.IsComplete) return false;
        collision = FreezePoses(collision, ref _legacyWork);
        if (collision is null) return false;
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
        ObserveLegacyVeto(entity, context.Tick, result);
        return result.Status == NavigationGeometryStatus.Clear && result.AllowedFraction == 1 &&
            result.FrameTick == context.Tick.Value && result.Revision == collision.World.Revision;
    }

    internal bool TryProposeLegacyQueueReverse(SimulationContext context, EntityId entity,
        in WorldTransform start, float maximumSpeed, out WorldTransform end, out float heading)
    {
        end = start; heading = 0;
        if (!IsLogisticsVehicle(context.Entities, entity) || maximumSpeed <= 0 ||
            !NavigationMotionCapture.TryCaptureVehicle(context.Entities, entity, _movementOptions, out var vehicle) ||
            vehicle.QueueEntry is not { } queue || queue.ArrivedAtService || queue.Generation == 0 || queue.Position <= 0 || queue.Owner != vehicle.Owner ||
            vehicle.DockVisit is { LaneEntered: true } ||
            vehicle.DirectOrder is not { } order || order.Issuer != vehicle.Owner ||
            order.TargetHeadingRadians.HasValue && order.WorldTarget != queue.Target) return false;
        bool ownsQueueMotion = context.Entities.TryGetComponent(entity, out VehicleQueueMotion motion) &&
            motion.Generation == queue.Generation;
        // Admission removes VehicleQueueMotion from the FIFO head before it has
        // reached the approach pose. Its exact reserved dock still owns that
        // approach; absence of the old queue controller is not loss of authority.
        bool currentApproachTarget = order.WorldTarget == queue.Target ||
            vehicle.RouteOrder is { } root && root.Issuer == vehicle.Owner && root.WorldTarget == queue.Target &&
            context.Entities.TryGetComponent(entity, out NavigationRouteState queuedRoute) &&
            HierarchicalNavigationSystem.IsManagedWaypoint(order, queuedRoute);
        bool ownsReservedApproach = queue.Position == 1 &&
            vehicle.DockVisit is { Generation: > 0, LaneEntered: false } reserved && reserved.Building == queue.Building &&
            vehicle.BuildingSlot is { } reservedSlot && reservedSlot.Truck == entity &&
            reservedSlot.Owner == vehicle.Owner && reservedSlot.Generation == reserved.Generation &&
            reservedSlot.Pose == reserved.Pose && reserved.Pose.ApproachStart == queue.Target && currentApproachTarget;
        if (!ownsQueueMotion && !ownsReservedApproach) return false;
        if (vehicle.DockVisit is { } visit &&
            (visit.Building != queue.Building || visit.Generation > 0 &&
                (vehicle.BuildingSlot is not { } slot || slot.Truck != entity || slot.Generation != visit.Generation || slot.Owner != vehicle.Owner)))
            return false;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, start.Rotation);
        if (Vector3.Dot(queue.Target - start.Position, forward) >= 0) return false;
        EnsureWorld();
        if (!NavigationReverseMotion.TryStep(vehicle, queue.Target, MathF.Min(1, maximumSpeed),
            (float)context.TickDuration.TotalSeconds, _snapshot!, out var proposal, out float speed, out heading, arrivalTolerance: .05f) ||
            speed <= 0 || speed > MathF.Min(1, maximumSpeed) + .00001f)
            return false;
        end = start with { Position = proposal.Position, Rotation = proposal.Rotation };
        return true;
    }

    // Earlier non-logistics movement cannot enter a logistics footprint and then
    // leave the protected vehicle trapped in an initial-overlap hold. Precise and
    // coarse logistics move only after this pass and see its accepted envelopes.
    private bool TryProtectLogistics(SimulationContext context, EntityId entity, in WorldTransform start, in WorldTransform end)
    {
        if (!_protectedComplete) return false;
        if (_protectedLogistics.Count == 0) return true;
        if (!context.Entities.TryGetComponent(entity, out GroundMovement movement)) return false;
        var profile = new NavigationVehicleProfile(start.Scale * .5f, _movementOptions.ObstacleClearanceMeters,
            movement.MaximumSpeed, movement.Acceleration, movement.Deceleration, movement.TurnRateRadiansPerSecond);
        var from = new NavigationPose(start.Position, start.Rotation);
        var to = new NavigationPose(end.Position, end.Rotation);
        if (!profile.IsValid || !NavigationMotionArbitrator.TryBuildSweptBounds(from, to, profile, out var swept)) return false;
        bool near = false;
        foreach (var body in _protectedLogistics)
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref _legacyWork)) return false;
            Vector3 projected = Vector3.Abs(Vector3.Transform(Vector3.UnitX, body.Pose.Rotation)) * body.HalfExtents.X +
                Vector3.Abs(Vector3.Transform(Vector3.UnitY, body.Pose.Rotation)) * body.HalfExtents.Y +
                Vector3.Abs(Vector3.Transform(Vector3.UnitZ, body.Pose.Rotation)) * body.HalfExtents.Z;
            Vector3 minimum = body.Pose.Position - projected, maximum = body.Pose.Position + projected;
            near |= swept.Minimum.X <= maximum.X && swept.Maximum.X >= minimum.X &&
                swept.Minimum.Z <= maximum.Z && swept.Maximum.Z >= minimum.Z;
        }
        if (!near) return true;
        EnsureWorld();
        var bounds = _snapshot!.WorldBounds;
        var coverage = new ForgeLine.World.AxisAlignedBounds(new(bounds.Minimum.X, bounds.Minimum.Y - 1024, bounds.Minimum.Z),
            new(bounds.Maximum.X, bounds.Maximum.Y + 1024, bounds.Maximum.Z));
        var collision = new NavigationCollisionFrame(_snapshot, context.Tick.Value, coverage, true,
            _protectedLogistics.Select(static b => b with { Velocity = Vector3.Zero }).ToArray(), MaximumVehicles, bodyPosesAreFixed: true);
        int allowance = Limits.WorkPerTick - _legacyWork;
        if (allowance <= 0) return false;
        var result = _geometry!.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(entity), from, to,
            profile, (float)context.TickDuration.TotalSeconds), allowance);
        if (result.WorkConsumed <= 0 || result.WorkConsumed > allowance) { _legacyWork = Limits.WorkPerTick; return false; }
        _legacyWork += result.WorkConsumed;
        return result.Status == NavigationGeometryStatus.Clear && result.AllowedFraction == 1 &&
            result.FrameTick == context.Tick.Value && result.Revision == collision.World.Revision;
    }

    private NavigationCollisionFrame? FreezePoses(NavigationCollisionFrame source, ref int consumed)
    {
        if (source.BodyPosesAreFixed) return source;
        if (source.Bodies.Count > Limits.WorkPerTick - consumed) { consumed = Limits.WorkPerTick; return null; }
        consumed += source.Bodies.Count;
        return new(source.World, source.Tick, source.Coverage, source.IsComplete,
            source.Bodies.Select(static b => b with { Velocity = Vector3.Zero }).ToArray(), MaximumBodies, bodyPosesAreFixed: true);
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
            !NavigationMotionArbitrator.TryBuildSweptOccupancy(from, to, profile, out var pose, out var half))
        { _legacyComplete = false; return; }
        _legacyEnvelopes.Add(new(new(NavigationObstacleSource.TrafficReservation, (ulong)entity.Index + 1, entity.Generation),
            NavigationObstacleKind.Reservation, pose, half, Vector3.Zero));
    }

    internal void ObserveUnavailable(EntityId entity, NavigationBlockReason reason)
    {
        if (_actors.TryGetValue(entity, out var actor))
            _actors[entity] = actor with { LastTick = _tick, LastAction = NavigationTrafficAction.Hold, LastReason = reason };
    }
}
