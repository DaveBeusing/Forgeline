using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Combat;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum VehicleDockStatus : byte
{
    Reserved, Approaching, Aligning, Docked, Servicing, Exiting, Busy, Blocked
}

public enum VehicleDockBlockReason : byte
{
    None, MissingGeometry, InvalidOwner, Obstructed, OutsideTerrain, PoseLost, ExitBlocked, SourceEmpty, TargetFull
}

/// <summary>Metres in building-local coordinates; the lane follows the loading facade.</summary>
public readonly record struct BuildingLoadingDock(Vector3 ServicePosition, Vector3 Forward, float ApproachLength)
{
    public BuildingOrientation OrientationToward(Vector3 direction)
    {
        Vector3 outward = new(-Forward.Z, 0, Forward.X);
        if (Vector3.Dot(outward, ServicePosition) < 0) outward = -outward;
        float best = float.NegativeInfinity;
        var selected = BuildingOrientation.North;
        for (int index = 0; index < 4; index++)
        {
            var orientation = (BuildingOrientation)index;
            float score = Vector3.Dot(Vector3.Transform(outward, BuildingFootprint.GetRotation(orientation)), direction);
            if (score > best + 0.00001f) { best = score; selected = orientation; }
        }
        return selected;
    }

    public bool IsValid => Finite(ServicePosition) && Finite(Forward) &&
        MathF.Abs(Forward.Y) < 0.001f && MathF.Abs(Forward.LengthSquared() - 1) < 0.001f &&
        float.IsFinite(ApproachLength) && ApproachLength >= 12;

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct VehicleDockPose(Vector3 Entry, Vector3 Service, Vector3 Exit, float Heading,
    float HalfLength, float HalfWidth, Vector3? ApproachStart = null)
{
    public AxisAlignedBounds CorridorBounds
    {
        get
        {
            Vector3 forward = new(MathF.Sin(Heading), 0, MathF.Cos(Heading));
            Vector3 side = new(forward.Z, 0, -forward.X);
            Vector3 start = Entry - forward * (HalfLength + 1);
            Vector3 end = Exit + forward * (HalfLength * 3 + 3);
            Vector3 lateral = side * (MathF.Sqrt(HalfLength * HalfLength + HalfWidth * HalfWidth) + 1);
            Vector3 minimum = Vector3.Min(Vector3.Min(start - lateral, start + lateral), Vector3.Min(end - lateral, end + lateral));
            Vector3 maximum = Vector3.Max(Vector3.Max(start - lateral, start + lateral), Vector3.Max(end - lateral, end + lateral));
            if (ApproachStart is Vector3 approach)
            {
                float radius = MathF.Sqrt(HalfLength * HalfLength + HalfWidth * HalfWidth) + 1;
                minimum = Vector3.Min(minimum, approach - new Vector3(radius, 0, radius));
                maximum = Vector3.Max(maximum, approach + new Vector3(radius, 0, radius));
            }
            return new(minimum - new Vector3(0, 12, 0), maximum + new Vector3(0, 12, 0));
        }
    }
    public static bool TryCreate(in BuildingLoadingDock definition, in WorldTransform building,
        in WorldTransform truck, out VehicleDockPose pose)
    {
        pose = default;
        if (!definition.IsValid || !float.IsFinite(truck.Scale.X) || !float.IsFinite(truck.Scale.Y) || !float.IsFinite(truck.Scale.Z) ||
            truck.Scale.X <= 0 || truck.Scale.Y <= 0 || truck.Scale.Z <= 0 || !float.IsFinite(building.Rotation.LengthSquared()) ||
            MathF.Abs(building.Rotation.LengthSquared() - 1) > 0.001f) return false;
        Vector3 direction = Vector3.Transform(definition.Forward, building.Rotation);
        if (MathF.Abs(direction.Y) > 0.001f) return false;
        Vector3 service = building.Position + Vector3.Transform(definition.ServicePosition, building.Rotation);
        float length = definition.ApproachLength + truck.Scale.Z * 0.5f;
        pose = new(service - direction * length, service, service + direction * length,
            MathF.Atan2(direction.X, direction.Z), truck.Scale.Z * 0.5f, truck.Scale.X * 0.5f);
        return float.IsFinite(service.X) && float.IsFinite(service.Y) && float.IsFinite(service.Z);
    }

    public bool Contains(Vector3 position, float radius = 0)
    {
        Vector3 direction = new(MathF.Sin(Heading), 0, MathF.Cos(Heading));
        Vector3 side = new(direction.Z, 0, -direction.X);
        Vector3 relative = position - Service;
        float along = Vector3.Dot(relative, direction);
        bool lane = along >= Vector3.Dot(Entry - Service, direction) - HalfLength - radius &&
            along <= Vector3.Dot(Exit - Service, direction) + HalfLength + radius &&
            MathF.Abs(Vector3.Dot(relative, side)) <= HalfWidth + 1 + radius;
        float maneuverRadius = MathF.Sqrt(HalfLength * HalfLength + HalfWidth * HalfWidth) + 1 + radius;
        Vector3 start = ApproachStart ?? Entry;
        Vector3 delta = Entry - start;
        delta.Y = 0;
        Vector3 offset = position - start;
        offset.Y = 0;
        float fraction = delta.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(offset, delta) / delta.LengthSquared(), 0, 1) : 0;
        return lane || HorizontalDistanceSquared(position, start + delta * fraction) <= maneuverRadius * maneuverRadius;
    }

    public bool IsSettled(in WorldTransform transform, in GroundMovementState movement)
    {
        if (!float.IsFinite(movement.HeadingRadians) || !float.IsFinite(Heading) ||
            !float.IsFinite(transform.Rotation.LengthSquared()) || MathF.Abs(transform.Rotation.LengthSquared() - 1) > 0.001f) return false;
        Vector3 bodyForward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
        return Vector3.DistanceSquared(transform.Position, Service) <= 0.25f * 0.25f &&
            MathF.Abs(GroundMovementState.NormalizeAngle(movement.HeadingRadians - Heading)) <= MathF.PI / 60 &&
            MathF.Abs(GroundMovementState.NormalizeAngle(MathF.Atan2(bodyForward.X, bodyForward.Z) - Heading)) <= MathF.PI / 60 &&
            movement.Velocity.LengthSquared() <= 0.05f * 0.05f;
    }

    public bool IntersectsLane(in AxisAlignedBounds obstacle)
    {
        Vector3 direction = new(MathF.Sin(Heading), 0, MathF.Cos(Heading));
        Vector3 side = new(direction.Z, 0, -direction.X);
        Vector3 offset = obstacle.Center - Service;
        Vector3 half = (obstacle.Maximum - obstacle.Minimum) * 0.5f;
        float along = Vector3.Dot(offset, direction);
        float alongHalf = MathF.Abs(direction.X) * half.X + MathF.Abs(direction.Z) * half.Z;
        float acrossHalf = MathF.Abs(side.X) * half.X + MathF.Abs(side.Z) * half.Z;
        bool lane = along + alongHalf >= Vector3.Dot(Entry - Service, direction) - HalfLength - 1 &&
            along - alongHalf <= Vector3.Dot(Exit - Service, direction) + HalfLength * 3 + 3 &&
            MathF.Abs(Vector3.Dot(offset, side)) <= HalfWidth + 1 + acrossHalf;
        float nearestX = Math.Clamp(Entry.X, obstacle.Minimum.X, obstacle.Maximum.X);
        float nearestZ = Math.Clamp(Entry.Z, obstacle.Minimum.Z, obstacle.Maximum.Z);
        float radius = MathF.Sqrt(HalfLength * HalfLength + HalfWidth * HalfWidth) + 1;
        bool approachBlocked = false;
        if (ApproachStart is Vector3 start)
        {
            float first = 0, last = 1;
            Vector3 delta = Entry - start;
            for (int axis = 0; axis < 2 && first <= last; axis++)
            {
                float origin = axis == 0 ? start.X : start.Z;
                float directionComponent = axis == 0 ? delta.X : delta.Z;
                float minimum = axis == 0 ? obstacle.Minimum.X : obstacle.Minimum.Z;
                float maximum = axis == 0 ? obstacle.Maximum.X : obstacle.Maximum.Z;
                if (MathF.Abs(directionComponent) < 0.0001f)
                {
                    if (origin < minimum || origin > maximum) first = 2;
                }
                else
                {
                    float enter = (minimum - origin) / directionComponent;
                    float leave = (maximum - origin) / directionComponent;
                    first = MathF.Max(first, MathF.Min(enter, leave));
                    last = MathF.Min(last, MathF.Max(enter, leave));
                }
            }
            float startX = Math.Clamp(start.X, obstacle.Minimum.X, obstacle.Maximum.X);
            float startZ = Math.Clamp(start.Z, obstacle.Minimum.Z, obstacle.Maximum.Z);
            float nearest = (start.X - startX) * (start.X - startX) + (start.Z - startZ) * (start.Z - startZ);
            nearest = MathF.Min(nearest, (Entry.X - nearestX) * (Entry.X - nearestX) + (Entry.Z - nearestZ) * (Entry.Z - nearestZ));
            float lengthSquared = delta.X * delta.X + delta.Z * delta.Z;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner & 1) == 0 ? obstacle.Minimum.X : obstacle.Maximum.X;
                float z = (corner & 2) == 0 ? obstacle.Minimum.Z : obstacle.Maximum.Z;
                float amount = lengthSquared > 0 ? Math.Clamp(((x - start.X) * delta.X + (z - start.Z) * delta.Z) / lengthSquared, 0, 1) : 0;
                float dx = x - start.X - delta.X * amount;
                float dz = z - start.Z - delta.Z * amount;
                nearest = MathF.Min(nearest, dx * dx + dz * dz);
            }
            approachBlocked = first <= last || nearest <= radius * radius;
        }
        return lane || approachBlocked || (Entry.X - nearestX) * (Entry.X - nearestX) + (Entry.Z - nearestZ) * (Entry.Z - nearestZ) <= radius * radius;
    }

    public bool HasDrivableTerrain(ITerrainQuery terrain)
    {
        var bounds = CorridorBounds;
        if (bounds.Minimum.X < terrain.WorldBounds.Minimum.X || bounds.Minimum.Z < terrain.WorldBounds.Minimum.Z ||
            bounds.Maximum.X > terrain.WorldBounds.Maximum.X || bounds.Maximum.Z > terrain.WorldBounds.Maximum.Z) return false;
        Vector3 side = new(MathF.Cos(Heading), 0, -MathF.Sin(Heading));
        for (int i = 0; i <= 16; i++)
        {
            Vector3 center = i <= 8 ? Vector3.Lerp(ApproachStart ?? Entry, Entry, i / 8.0f) :
                Vector3.Lerp(Entry, Exit, (i - 8) / 8.0f);
            for (int across = -1; across <= 1; across++)
            {
                Vector3 sample = center + side * (across * (HalfWidth + 1));
                if (!terrain.TrySampleHeight(sample.X, sample.Z, out float height) || !float.IsFinite(height) ||
                    !terrain.TrySampleNormal(sample.X, sample.Z, out Vector3 normal) ||
                    !float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z) ||
                    normal.Y < MathF.Cos(24 * MathF.PI / 180)) return false;
            }
        }
        return true;
    }

    internal static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        Vector3 difference = first - second;
        return difference.X * difference.X + difference.Z * difference.Z;
    }
}

