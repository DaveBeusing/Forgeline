using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct RoadTrafficCorridor(Vector3 Start, Vector3 End, float HalfWidth);
public readonly record struct VehicleQueueMotion(Vector3 Front, Vector3 Tail, float Heading,
    EntityId Predecessor, float Gap, ulong Generation);
public readonly record struct VehicleQueueHolding(EntityId Building, Vector3 Target, VehicleQueueWaitReason Reason,
    SimulationTick ChangedAtTick);

public sealed partial class VehicleDockingService
{
    private readonly VehicleQueueCoordinator _queues = new();
    private readonly List<EntityId> _queueMembers = new();
    private readonly SpatialQueryBuffer _queueNeighbors = new(32);

    private bool StageBeforeReservation(SimulationContext context, EntityId truck, EntityId building, PlayerId owner)
    {
        var entities = context.Entities;
        if (!OwnsTruck(entities, truck, owner) || !Operational(entities, building) ||
            !entities.TryGetComponent(building, out CompletedBuilding completed) || completed.Owner != owner ||
            !entities.TryGetComponent(building, out BuildingLoadingDock definition) ||
            !entities.TryGetComponent(building, out WorldTransform buildingTransform) ||
            !entities.TryGetComponent(truck, out WorldTransform transform) ||
            !entities.TryGetComponent(truck, out GroundMovement movement) ||
            !VehicleDockPose.TryCreate(definition, buildingTransform, transform, out var pose)) return false;
        ClearFormationMembership(entities, truck);
        Vector3 entryPoint = pose.Entry, servicePoint = pose.Service, exitPoint = pose.Exit;
        if (_terrain is not null)
        {
            if (!_terrain.TrySampleHeight(entryPoint.X, entryPoint.Z, out float entryHeight) ||
                !_terrain.TrySampleHeight(servicePoint.X, servicePoint.Z, out float serviceHeight) ||
                !_terrain.TrySampleHeight(exitPoint.X, exitPoint.Z, out float exitHeight))
            {
                StopDockMotion(entities, truck);
                return true;
            }
            entryPoint.Y = entryHeight; servicePoint.Y = serviceHeight; exitPoint.Y = exitHeight;
        }
        entryPoint.Y += movement.HeightOffset; servicePoint.Y += movement.HeightOffset; exitPoint.Y += movement.HeightOffset;
        pose = pose with { Entry = entryPoint, Service = servicePoint, Exit = exitPoint };
        var service = entities.HasComponent<CargoFuelServiceState>(truck) ? VehicleQueueService.CargoRefueling :
            entities.HasComponent<SupplyLoadingOrder>(truck) ? VehicleQueueService.SupplyLoading :
            entities.TryGetComponent(truck, out CargoTransportRuntimeState cargo) &&
                cargo.Lifecycle is CargoTransportLifecycleState.ToDestination or CargoTransportLifecycleState.Unloading
                ? VehicleQueueService.CargoDelivery : VehicleQueueService.CargoPickup;
        if (entities.TryGetComponent(truck, out GroundMovementState parked) && pose.IsSettled(transform, parked) &&
            (!entities.HasComponent<VehicleQueueEntry>(truck) && _queues.Collect(entities, building).Count == 0 ||
             entities.TryGetComponent(truck, out VehicleQueueEntry atService) && atService.Building == building && atService.ArrivedAtService))
        {
            if (_queues.TryRegister(entities, truck, building, owner, context.Tick, service))
            {
                var arrival = entities.GetComponent<VehicleQueueEntry>(truck);
                Put(entities, truck, arrival with { Position = 1, Target = pose.Service, Heading = pose.Heading, ArrivedAtService = true });
                Put(entities, truck, new VehicleDockVisit(building, 0, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, pose));
                if (!entities.HasComponent<MovementOrder>(truck))
                    Put(entities, truck, new MovementOrder(owner, pose.Service, context.Tick, context.Tick,
                        MovementOrderKind.LocalApproach, pose.Heading));
                return !_queues.IsHead(entities, truck, building, context.Tick);
            }
        }
        if (!TryQueueLane(entities, truck, building, pose, transform, movement, out var lane))
        {
            StopDockMotion(entities, truck);
            Put(entities, truck, new VehicleDockVisit(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed, pose));
            return true;
        }
        if (entities.TryGetComponent(truck, out VehicleQueueEntry old) && (old.Building != building || old.Service != service))
        {
            RemoveQueueAssignment(entities, truck);
            StopDockMotion(entities, truck);
        }
        Vector3 forward = new(MathF.Sin(lane.Heading), 0, MathF.Cos(lane.Heading));
        Vector3 side = new(forward.Z, 0, -forward.X);
        Vector3 relative = transform.Position - lane.Front;
        float range = MathF.Max(64, lane.Capacity * (transform.Scale.Z + movement.SeparationRadius) + 64);
        bool localArrival = entities.TryGetComponent(truck, out VehicleQueueHolding holding) && holding.Building == building &&
            holding.Reason != VehicleQueueWaitReason.GeometryBlocked &&
            VehicleDockPose.HorizontalDistanceSquared(transform.Position, holding.Target) <= 2.25f ||
            VehicleDockPose.HorizontalDistanceSquared(transform.Position, lane.Front) <= 64 * 64 ||
            MathF.Abs(Vector3.Dot(relative, side)) <= 32 && Vector3.Dot(relative, forward) <= 32 &&
            Vector3.Dot(relative, forward) >= -range;
        bool newlyRegistered = false;
        if (!entities.HasComponent<VehicleQueueEntry>(truck))
        {
            var members = _queues.Collect(entities, building);
            float distance = 0, previousHalf = transform.Scale.Z * .5f;
            foreach (EntityId member in members)
            {
                float half = entities.GetComponent<WorldTransform>(member).Scale.Z * .5f;
                if (distance == 0) distance = previousHalf + half + movement.SeparationRadius;
                else distance += previousHalf + half + movement.SeparationRadius;
                previousHalf = half;
            }
            Vector3 proposed = lane.Front - forward * distance;
            EntityId replacement = _queues.SameTickReplacement(entities, truck, building, context.Tick, lane.Capacity);
            if (replacement.IsValid) proposed = entities.GetComponent<VehicleQueueEntry>(replacement).Target;
            bool place = (members.Count < lane.Capacity || replacement.IsValid) && QueuePlaceClear(entities, building, truck, proposed, transform, lane.Heading);
            if (!localArrival || !place || !_queues.TryRegister(entities, truck, building, owner, context.Tick, service, lane.Capacity))
            {
                HoldOutsideQueue(context, truck, building, owner, pose, lane, transform, movement,
                    place ? VehicleQueueWaitReason.None : VehicleQueueWaitReason.Full);
                return true;
            }
            newlyRegistered = true;
            if (entities.HasComponent<VehicleQueueHolding>(truck)) entities.RemoveComponent<VehicleQueueHolding>(truck);
        }
        RefreshQueueTargets(context, building, lane);
        var entry = entities.GetComponent<VehicleQueueEntry>(truck);
        if (entry.WaitReason == VehicleQueueWaitReason.GeometryBlocked)
        {
            if (newlyRegistered)
            {
                RemoveQueueAssignment(entities, truck);
                HoldOutsideQueue(context, truck, building, owner, pose, lane, transform, movement, VehicleQueueWaitReason.GeometryBlocked);
                return true;
            }
            StopDockMotion(entities, truck);
            Put(entities, truck, new VehicleDockVisit(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed,
                pose with { ApproachStart = entry.Target }));
            return true;
        }
        float admissionRadius = transform.Scale.Z + movement.SeparationRadius + movement.StopRadius;
        bool atAdmission = VehicleDockPose.HorizontalDistanceSquared(transform.Position, lane.Front) <= admissionRadius * admissionRadius;
        if (atAdmission && _queues.IsHead(entities, truck, building, context.Tick) &&
            !entities.HasComponent<VehicleDockSlot>(building)) return false;
        QueueMove(context, truck, owner, entry.Target, lane.Heading, lane);
        Put(entities, truck, new VehicleDockVisit(building, 0, VehicleDockStatus.Busy,
            VehicleDockBlockReason.Obstructed, pose with { ApproachStart = entry.Target }));
        return true;
    }

