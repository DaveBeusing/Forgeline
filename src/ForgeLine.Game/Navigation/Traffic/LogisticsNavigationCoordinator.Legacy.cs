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
    private readonly Dictionary<EntityId, NavigationPhysicalWait> _physicalWaits = new();
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
        _physicalWaits.Remove(entity);
        if (result.FrameTick == context.Tick.Value && result.Revision == collision.World.Revision &&
            result.Status is NavigationGeometryStatus.Blocked or NavigationGeometryStatus.Limited &&
            result.Reason is NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle &&
            result.Blocker is { Source: NavigationObstacleSource.Entity } blocker &&
            blocker.Value <= uint.MaxValue && blocker.Generation <= uint.MaxValue &&
            (_physicalWaits.Count < MaximumVehicles || _physicalWaits.ContainsKey(entity)))
        {
            if (_legacyWork == Limits.WorkPerTick) return false;
            _legacyWork++;
            var other = new EntityId((uint)blocker.Value, (uint)blocker.Generation);
            if (NavigationMotionCapture.TryCaptureVehicle(context.Entities, other, _movementOptions, out var observedBlocker))
                _physicalWaits[entity] = new(context.Tick, result.Revision, vehicle, observedBlocker);
        }
        return result.Status == NavigationGeometryStatus.Clear && result.AllowedFraction == 1 &&
            result.FrameTick == context.Tick.Value && result.Revision == collision.World.Revision;
    }

    internal bool TryProposeLegacyQueueReverse(SimulationContext context, EntityId entity,
        in WorldTransform start, float maximumSpeed, bool forwardBlocked, out WorldTransform end, out float heading)
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
        Vector3 approachDelta = queue.Target - start.Position; approachDelta.Y = 0;
        float localRange = vehicle.Profile.HalfExtents.Z * 2;
        // Prefer the checked forward approach for a remote waiting pose. Reverse
        // remains available when forward motion is physically blocked, and an
        // already backing owner retains that motion until its own endpoint.
        if (!forwardBlocked && Vector3.Dot(vehicle.Movement.Velocity, forward) >= -.001f &&
            approachDelta.LengthSquared() > localRange * localRange) return false;
        EnsureWorld();
        if (!NavigationReverseMotion.TryStep(vehicle, queue.Target, MathF.Min(1, maximumSpeed),
            (float)context.TickDuration.TotalSeconds, _snapshot!, out var proposal, out float speed, out heading, arrivalTolerance: .05f) ||
            speed <= 0 || speed > MathF.Min(1, maximumSpeed) + .00001f)
            return false;
        end = start with { Position = proposal.Position, Rotation = proposal.Rotation };
        return true;
    }

    // An ordinary unit or unentered queue owner can be trapped beside a long body before
    // its centre-based steering reacts. Verify a complete slow rear arc, retaining
    // the existing command and checking every physical body before proposing a tick.
    internal bool TryProposeLegacyLogisticsEscape(SimulationContext context, EntityId entity,
        in WorldTransform start, float maximumSpeed, bool blocked, out WorldTransform end, out float heading)
    {
        end = start; heading = 0;
        var entities = context.Entities;
        if (!_protectedComplete || _protectedLogistics.Count == 0 || maximumSpeed <= 0 ||
            !NavigationMotionCapture.TryCaptureVehicle(entities, entity, _movementOptions, out var vehicle) ||
            vehicle.DirectOrder is not { } order || order.Issuer != vehicle.Owner ||
            vehicle.Movement.Velocity.LengthSquared() > 1.0001f) return false;
        bool logistics = IsLogisticsVehicle(entities, entity);
        if (logistics)
        {
            if (vehicle.QueueEntry is not { Generation: > 0, Position: > 0, ArrivedAtService: false } queue ||
                queue.Owner != vehicle.Owner || order.WorldTarget != queue.Target || vehicle.DockVisit is { LaneEntered: true } ||
                vehicle.DockVisit is { Generation: > 0 } visit &&
                    (visit.Building != queue.Building || vehicle.BuildingSlot is not { } slot || slot.Truck != entity ||
                     slot.Generation != visit.Generation || slot.Owner != vehicle.Owner)) return false;
        }
        else if (entities.HasComponent<VehicleQueueEntry>(entity) || entities.HasComponent<VehicleDockVisit>(entity) ||
            order.Kind != MovementOrderKind.Strategic || order.TargetHeadingRadians.HasValue) return false;
        Vector3 direction = order.WorldTarget - start.Position; direction.Y = 0;
        if (direction.LengthSquared() < .0001f) return false;
        direction = Vector3.Normalize(direction);
        Vector3 initialForward = Vector3.Transform(Vector3.UnitZ, start.Rotation);
        bool backing = Vector3.Dot(vehicle.Movement.Velocity, initialForward) < -.001f;
        if (!blocked && !backing) return false;
        NavigationCollisionBody? obstruction = null;
        float nearest = float.PositiveInfinity;
        foreach (var body in _protectedLogistics)
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref _legacyWork)) return false;
            if (body.Identity == NavigationReadAdapter.GetObstacleIdentity(entity)) continue;
            if (!backing && (!_legacyVetoes.TryGetValue(entity, out var veto) || veto.Tick != context.Tick ||
                veto.Reason is not (NavigationBlockReason.MovingVehicle or NavigationBlockReason.StaticObstacle) || veto.Blocker != body.Identity)) continue;
            Vector3 delta = body.Pose.Position - start.Position; delta.Y = 0;
            float range = vehicle.Profile.HalfExtents.Length() + body.HalfExtents.Length() + vehicle.Profile.ClearanceMeters + 2;
            float along = Vector3.Dot(delta, direction);
            if (along <= 0 || delta.LengthSquared() >= 4 * range * range ||
                (delta - direction * along).LengthSquared() >= (range - 1) * (range - 1) || delta.LengthSquared() >= nearest) continue;
            nearest = delta.LengthSquared(); obstruction = body;
        }
        if (obstruction is not { } blocker) return false;
        EnsureWorld();
        var physical = CapturePhysical(context, _snapshot!, Limits.WorkPerTick - _legacyWork);
        _legacyWork += physical.Work;
        if (!physical.Collision.IsComplete) return false;
        var collision = CaptureNeighborhood(physical, vehicle, context.Tick, ref _legacyWork);
        if (collision is null || !collision.IsComplete) return false;
        collision = FreezePoses(collision, ref _legacyWork);
        if (collision is null) return false;
        float clearance = vehicle.Profile.HalfExtents.Length() + blocker.HalfExtents.Length() + vehicle.Profile.ClearanceMeters + 2;
        Vector3 side = new(direction.Z, 0, -direction.X);
        float straightRange = clearance - 1.5f;
        // The narrower steering rate is within authored yaw limits and leaves room
        // for a rear corner to clear before the reverse arc broadens.
        var simulatedVehicle = vehicle with { Profile = vehicle.Profile with { MaximumYawRate = MathF.Min(1, vehicle.Profile.MaximumYawRate) } };
        ReadOnlySpan<int> sides = [1, -1];
        foreach (int sign in sides)
        {
            Vector3 target = blocker.Pose.Position - direction * clearance + side * (sign * clearance);
            if (!_snapshot!.TrySampleHeight(target.X, target.Z, out float height) ||
                !_snapshot.TrySampleHeight(start.Position.X, start.Position.Z, out float ground)) continue;
            target.Y = height + start.Position.Y - ground;
            var simulated = simulatedVehicle;
            bool reached = false;
            for (int step = 0; step < 128; step++)
            {
                if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref _legacyWork)) return false;
                Vector3 stepTarget = Vector2.Distance(new(simulated.Pose.Position.X, simulated.Pose.Position.Z), new(blocker.Pose.Position.X, blocker.Pose.Position.Z)) < straightRange
                    ? simulated.Pose.Position - initialForward * .5f : target;
                if (!NavigationReverseMotion.TryStep(simulated, stepTarget, MathF.Min(1, maximumSpeed), .125f,
                    _snapshot, out var pose, out _, out float yaw)) break;
                int remaining = Limits.WorkPerTick - _legacyWork;
                if (remaining == 0) return false;
                var result = _geometry!.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(entity),
                    simulated.Pose, pose, vehicle.Profile, .125f), remaining);
                if (result.WorkConsumed <= 0 || result.WorkConsumed > remaining) { _legacyWork = Limits.WorkPerTick; return false; }
                _legacyWork += result.WorkConsumed;
                if (result.Status != NavigationGeometryStatus.Clear || result.AllowedFraction != 1 ||
                    result.FrameTick != context.Tick.Value || result.Revision != collision.World.Revision) break;
                simulated = simulated with { Pose = pose, Movement = simulated.Movement with
                    { Velocity = (pose.Position - simulated.Pose.Position) / .125f, HeadingRadians = yaw } };
                if (Vector3.DistanceSquared(pose.Position, target) <= .04f) { reached = true; break; }
            }
            Vector3 firstTarget = Vector2.Distance(new(start.Position.X, start.Position.Z), new(blocker.Pose.Position.X, blocker.Pose.Position.Z)) < straightRange
                ? start.Position - initialForward * .5f : target;
            if (!reached || !NavigationReverseMotion.TryStep(simulatedVehicle, firstTarget, MathF.Min(1, maximumSpeed),
                (float)context.TickDuration.TotalSeconds, _snapshot, out var next, out _, out heading)) continue;
            int allowance = Limits.WorkPerTick - _legacyWork;
            if (allowance == 0) return false;
            var actual = _geometry!.Sweep(collision, new(NavigationReadAdapter.GetObstacleIdentity(entity),
                vehicle.Pose, next, vehicle.Profile, (float)context.TickDuration.TotalSeconds), allowance);
            if (actual.WorkConsumed <= 0 || actual.WorkConsumed > allowance) { _legacyWork = Limits.WorkPerTick; return false; }
            _legacyWork += actual.WorkConsumed;
            if (actual.Status != NavigationGeometryStatus.Clear || actual.AllowedFraction != 1 ||
                actual.FrameTick != context.Tick.Value || actual.Revision != collision.World.Revision) continue;
            end = start with { Position = next.Position, Rotation = next.Rotation };
            return true;
        }
        return false;
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
            // The narrow phase rounds both oriented envelopes. Its local padding
            // can project sqrt(3) times onto a world axis. The coarse gate must
            // cover both bodies' rounding, padded rotation and interpolation error,
            // or a skipped sweep can admit a pose
            // that the next full sweep considers initially obstructed.
            Vector3 coordinates = Vector3.Max(Vector3.Max(Vector3.Abs(start.Position), Vector3.Abs(end.Position)), Vector3.Abs(body.Pose.Position));
            double rounding = (Math.Max(Math.Max(coordinates.X, coordinates.Y), coordinates.Z) +
                profile.HalfExtents.Length() + body.HalfExtents.Length() + 1d) * .000020d;
            if (!double.IsFinite(rounding) || rounding > float.MaxValue) return false;
            projected += new Vector3((float)rounding);
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
        ObserveLegacyVeto(entity, context.Tick, result);
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