/// <summary>Stored on the building; ownership includes entity generation and visit generation.</summary>
public readonly record struct VehicleDockSlot(EntityId Truck, ulong Generation, VehicleDockPose Pose, PlayerId Owner);

/// <summary>Authoritative visit state is included by the existing ECS snapshot contract.</summary>
public readonly record struct VehicleDockVisit(EntityId Building, ulong Generation, VehicleDockStatus Status,
    VehicleDockBlockReason Reason, VehicleDockPose Pose, ulong LastObservedTick = 0, int SettledTicks = 0,
    ulong ServiceStartedTick = 0, ulong LastTransferTick = 0, double ServiceProgress = 0, bool ExitTurning = false,
    bool LaneEntered = false, float DepartureDistance = 0);

public readonly record struct VehicleDockGeneration(ulong Value);
public readonly record struct SupplyLoadingOrder(EntityId Source, SimulationTick AcceptedAtTick,
    VehicleDockBlockReason WaitReason = VehicleDockBlockReason.None, ulong RetryAtTick = 0);

/// <summary>One exclusive lane owner shared by cargo and supply service. No queue or timeout release.</summary>
public sealed partial class VehicleDockingService
{
    private readonly ITerrainQuery? _terrain;
    private readonly SpatialGridIndex? _spatial;
    private readonly HierarchicalNavigationSystem? _navigation;
    private readonly SpatialQueryBuffer _neighbors = new(32);
    private readonly List<EntityId> _occupiedBuildings = new();