    private bool TryQueueLane(EntityRegistry entities, EntityId truck, EntityId building, in VehicleDockPose pose,
        in WorldTransform transform, in GroundMovement movement, out VehicleQueueLane lane)
    {
        if (entities.TryGetComponent(building, out lane)) return true;
        Vector3 forward = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
        Vector3 outward = new(-forward.Z, 0, forward.X);
        if (entities.TryGetComponent(building, out WorldTransform origin) &&
            Vector3.Dot(outward, pose.Service - origin.Position) < 0) outward = -outward;
        float margin = transform.Scale.Z + movement.SeparationRadius + movement.StopRadius + 2.5f;
        float stepLength = _navigation?.World.Grid.Settings.CellSizeMeters ?? margin;
        for (int step = 0; step <= 6; step++)
        {
            for (int candidate = 0; candidate < (step == 0 ? 1 : 3); candidate++)
            {
                Vector3 offset = candidate switch { 0 => -forward, 1 => outward, _ => outward - forward };
                Vector3 front = pose.Entry + offset * (step * stepLength) - forward * margin;
                if (!QueuePlaceClear(entities, building, truck, front, transform, pose.Heading)) continue;
                var extended = pose with { ApproachStart = front };
                if (_terrain is not null && !extended.HasDrivableTerrain(_terrain) ||
                    !StaticQueueConnectionClear(entities, building, truck, front, pose.Entry, transform, preserveRoad: false)) continue;
                if (_terrain is not null && _terrain.TrySampleHeight(front.X, front.Z, out float height))
                    front.Y = height + movement.HeightOffset;
                int capacity = 1;
                Vector3 previous = front;
                float spacing = transform.Scale.Z + movement.SeparationRadius;
                for (int index = 1; index < 20; index++)
                {
                    Vector3 waiting = front - forward * (index * spacing);
                    if (!QueuePlaceClear(entities, building, truck, waiting, transform, pose.Heading) ||
                        !StaticQueueConnectionClear(entities, building, truck, previous, waiting, transform)) break;
                    capacity++;
                    previous = waiting;
                }
                lane = new(front, pose.Heading, capacity);
                Put(entities, building, lane);
                return true;
            }
        }
        lane = default;
        return false;
    }