    public VehicleDockingService(ITerrainQuery? terrain = null, SpatialGridIndex? spatial = null, HierarchicalNavigationSystem? navigation = null)
    {
        _terrain = terrain;
        _spatial = spatial;
        _navigation = navigation;
    }

    public bool TryReserve(SimulationContext context, EntityId truck, EntityId building, PlayerId owner,
        out VehicleDockVisit visit)
    {
        var entities = context.Entities;
        visit = default;
        if (entities.TryGetComponent(truck, out VehicleDockVisit currentVisit) && currentVisit.Generation > 0 && currentVisit.Building != building)
            return false;
        if (entities.TryGetComponent(building, out CompletedBuilding ownership) && ownership.Owner != owner)
            return Reject(entities, truck, building, VehicleDockBlockReason.InvalidOwner, out visit);
        if (!OwnsTruck(entities, truck, owner) || !Operational(entities, building))
            return Reject(entities, truck, building, VehicleDockBlockReason.InvalidOwner, out visit);

        if (!entities.IsAlive(truck) || !entities.IsAlive(building) ||
            !entities.TryGetComponent(truck, out WorldTransform truckTransform) ||
            !entities.TryGetComponent(building, out WorldTransform buildingTransform) ||
            !entities.TryGetComponent(building, out BuildingLoadingDock definition) ||
            !entities.TryGetComponent(building, out CompletedBuilding completed) ||
            completed.Owner != owner || !VehicleDockPose.TryCreate(definition, buildingTransform, truckTransform, out var pose))
            return Reject(entities, truck, building, VehicleDockBlockReason.MissingGeometry, out visit);

        if (entities.TryGetComponent(truck, out GroundMovement vehicleMovement))
        {
            Vector3 entry = pose.Entry, service = pose.Service, exit = pose.Exit;
            if (_terrain is not null)
            {
                if (!_terrain.TrySampleHeight(entry.X, entry.Z, out float entryY) ||
                    !_terrain.TrySampleHeight(service.X, service.Z, out float serviceY) ||
                    !_terrain.TrySampleHeight(exit.X, exit.Z, out float exitY))
                    return Reject(entities, truck, building, VehicleDockBlockReason.OutsideTerrain, out visit);
                entry.Y = entryY; service.Y = serviceY; exit.Y = exitY;
            }
            entry.Y += vehicleMovement.HeightOffset; service.Y += vehicleMovement.HeightOffset; exit.Y += vehicleMovement.HeightOffset;
            pose = pose with { Entry = entry, Service = service, Exit = exit };
        }

        if (entities.TryGetComponent(building, out VehicleDockSlot occupied))
        {
            if (occupied.Truck == truck && entities.TryGetComponent(truck, out visit) &&
                visit.Building == building && visit.Generation == occupied.Generation) return true;
            bool active = entities.TryGetComponent(occupied.Truck, out VehicleDockVisit ownerVisit) &&
                ownerVisit.Building == building && ownerVisit.Generation == occupied.Generation;
            if (!active && !OwnerInLane(entities, occupied) && LaneClear(entities, building, occupied.Truck, occupied.Pose))
                entities.RemoveComponent<VehicleDockSlot>(building);
            else
            {
                visit = new(building, 0, VehicleDockStatus.Busy, VehicleDockBlockReason.Obstructed,
                    pose with { ApproachStart = currentVisit.Generation == 0 && currentVisit.Building == building ? currentVisit.Pose.ApproachStart : null });
                Put(entities, truck, visit);
                return false;
            }
        }

        if (entities.TryGetComponent(truck, out VehicleQueueEntry queued) && queued.Building == building)
        {
            if (!_queues.IsHead(entities, truck, building, context.Tick)) return false;
            if (!queued.ArrivedAtService)
                pose = pose with { ApproachStart = entities.GetComponent<VehicleQueueLane>(building).Front };
        }
        else if (_queues.Collect(entities, building).Count > 0)
            return Reject(entities, truck, building, VehicleDockBlockReason.Obstructed, out visit);
        if (_navigation is not null && entities.TryGetComponent(truck, out NavigationAgent agent))
        {
            var grid = _navigation.World.Grid;
            Vector3 direction = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
            if (pose.ApproachStart is Vector3 queueStart)
            {
                if (!grid.TryWorldToCell(queueStart, out var queueCell) || !grid.IsTraversable(queueCell, agent.Capabilities))
                    return Reject(entities, truck, building, VehicleDockBlockReason.Obstructed, out visit);
            }
            else if (!grid.TryWorldToCell(pose.Entry, out var entryCell) || !grid.IsTraversable(entryCell, agent.Capabilities))
            {
                Vector3 outward = new(-direction.Z, 0, direction.X);
                if (Vector3.Dot(outward, pose.Service - buildingTransform.Position) < 0) outward = -outward;
                bool found = false;
                for (int step = 1; step <= 6 && !found; step++)
                {
                    for (int candidate = 0; candidate < 3 && !found; candidate++)
                    {
                        Vector3 offset = candidate switch { 0 => -direction, 1 => outward, _ => outward - direction };
                        Vector3 start = pose.Entry + offset * (step * grid.Settings.CellSizeMeters);
                        var extended = pose with { ApproachStart = start };
                        if (grid.TryWorldToCell(start, out var cell) && grid.IsTraversable(cell, agent.Capabilities) &&
                            LaneClear(entities, building, truck, extended) &&
                            (!entities.TryGetComponent(building, out SpatialPresence ownPresence) ||
                             !extended.IntersectsLane(ownPresence.CreateEntry(building, buildingTransform).Bounds)))
                        {
                            pose = extended;
                            found = true;
                        }
                    }
                }
                if (!found) return Reject(entities, truck, building, VehicleDockBlockReason.Obstructed, out visit);
            }
        }
        if (_terrain is not null && !pose.HasDrivableTerrain(_terrain))
            return Reject(entities, truck, building, VehicleDockBlockReason.OutsideTerrain, out visit);
        if (entities.TryGetComponent(building, out SpatialPresence buildingPresence) &&
            pose.IntersectsLane(buildingPresence.CreateEntry(building, buildingTransform).Bounds))
            return Reject(entities, truck, building, VehicleDockBlockReason.MissingGeometry, out visit);
        if (!ReservedLanesClear(entities, building, pose) || !LaneClear(entities, building, truck, pose))
        {
            visit = new(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed, pose);
            Put(entities, truck, visit);
            return false;
        }
        if (!TryChooseDeparture(entities, building, truck, pose, buildingTransform.Position, out float departure))
            return Reject(entities, truck, building, VehicleDockBlockReason.ExitBlocked, out visit);
        ulong generation = entities.TryGetComponent(truck, out VehicleDockGeneration sequence) ? checked(sequence.Value + 1) : 1;
        Put(entities, truck, new VehicleDockGeneration(generation));
        visit = new(building, generation, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, pose,
            DepartureDistance: departure);
        Put(entities, building, new VehicleDockSlot(truck, generation, pose, owner));
        if (entities.HasComponent<VehicleQueueMotion>(truck)) entities.RemoveComponent<VehicleQueueMotion>(truck);
        Put(entities, truck, visit);
        return true;
    }

    private static bool Reject(EntityRegistry entities, EntityId truck, EntityId building, VehicleDockBlockReason reason,
        out VehicleDockVisit visit)
    {
        visit = new(building, 0, VehicleDockStatus.Blocked, reason, default);
        if (entities.IsAlive(truck) && (!entities.TryGetComponent(truck, out VehicleDockVisit existing) || existing.Generation == 0))
            Put(entities, truck, visit);
        return false;
    }

    public bool ObservePose(SimulationContext context, EntityId truck, out VehicleDockVisit visit)
    {
        var entities = context.Entities;
        if (!entities.TryGetComponent(truck, out visit) ||
            !entities.TryGetComponent(visit.Building, out VehicleDockSlot slot) || slot.Truck != truck ||
            slot.Generation != visit.Generation || !entities.TryGetComponent(visit.Building, out CompletedBuilding completed) || completed.Owner != slot.Owner ||
            !OwnsTruck(entities, truck, slot.Owner) || !Operational(entities, visit.Building) ||
            !entities.TryGetComponent(truck, out WorldTransform transform) ||
            !entities.TryGetComponent(truck, out GroundMovementState movement)) return false;
        bool settled = visit.Pose.IsSettled(transform, movement) && LaneClear(entities, visit.Building, truck, visit.Pose);
        if (visit.LastObservedTick == context.Tick.Value && settled) return visit.SettledTicks >= 2;
        int ticks = settled ? (visit.LastObservedTick + 1 == context.Tick.Value ? visit.SettledTicks + 1 : 1) : 0;
        visit = visit with { LastObservedTick = context.Tick.Value, SettledTicks = ticks,
            Status = ticks >= 2 ? VehicleDockStatus.Docked : VehicleDockStatus.Aligning,
            Reason = !settled && visit.SettledTicks > 0 ? VehicleDockBlockReason.PoseLost : VehicleDockBlockReason.None,
            ServiceStartedTick = settled ? visit.ServiceStartedTick : 0,
            ServiceProgress = settled ? visit.ServiceProgress : 0 };
        Put(entities, truck, visit);
        return ticks >= 2;
    }