    private void RefreshQueueTargets(SimulationContext context, EntityId building, in VehicleQueueLane lane)
    {
        var entities = context.Entities;
        _queueMembers.Clear();
        _queueMembers.AddRange(_queues.Collect(entities, building));
        Vector3 forward = new(MathF.Sin(lane.Heading), 0, MathF.Cos(lane.Heading));
        Vector3 target = lane.Front;
        EntityId predecessor = EntityId.Invalid;
        float previousHalf = 0, previousGap = 0;
        for (int index = 0; index < _queueMembers.Count; index++)
        {
            EntityId member = _queueMembers[index];
            var entry = entities.GetComponent<VehicleQueueEntry>(member);
            var transform = entities.GetComponent<WorldTransform>(member);
            var movement = entities.GetComponent<GroundMovement>(member);
            float half = transform.Scale.Z * .5f;
            float gap = MathF.Max(previousGap, movement.SeparationRadius);
            Vector3 previous = target;
            if (index > 0) target -= forward * (previousHalf + half + gap);
            if (_terrain is not null && _terrain.TrySampleHeight(target.X, target.Z, out float height))
                target.Y = height + movement.HeightOffset;
            bool changed = entry.Position != index + 1 || entry.Target != target || entry.Predecessor != predecessor;
            bool valid = !changed && entry.WaitReason != VehicleQueueWaitReason.GeometryBlocked ||
                QueuePlaceClear(entities, building, member, target, transform, lane.Heading) &&
                StaticQueueConnectionClear(entities, building, member, previous, target, transform);
            VehicleQueueWaitReason reason = !valid ? VehicleQueueWaitReason.GeometryBlocked : index > 0 ? VehicleQueueWaitReason.Predecessor :
                entities.HasComponent<VehicleDockSlot>(building) ? VehicleQueueWaitReason.OccupiedExit : VehicleQueueWaitReason.None;
            if (entities.TryGetComponent(member, out GroundMovementState state) && state.Status == GroundMovementStatus.OutOfFuel)
                reason = VehicleQueueWaitReason.Unavailable;
            int serving = entities.TryGetComponent(building, out VehicleDockSlot serviceSlot) &&
                entities.HasComponent<VehicleQueueEntry>(serviceSlot.Truck) ? 1 : 0;
            var updated = entry with { Position = index + 1, WaitingCount = _queueMembers.Count - serving,
                Target = entry.ArrivedAtService && index == 0 ? entry.Target : valid ? target : entry.Target,
                Heading = lane.Heading, Predecessor = predecessor, WaitReason = reason };
            if (updated != entry) Put(entities, member, updated with { ChangedAtTick = context.Tick });
            if (!entry.ArrivedAtService && (!entities.TryGetComponent(building, out VehicleDockSlot occupied) || occupied.Truck != member))
                Put(entities, member, new VehicleQueueMotion(lane.Front, target, lane.Heading, predecessor, gap, entry.Generation));
            predecessor = member;
            previousHalf = half;
            previousGap = movement.SeparationRadius;
        }
    }