    public bool Approach(SimulationContext context, EntityId truck, EntityId building, PlayerId owner)
    {
        if ((!context.Entities.TryGetComponent(truck, out VehicleDockVisit current) || current.Generation == 0) &&
            StageBeforeReservation(context, truck, building, owner)) return false;
        if (!TryReserve(context, truck, building, owner, out var visit))
        {
            StopDockMotion(context.Entities, truck);
            return false;
        }
        ClearFormationMembership(context.Entities, truck);
        if (!LaneClear(context.Entities, building, truck, visit.Pose))
        {
            Put(context.Entities, truck, visit with { Status = VehicleDockStatus.Blocked, Reason = VehicleDockBlockReason.Obstructed });
            StopDockMotion(context.Entities, truck);
            return false;
        }
        bool approached = visit.Status is VehicleDockStatus.Approaching or VehicleDockStatus.Aligning
            or VehicleDockStatus.Docked or VehicleDockStatus.Servicing;
        if (ObservePose(context, truck, out visit)) return true;
        var entities = context.Entities;
        var position = entities.GetComponent<WorldTransform>(truck).Position;
        if (!entities.TryGetComponent(truck, out GroundMovement movement) ||
            !entities.TryGetComponent(truck, out GroundMovementState motion)) return false;
        bool atEntry = VehicleDockPose.HorizontalDistanceSquared(position, visit.Pose.Entry) <= 0.25f * 0.25f &&
            motion.Velocity.LengthSquared() <= 0.05f * 0.05f &&
            MathF.Abs(GroundMovementState.NormalizeAngle(motion.HeadingRadians - visit.Pose.Heading)) <= MathF.PI / 60;
        bool entered = atEntry || visit.SettledTicks > 0 || visit.LaneEntered && visit.Pose.Contains(position, 0);
        Vector3 approachStart = visit.Pose.ApproachStart ?? visit.Pose.Entry;
        float handoffRadius = movement.StopRadius + 0.5f;
        bool local = entered || (approached && visit.Pose.Contains(position, 0)) ||
            VehicleDockPose.HorizontalDistanceSquared(position, approachStart) <= handoffRadius * handoffRadius &&
            motion.Velocity.LengthSquared() <= 0.05f * 0.05f;
        Vector3 destination = entered ? visit.Pose.Service : local ? visit.Pose.Entry : approachStart;
        if (local && entities.TryGetComponent(truck, out MovementOrder coarseOrder) &&
            (!coarseOrder.TargetHeadingRadians.HasValue || coarseOrder.WorldTarget != destination))
            StopDockMotion(entities, truck);
        // The entry is immutable for this visit. Navigation owns the coarse route;
        // only the exclusive final lane uses precise local movement.
        if (!entities.HasComponent<MovementOrder>(truck) && !entities.HasComponent<NavigationPendingPath>(truck) &&
            !entities.HasComponent<NavigationRouteState>(truck))
        {
            Put(entities, truck, new MovementOrder(owner, destination, context.Tick, context.Tick,
                local ? MovementOrderKind.LocalApproach : MovementOrderKind.Strategic,
                local ? visit.Pose.Heading : null));
        }
        Put(entities, truck, visit with { Status = local ? VehicleDockStatus.Approaching : VehicleDockStatus.Reserved, LaneEntered = entered });
        return false;
    }

    internal static bool BlocksStagingStep(EntityRegistry entities, EntityId truck, in WorldTransform current, in WorldTransform next)
    {
        if (BlocksQueueStep(entities, truck, current, next)) return true;
        if (!entities.TryGetComponent(truck, out VehicleDockVisit visit) || visit.Generation != 0) return false;
        var first = VehicleBounds(current);
        var second = VehicleBounds(next);
        var swept = new AxisAlignedBounds(Vector3.Min(first.Minimum, second.Minimum), Vector3.Max(first.Maximum, second.Maximum));
        // This is an admission gate. An occupant already overlapping the reserved lane
        // must retain ordinary movement so it can evacuate; the owner still waits for LaneClear.
        foreach (EntityId building in entities.Query<VehicleDockSlot>())
        {
            var slot = entities.GetComponent<VehicleDockSlot>(building);
            if (slot.Truck == truck || slot.Pose.IntersectsLane(first)) continue;
            if (slot.Pose.IntersectsLane(swept)) return true;
        }
        return false;
    }

    public bool Exit(SimulationContext context, EntityId truck)
    {
        var entities = context.Entities;
        if (!entities.TryGetComponent(truck, out VehicleDockVisit visit)) return true;
        if (TryRelease(entities, truck, visit.Building, visit.Generation)) return true;
        if (!entities.TryGetComponent(visit.Building, out VehicleDockSlot slot) || slot.Generation != visit.Generation || slot.Truck != truck)
        {
            entities.RemoveComponent<VehicleDockVisit>(truck);
            return true;
        }
        if (!LaneClear(entities, visit.Building, truck, visit.Pose))
        {
            StopDockMotion(entities, truck);
            Put(entities, truck, visit with { Status = VehicleDockStatus.Exiting, Reason = VehicleDockBlockReason.ExitBlocked });
            return false;
        }
        Vector3 forward = new(MathF.Sin(visit.Pose.Heading), 0, MathF.Cos(visit.Pose.Heading));
        Vector3 position = entities.GetComponent<WorldTransform>(truck).Position;
        float clearance = visit.DepartureDistance > 0 ? visit.DepartureDistance : visit.Pose.HalfLength + 2;
        if (!visit.ExitTurning && Vector3.Dot(position - visit.Pose.Exit, forward) >= clearance - 0.2f &&
            entities.TryGetComponent(truck, out GroundMovementState departureMotion) && departureMotion.Velocity.LengthSquared() <= 0.05f * 0.05f)
        {
            visit = visit with { ExitTurning = true };
            StopDockMotion(entities, truck);
        }
        Vector3 outward = new(-forward.Z, 0, forward.X);
        if (entities.TryGetComponent(visit.Building, out WorldTransform buildingTransform) &&
            Vector3.Dot(outward, visit.Pose.Service - buildingTransform.Position) < 0) outward = -outward;
        Vector3 target = visit.ExitTurning
            ? visit.Pose.Exit + forward * clearance + outward * (visit.Pose.HalfWidth +
                MathF.Sqrt(visit.Pose.HalfLength * visit.Pose.HalfLength + visit.Pose.HalfWidth * visit.Pose.HalfWidth) + 8)
            : visit.Pose.Exit + forward * clearance;
        if (_terrain is not null && _terrain.TrySampleHeight(target.X, target.Z, out float height) &&
            entities.TryGetComponent(truck, out GroundMovement ground)) target.Y = height + ground.HeightOffset;
        if (!entities.HasComponent<MovementOrder>(truck))
            Put(entities, truck, new MovementOrder(slot.Owner, target, context.Tick, context.Tick, MovementOrderKind.LocalApproach,
                visit.ExitTurning ? MathF.Atan2(outward.X, outward.Z) : visit.Pose.Heading));
        bool stopped = entities.TryGetComponent(truck, out GroundMovementState motion) &&
            motion.Status is GroundMovementStatus.OutOfFuel or GroundMovementStatus.Stuck;
        Put(entities, truck, visit with { Status = VehicleDockStatus.Exiting,
            Reason = stopped ? VehicleDockBlockReason.ExitBlocked : VehicleDockBlockReason.None });
        return false;
    }

    // Preserve exclusive ownership while a failed cargo order's parked vehicle
    // clears the actual lane. Never release the bay simply because its order ended.
    internal static bool BeginFailureExit(EntityRegistry entities, EntityId truck)
    {
        if (!entities.TryGetComponent(truck, out VehicleDockVisit visit) ||
            visit.Generation == 0 ||
            !entities.TryGetComponent(visit.Building, out VehicleDockSlot slot) ||
            slot.Truck != truck || slot.Generation != visit.Generation ||
            !entities.TryGetComponent(truck, out WorldTransform transform) ||
            !visit.Pose.Contains(transform.Position))
            return false;

        Put(entities, truck, visit with
        {
            Status = VehicleDockStatus.Exiting,
            Reason = VehicleDockBlockReason.None,
            ServiceStartedTick = 0,
            ServiceProgress = 0
        });
        return true;
    }

    public static void Suspend(EntityRegistry entities, EntityId truck)
    {
        RemoveQueueAssignment(entities, truck);
        if (!entities.HasComponent<VehicleDockVisit>(truck)) return;
        StopDockMotion(entities, truck);
        entities.RemoveComponent<VehicleDockVisit>(truck);
        CargoDeliveryFuelPolicy.ClearTravelState(entities, truck);
    }

    public static void Cancel(EntityRegistry entities, EntityId truck)
    {
        bool docking = entities.HasComponent<VehicleDockVisit>(truck) || entities.HasComponent<SupplyLoadingOrder>(truck) ||
            entities.HasComponent<CargoTransportOrder>(truck) || entities.HasComponent<VehicleQueueEntry>(truck);
        if (!docking) return;
        RemoveQueueAssignment(entities, truck);
        StopDockMotion(entities, truck);
        if (entities.HasComponent<VehicleDockVisit>(truck)) entities.RemoveComponent<VehicleDockVisit>(truck);
        if (entities.HasComponent<SupplyLoadingOrder>(truck)) entities.RemoveComponent<SupplyLoadingOrder>(truck);
        if (entities.HasComponent<CargoTransportOrder>(truck)) entities.RemoveComponent<CargoTransportOrder>(truck);
        if (entities.TryGetComponent(truck, out CargoTransportRuntimeState cargoState))
            Put(entities, truck, cargoState with
            {
                Lifecycle = CargoTransportLifecycleState.Idle, WaitReason = CargoTransportWaitReason.None,
                FailureReason = CargoTransportFailureReason.None, AnchorNode = ForgeLine.Logistics.LogisticsNodeId.None,
                LoadedQuantity = 0
            });
        CargoDeliveryFuelPolicy.ClearTravelState(entities, truck);
        // Slot remains on the building until the physical lane is clear. The
        // existing distribution owner observes cancellation and releases stock.
    }

    internal static bool HasExclusiveLoadingVisit(EntityRegistry entities, EntityId truck) =>
        entities.HasComponent<SupplyLoadingOrder>(truck) &&
        entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Generation > 0 &&
        entities.TryGetComponent(visit.Building, out VehicleDockSlot slot) &&
        slot.Truck == truck && slot.Generation == visit.Generation;

    public void Cleanup(EntityRegistry entities)
    {
        _queues.Cleanup(entities);
        _occupiedBuildings.Clear();
        foreach (EntityId truck in entities.Query<VehicleQueueHolding>())
        {
            var holding = entities.GetComponent<VehicleQueueHolding>(truck);
            if (!Operational(entities, holding.Building) ||
                !entities.TryGetComponent(holding.Building, out CompletedBuilding source) || !OwnsTruck(entities, truck, source.Owner) ||
                !entities.TryGetComponent(truck, out VehicleDockVisit waiting) || waiting.Building != holding.Building)
                _occupiedBuildings.Add(truck);
        }
        foreach (EntityId truck in _occupiedBuildings) RemoveQueueAssignment(entities, truck);
        _occupiedBuildings.Clear();
        foreach (EntityId building in entities.Query<VehicleDockSlot>(QueryIterationOrder.StableByEntityIndex))
            _occupiedBuildings.Add(building);
        foreach (EntityId building in _occupiedBuildings)
        {
            var slot = entities.GetComponent<VehicleDockSlot>(building);
            bool permitted = OwnsTruck(entities, slot.Truck, slot.Owner) && Operational(entities, building) &&
                entities.TryGetComponent(building, out CompletedBuilding completed) && completed.Owner == slot.Owner &&
                (!entities.TryGetComponent(slot.Truck, out ControllableEntity control) || control.Owner == slot.Owner);
            bool active = permitted && entities.TryGetComponent(slot.Truck, out VehicleDockVisit visit) &&
                visit.Building == building && visit.Generation == slot.Generation;
            if (active) continue;
            if (!permitted && entities.IsAlive(slot.Truck)) Cancel(entities, slot.Truck);
            if (!OwnerInLane(entities, slot) && LaneClear(entities, building, slot.Truck, slot.Pose))
                entities.RemoveComponent<VehicleDockSlot>(building);
        }
    }