    private static void QueueMove(SimulationContext context, EntityId truck, PlayerId owner, Vector3 target, float heading,
        in VehicleQueueLane lane)
    {
        var entities = context.Entities;
        Vector3 position = entities.GetComponent<WorldTransform>(truck).Position;
        var forward = new Vector3(MathF.Sin(lane.Heading), 0, MathF.Cos(lane.Heading));
        var side = new Vector3(forward.Z, 0, -forward.X);
        var vehicle = entities.GetComponent<WorldTransform>(truck);
        var movement = entities.GetComponent<GroundMovement>(truck);
        float localRange = vehicle.Scale.Z * 2 + movement.SeparationRadius;
        bool local = VehicleDockPose.HorizontalDistanceSquared(position, target) <= 16 ||
            MathF.Abs(Vector3.Dot(position - target, side)) <= 4 &&
            VehicleDockPose.HorizontalDistanceSquared(position, target) <= localRange * localRange;
        if (TacticalCommandUtilities.TryGetMovementIntent(context, truck, out MovementOrder currentIntent) &&
            currentIntent.WorldTarget == target && currentIntent.TargetHeadingRadians.HasValue) local = true;
        if (TacticalCommandUtilities.TryGetMovementIntent(context, truck, out MovementOrder intent) &&
            intent.WorldTarget == target && intent.TargetHeadingRadians.HasValue == local) return;
        StopDockMotion(entities, truck);
        Put(entities, truck, new MovementOrder(owner, target, context.Tick, context.Tick,
            local ? MovementOrderKind.LocalApproach : MovementOrderKind.Strategic, local ? heading : null));
    }