    internal static void EndQueueSession(EntityRegistry entities)
    {
        var participants = new HashSet<EntityId>();
        foreach (EntityId truck in entities.Query<VehicleQueueEntry>()) participants.Add(truck);
        foreach (EntityId truck in entities.Query<VehicleQueueHolding>()) participants.Add(truck);
        foreach (EntityId truck in entities.Query<VehicleDockVisit>()) participants.Add(truck);
        foreach (EntityId truck in participants.OrderBy(id => id.Index).ThenBy(id => id.Generation)) Cancel(entities, truck);
        var buildings = new List<EntityId>();
        foreach (EntityId building in entities.Query<VehicleDockSlot>()) buildings.Add(building);
        foreach (EntityId building in buildings) entities.RemoveComponent<VehicleDockSlot>(building);
        buildings.Clear();
        foreach (EntityId building in entities.Query<VehicleQueueLane>()) buildings.Add(building);
        foreach (EntityId building in buildings) entities.RemoveComponent<VehicleQueueLane>(building);
    }

    private static void ClearFormationMembership(EntityRegistry entities, EntityId truck)
    {
        if (entities.HasComponent<MovementGroupMember>(truck)) entities.RemoveComponent<MovementGroupMember>(truck);
        if (entities.HasComponent<FormationMovementConstraint>(truck)) entities.RemoveComponent<FormationMovementConstraint>(truck);
    }

    private static void StopDockMotion(EntityRegistry entities, EntityId truck)
    {
        if (!entities.IsAlive(truck)) return;
        if (entities.HasComponent<MovementOrder>(truck)) entities.RemoveComponent<MovementOrder>(truck);
        if (entities.HasComponent<NavigationPendingPath>(truck)) entities.RemoveComponent<NavigationPendingPath>(truck);
        if (entities.HasComponent<NavigationRouteState>(truck)) entities.RemoveComponent<NavigationRouteState>(truck);
    }

    public bool CanTransfer(SimulationContext context, EntityId truck, ulong generation, double quantity, double unitsPerSecond = 100)
    {
        if (!double.IsFinite(quantity) || quantity <= 0 || !double.IsFinite(unitsPerSecond) || unitsPerSecond <= 0 ||
            !ObservePose(context, truck, out var visit) || visit.Generation != generation ||
            visit.LastTransferTick == context.Tick.Value) return false;
        if (visit.ServiceStartedTick == 0)
        {
            Put(context.Entities, truck, visit with { ServiceStartedTick = context.Tick.Value, Status = VehicleDockStatus.Servicing, ServiceProgress = 0 });
            return false;
        }
        double progress = (context.Tick.Value - visit.ServiceStartedTick) * context.TickDuration.TotalSeconds * unitsPerSecond / quantity;
        Put(context.Entities, truck, visit with { Status = VehicleDockStatus.Servicing, ServiceProgress = Math.Clamp(progress, 0, 1) });
        return progress >= 1;
    }

    public static void RecordTransfer(SimulationContext context, EntityId truck, ulong generation)
    {
        if (context.Entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Generation == generation)
            Put(context.Entities, truck, visit with { LastTransferTick = context.Tick.Value, ServiceStartedTick = 0, ServiceProgress = 0 });
    }

    public bool TryRelease(EntityRegistry entities, EntityId truck, EntityId building, ulong generation)
    {
        if (entities.TryGetComponent(truck, out VehicleDockVisit departure) && departure.Generation == generation &&
            departure.Building == building && departure.Status == VehicleDockStatus.Exiting)
        {
            if (!entities.TryGetComponent(truck, out WorldTransform departed)) return false;
            Vector3 side = new(MathF.Cos(departure.Pose.Heading), 0, -MathF.Sin(departure.Pose.Heading));
            float minimum = departure.Pose.HalfWidth +
                MathF.Sqrt(departure.Pose.HalfLength * departure.Pose.HalfLength + departure.Pose.HalfWidth * departure.Pose.HalfWidth) + 2;
            if ((departure.LaneEntered || departure.ExitTurning || departure.Pose.Contains(departed.Position, departure.Pose.HalfLength)) &&
                (!departure.ExitTurning || MathF.Abs(Vector3.Dot(departed.Position - departure.Pose.Service, side)) <= minimum)) return false;
        }
        if (!entities.TryGetComponent(building, out VehicleDockSlot slot) || slot.Truck != truck || slot.Generation != generation || OwnerInLane(entities, slot) ||
            !LaneClear(entities, building, slot.Truck, slot.Pose)) return false;
        entities.RemoveComponent<VehicleDockSlot>(building);
        RemoveQueueAssignment(entities, truck);
        if (entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Building == building && visit.Generation == generation)
        {
            if (visit.Status == VehicleDockStatus.Exiting) StopDockMotion(entities, truck);
            entities.RemoveComponent<VehicleDockVisit>(truck);
        }
        return true;
    }