    private void HoldOutsideQueue(SimulationContext context, EntityId truck, EntityId building, PlayerId owner,
        in VehicleDockPose pose, in VehicleQueueLane lane, in WorldTransform transform, in GroundMovement movement,
        VehicleQueueWaitReason reason)
    {
        var entities = context.Entities;
        Vector3 forward = new(MathF.Sin(lane.Heading), 0, MathF.Cos(lane.Heading));
        Vector3 side = new(forward.Z, 0, -forward.X);
        float gap = transform.Scale.Z + movement.SeparationRadius + 2;
        Vector3 target = default;
        bool found = entities.TryGetComponent(truck, out VehicleQueueHolding previous) && previous.Building == building &&
            QueuePlaceClear(entities, building, truck, previous.Target, transform, lane.Heading);
        if (found) target = previous.Target;
        int occupied = _queues.Collect(entities, building).Count;
        int initialSlot = reason == VehicleQueueWaitReason.None ? occupied + 1 : lane.Capacity + 2;
        for (int index = 0; index < 24 && !found; index++)
        {
            target = lane.Front - forward * (gap * (initialSlot + index / 6)) + side * (gap * (index % 6 - 2));
            if (MathF.Abs(Vector3.Dot(target - lane.Front, side)) <= movement.SeparationRadius * 2 + transform.Scale.X * .5f) continue;
            if (!QueuePlaceClear(entities, building, truck, target, transform, lane.Heading)) continue;
            found = true;
            foreach (EntityId other in entities.Query<VehicleDockVisit>())
                if (other != truck && entities.GetComponent<VehicleDockVisit>(other).Pose.ApproachStart is Vector3 waiting &&
                    VehicleDockPose.HorizontalDistanceSquared(target, waiting) < gap * gap) { found = false; break; }
        }
        if (found)
        {
            if (_terrain is not null && _terrain.TrySampleHeight(target.X, target.Z, out float height)) target.Y = height + movement.HeightOffset;
            QueueMove(context, truck, owner, target, lane.Heading, lane);
        }
        else StopDockMotion(entities, truck);
        Put(entities, truck, new VehicleDockVisit(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed,
            pose with { ApproachStart = found ? target : null }));
        var updated = new VehicleQueueHolding(building, found ? target : transform.Position,
            found ? reason : VehicleQueueWaitReason.GeometryBlocked, previous.ChangedAtTick);
        if (updated != previous) Put(entities, truck, updated with { ChangedAtTick = context.Tick });
    }