    private bool TryChooseDeparture(EntityRegistry entities, EntityId building, EntityId truck, in VehicleDockPose pose,
        Vector3 buildingPosition, out float distance)
    {
        Vector3 forward = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
        Vector3 outward = new(-forward.Z, 0, forward.X);
        if (Vector3.Dot(outward, pose.Service - buildingPosition) < 0) outward = -outward;
        float radius = pose.HalfLength + pose.HalfWidth + 1;
        float offset = pose.HalfWidth + MathF.Sqrt(pose.HalfLength * pose.HalfLength + pose.HalfWidth * pose.HalfWidth) + 3;
        for (int candidate = 0; candidate < 8; candidate++)
        {
            distance = pose.HalfLength + 2 + candidate * (2 * pose.HalfLength + 2 * pose.HalfWidth + 4);
            Vector3 center = pose.Exit + forward * distance + outward * offset;
            var area = new AxisAlignedBounds(center - new Vector3(radius, 12, radius), center + new Vector3(radius, 12, radius));
            if (_terrain is not null && (area.Minimum.X < _terrain.WorldBounds.Minimum.X || area.Minimum.Z < _terrain.WorldBounds.Minimum.Z ||
                area.Maximum.X > _terrain.WorldBounds.Maximum.X || area.Maximum.Z > _terrain.WorldBounds.Maximum.Z)) continue;
            bool clear = true;
            if (_spatial is not null)
            {
                _spatial.QueryAabb(area, _neighbors);
                foreach (EntityId neighbor in _neighbors.Results)
                    if (neighbor != building && neighbor != truck && entities.IsAlive(neighbor)) { clear = false; break; }
            }
            if (clear) return true;
        }
        distance = 0;
        return false;
    }

    private static bool ReservedLanesClear(EntityRegistry entities, EntityId building, in VehicleDockPose pose)
    {
        var bounds = pose.CorridorBounds;
        foreach (EntityId neighbor in entities.Query<VehicleDockSlot>())
        {
            if (neighbor == building) continue;
            var occupied = entities.GetComponent<VehicleDockSlot>(neighbor).Pose;
            if (pose.IntersectsLane(occupied.CorridorBounds) && occupied.IntersectsLane(bounds)) return false;
        }
        return true;
    }

    private bool LaneClear(EntityRegistry entities, EntityId building, EntityId truck, in VehicleDockPose pose)
    {
        var bounds = pose.CorridorBounds;
        Vector3 minimum = bounds.Minimum;
        Vector3 maximum = bounds.Maximum;
        if (_terrain is not null && !pose.HasDrivableTerrain(_terrain)) return false;
        if (_spatial is null) return true;
        _spatial.QueryAabb(new AxisAlignedBounds(minimum, maximum), _neighbors);
        foreach (EntityId neighbor in _neighbors.Results)
        {
            if (neighbor == building || neighbor == truck || !entities.IsAlive(neighbor)) continue;
            if (entities.TryGetComponent(neighbor, out WorldTransform transform) &&
                entities.TryGetComponent(neighbor, out SpatialPresence presence))
            {
                var obstacle = presence.CreateEntry(neighbor, transform).Bounds;
                if (entities.HasComponent<CargoTransport>(neighbor) || entities.HasComponent<SupplyTruck>(neighbor))
                {
                    Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation);
                    Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
                    Vector3 half = new(MathF.Abs(right.X) * transform.Scale.X * 0.5f + MathF.Abs(forward.X) * transform.Scale.Z * 0.5f,
                        transform.Scale.Y * 0.5f, MathF.Abs(right.Z) * transform.Scale.X * 0.5f + MathF.Abs(forward.Z) * transform.Scale.Z * 0.5f);
                    obstacle = new(transform.Position - half, transform.Position + half);
                }
                if (pose.IntersectsLane(obstacle)) return false;
            }
        }
        return true;
    }

    private static bool OwnerInLane(EntityRegistry entities, in VehicleDockSlot slot)
    {
        if (!entities.TryGetComponent(slot.Truck, out WorldTransform transform)) return false;
        return slot.Pose.IntersectsLane(VehicleBounds(transform));
    }

    private static AxisAlignedBounds VehicleBounds(in WorldTransform transform)
    {
        Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
        Vector3 half = new(MathF.Abs(right.X) * transform.Scale.X * 0.5f + MathF.Abs(forward.X) * transform.Scale.Z * 0.5f,
            transform.Scale.Y * 0.5f, MathF.Abs(right.Z) * transform.Scale.X * 0.5f + MathF.Abs(forward.Z) * transform.Scale.Z * 0.5f);
        return new AxisAlignedBounds(transform.Position - half, transform.Position + half);
    }

    internal static bool Operational(EntityRegistry entities, EntityId entity) =>
        entities.IsAlive(entity) && (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted);

    internal static bool OwnsTruck(EntityRegistry entities, EntityId truck, PlayerId owner) =>
        Operational(entities, truck) &&
        (!entities.TryGetComponent(truck, out ControllableEntity control) || control.Owner == owner && control.IsControllable) &&
        (!entities.TryGetComponent(truck, out CargoTransport cargo) || cargo.Owner == owner) &&
        (!entities.TryGetComponent(truck, out SupplyTruck supply) || supply.Owner == owner);

    internal static void Put<T>(EntityRegistry entities, EntityId entity, T value) where T : struct
    {
        if (entities.HasComponent<T>(entity)) entities.SetComponent(entity, value);
        else entities.AddComponent(entity, value);
    }
}