    private bool QueuePlaceClear(EntityRegistry entities, EntityId building, EntityId truck, Vector3 point,
        in WorldTransform vehicle, float heading)
    {
        var transform = vehicle with { Position = point, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading) };
        var bounds = VehicleBounds(transform);
        if (_terrain is not null && (bounds.Minimum.X < _terrain.WorldBounds.Minimum.X || bounds.Minimum.Z < _terrain.WorldBounds.Minimum.Z ||
            bounds.Maximum.X > _terrain.WorldBounds.Maximum.X || bounds.Maximum.Z > _terrain.WorldBounds.Maximum.Z ||
            !_terrain.TrySampleHeight(point.X, point.Z, out _))) return false;
        if (_terrain is not null)
        {
            float maximumSlope = entities.TryGetComponent(truck, out GroundMovement terrainMovement) ? terrainMovement.MaximumSlopeDegrees : 24;
            Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation) * vehicle.Scale.X * .5f;
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation) * vehicle.Scale.Z * .5f;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 sample = point + ((corner & 1) == 0 ? -right : right) + ((corner & 2) == 0 ? -forward : forward);
                if (!_terrain.TrySampleHeight(sample.X, sample.Z, out float height) || !float.IsFinite(height) ||
                    !_terrain.TrySampleNormal(sample.X, sample.Z, out Vector3 normal) ||
                    !float.IsFinite(normal.Y) || normal.Y < MathF.Cos(maximumSlope * MathF.PI / 180)) return false;
            }
        }
        if (_navigation is not null && entities.TryGetComponent(truck, out NavigationAgent agent) &&
            (!_navigation.World.Grid.TryWorldToCell(point, out var cell) || !_navigation.World.Grid.IsTraversable(cell, agent.Capabilities))) return false;
        if (!StaticQueueConnectionClear(entities, building, truck, point, point, vehicle)) return false;
        foreach (EntityId other in entities.Query<VehicleQueueHolding, WorldTransform>())
        {
            if (other == truck) continue;
            var holding = entities.GetComponent<VehicleQueueHolding>(other);
            if (holding.Reason == VehicleQueueWaitReason.GeometryBlocked) continue;
            var otherVehicle = entities.GetComponent<WorldTransform>(other);
            float gap = entities.TryGetComponent(other, out GroundMovement otherMovement) ? otherMovement.SeparationRadius : 0;
            if (entities.TryGetComponent(truck, out GroundMovement ownMovement)) gap = MathF.Max(gap, ownMovement.SeparationRadius);
            float spacing = (vehicle.Scale.Z + otherVehicle.Scale.Z) * .5f + gap;
            if (VehicleDockPose.HorizontalDistanceSquared(point, holding.Target) < spacing * spacing) return false;
        }
        foreach (EntityId other in entities.Query<VehicleQueueEntry, WorldTransform>())
        {
            var queued = entities.GetComponent<VehicleQueueEntry>(other);
            if (other == truck || queued.Building == building) continue;
            var otherVehicle = entities.GetComponent<WorldTransform>(other);
            float gap = entities.TryGetComponent(other, out GroundMovement otherMovement) ? otherMovement.SeparationRadius : 0;
            if (entities.TryGetComponent(truck, out GroundMovement ownMovement)) gap = MathF.Max(gap, ownMovement.SeparationRadius);
            float spacing = (vehicle.Scale.Z + otherVehicle.Scale.Z) * .5f + gap;
            if (VehicleDockPose.HorizontalDistanceSquared(point, queued.Target) < spacing * spacing) return false;
        }
        foreach (EntityId entity in entities.Query<VehicleDockSlot>())
            if (entities.GetComponent<VehicleDockSlot>(entity).Pose.IntersectsLane(bounds)) return false;
        return true;
    }

    private bool StaticQueueConnectionClear(EntityRegistry entities, EntityId building, EntityId truck,
        Vector3 start, Vector3 end, in WorldTransform vehicle, bool preserveRoad = true)
    {
        float radius = MathF.Sqrt(vehicle.Scale.X * vehicle.Scale.X + vehicle.Scale.Z * vehicle.Scale.Z) * .5f + 1;
        var area = new AxisAlignedBounds(Vector3.Min(start, end) - new Vector3(radius, 12, radius),
            Vector3.Max(start, end) + new Vector3(radius, 12, radius));
        if (_spatial is not null)
        {
            _spatial.QueryAabb(area, _queueNeighbors);
            foreach (EntityId neighbor in _queueNeighbors.Results)
                if (neighbor != truck && neighbor != building && entities.IsAlive(neighbor) &&
                    (!entities.HasComponent<GroundMovement>(neighbor) || !Operational(entities, neighbor))) return false;
        }
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(start, end) / 2));
        for (int step = 0; step <= steps; step++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)step / steps);
            if (_terrain is not null)
            {
                Vector3 travel = end - start; travel.Y = 0;
                Vector3 side = travel.LengthSquared() > .0001f
                    ? Vector3.Normalize(new Vector3(travel.Z, 0, -travel.X))
                    : Vector3.Transform(Vector3.UnitX, vehicle.Rotation);
                float slope = entities.TryGetComponent(truck, out GroundMovement movement) ? movement.MaximumSlopeDegrees : 24;
                for (int across = -1; across <= 1; across++)
                {
                    Vector3 sample = point + side * (across * radius);
                    if (!_terrain.TrySampleHeight(sample.X, sample.Z, out float height) || !float.IsFinite(height) ||
                        !_terrain.TrySampleNormal(sample.X, sample.Z, out Vector3 normal) || !float.IsFinite(normal.Y) ||
                        normal.Y < MathF.Cos(slope * MathF.PI / 180)) return false;
                }
            }
            foreach (EntityId road in entities.Query<RoadTrafficCorridor>())
            {
                if (!preserveRoad) break;
                var corridor = entities.GetComponent<RoadTrafficCorridor>(road);
                Vector3 delta = corridor.End - corridor.Start; delta.Y = 0;
                Vector3 offset = point - corridor.Start; offset.Y = 0;
                float amount = delta.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(offset, delta) / delta.LengthSquared(), 0, 1) : 0;
                if (VehicleDockPose.HorizontalDistanceSquared(point, corridor.Start + delta * amount) <=
                    (corridor.HalfWidth + radius) * (corridor.HalfWidth + radius)) return false;
            }
            foreach (EntityId facility in entities.Query<UnitProductionFacility, WorldTransform>())
            {
                var transform = entities.GetComponent<WorldTransform>(facility);
                var production = entities.GetComponent<UnitProductionFacility>(facility);
                Vector3 spawn = transform.Position + Vector3.Transform(production.SpawnOffset, transform.Rotation);
                float clearance = MathF.Max(transform.Scale.X, transform.Scale.Z) * .5f + radius;
                if (VehicleDockPose.HorizontalDistanceSquared(point, spawn) < clearance * clearance) return false;
            }
        }
        return true;
    }

    private static void RemoveQueueAssignment(EntityRegistry entities, EntityId truck)
    {
        if (entities.HasComponent<VehicleQueueEntry>(truck)) entities.RemoveComponent<VehicleQueueEntry>(truck);
        if (entities.HasComponent<VehicleQueueMotion>(truck)) entities.RemoveComponent<VehicleQueueMotion>(truck);
        if (entities.HasComponent<VehicleQueueHolding>(truck)) entities.RemoveComponent<VehicleQueueHolding>(truck);
    }

    internal static void RevokeUnconfirmedQueueAdmission(EntityRegistry entities, EntityId truck, EntityId building)
    {
        StopDockMotion(entities, truck);
        if (entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Building == building && visit.Generation == 0)
            entities.RemoveComponent<VehicleDockVisit>(truck);
    }

    internal static bool BlocksQueueStep(EntityRegistry entities, EntityId truck, in WorldTransform current, in WorldTransform next)
    {
        if (!entities.TryGetComponent(truck, out VehicleQueueMotion motion) ||
            !entities.TryGetComponent(truck, out VehicleQueueEntry entry) || entry.Generation != motion.Generation ||
            !motion.Predecessor.IsValid || !entities.TryGetComponent(motion.Predecessor, out WorldTransform predecessor)) return false;
        Vector3 forward = new(MathF.Sin(motion.Heading), 0, MathF.Cos(motion.Heading));
        Vector3 side = new(forward.Z, 0, -forward.X);
        if (MathF.Abs(Vector3.Dot(current.Position - motion.Front, side)) > current.Scale.X + 2) return false;
        // A predecessor still approaching from outside the lane has not occupied
        // its no-passing plane. Dock admission stays FIFO and physical bodies
        // remain vetoes; do not trap a follower at its own free waiting pose.
        if (MathF.Abs(Vector3.Dot(predecessor.Position - motion.Front, side)) > predecessor.Scale.X + 2) return false;
        // A new arrival can already be ahead of its FIFO predecessor. Do not
        // let the no-passing gate trap it there while it moves sideways out
        // of the lane to reach its assigned tail position. Inspect the route
        // intent: the first steering step can point inward while turning.
        // Physical collision checks and exclusive dock admission still apply.
        if (Vector3.Dot(predecessor.Position - current.Position, forward) < 0 &&
            entities.TryGetComponent(truck, out MovementOrder evacuation) &&
            MathF.Abs(Vector3.Dot(evacuation.WorldTarget - motion.Front, side)) >
                MathF.Abs(Vector3.Dot(current.Position - motion.Front, side))) return false;
        // A later arrival can already be ahead while the FIFO head is still
        // approaching. It may advance away from that head only as far as its
        // own assigned tail pose. This does not grant service or dock admission.
        if (entry.Position > 1 && Vector3.Dot(current.Position - predecessor.Position, forward) > 0 &&
            Vector3.Dot(next.Position - current.Position, forward) > 0 &&
            Vector3.Dot(next.Position - motion.Tail, forward) <= .001f) return false;
        var ownBounds = VehicleBounds(next);
        var priorBounds = VehicleBounds(predecessor);
        float ownHalf = MathF.Abs(forward.X) * ownBounds.Extents.X + MathF.Abs(forward.Z) * ownBounds.Extents.Z;
        float priorHalf = MathF.Abs(forward.X) * priorBounds.Extents.X + MathF.Abs(forward.Z) * priorBounds.Extents.Z;
        float clearance = Vector3.Dot(predecessor.Position - next.Position, forward);
        return clearance < ownHalf + priorHalf + motion.Gap &&
            Vector3.Dot(next.Position - current.Position, forward) > 0;
    }
}
