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
    public bool IsValid => Finite(ServicePosition) && Finite(Forward) &&
        MathF.Abs(Forward.Y) < 0.001f && MathF.Abs(Forward.LengthSquared() - 1) < 0.001f &&
        float.IsFinite(ApproachLength) && ApproachLength >= 12;

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct VehicleDockPose(Vector3 Entry, Vector3 Service, Vector3 Exit, float Heading,
    float HalfLength, float HalfWidth)
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
        return lane || HorizontalDistanceSquared(position, Entry) <= maneuverRadius * maneuverRadius;
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
        return lane || (Entry.X - nearestX) * (Entry.X - nearestX) + (Entry.Z - nearestZ) * (Entry.Z - nearestZ) <= radius * radius;
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
    ulong ServiceStartedTick = 0, ulong LastTransferTick = 0, double ServiceProgress = 0, bool ExitTurning = false);

public readonly record struct VehicleDockGeneration(ulong Value);
public readonly record struct SupplyLoadingOrder(EntityId Source, SimulationTick AcceptedAtTick);

/// <summary>One exclusive lane owner shared by cargo and supply service. No queue or timeout release.</summary>
public sealed class VehicleDockingService
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
                    !_terrain.TrySampleHeight(exit.X, exit.Z, out float exitY)) return false;
                entry.Y = entryY; service.Y = serviceY; exit.Y = exitY;
            }
            entry.Y += vehicleMovement.HeightOffset; service.Y += vehicleMovement.HeightOffset; exit.Y += vehicleMovement.HeightOffset;
            pose = pose with { Entry = entry, Service = service, Exit = exit };
        }

        if (_navigation is not null && entities.TryGetComponent(truck, out NavigationAgent agent))
        {
            var grid = _navigation.World.Grid;
            Vector3 direction = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
            Vector3 entry = pose.Entry;
            for (int step = 0; step < 6; step++)
            {
                if (grid.TryWorldToCell(entry, out var cell) && grid.IsTraversable(cell, agent.Capabilities)) break;
                entry -= direction * grid.Settings.CellSizeMeters;
            }
            pose = pose with { Entry = entry };
        }
        if (entities.TryGetComponent(building, out VehicleDockSlot slot))
        {
            if (slot.Truck == truck && entities.TryGetComponent(truck, out visit) &&
                visit.Building == building && visit.Generation == slot.Generation) return true;
            // Even a dead/cancelled owner does not free an occupied lane.
            bool active = entities.TryGetComponent(slot.Truck, out VehicleDockVisit ownerVisit) &&
                ownerVisit.Building == building && ownerVisit.Generation == slot.Generation;
            if (!active && !OwnerInLane(entities, slot) && LaneClear(entities, building, slot.Truck, slot.Pose)) entities.RemoveComponent<VehicleDockSlot>(building);
            else
            {
                visit = new(building, 0, VehicleDockStatus.Busy, VehicleDockBlockReason.Obstructed, pose);
                Put(entities, truck, visit);
                return false;
            }
        }
        if (!LaneClear(entities, building, truck, pose))
        {
            visit = new(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed, pose);
            Put(entities, truck, visit);
            return false;
        }
        ulong generation = entities.TryGetComponent(truck, out VehicleDockGeneration sequence) ? checked(sequence.Value + 1) : 1;
        Put(entities, truck, new VehicleDockGeneration(generation));
        visit = new(building, generation, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, pose);
        Put(entities, building, new VehicleDockSlot(truck, generation, pose, owner));
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
        if (!TryReserve(context, truck, building, owner, out var visit))
        {
            StopDockMotion(context.Entities, truck);
            return false;
        }
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
        if (!entities.TryGetComponent(truck, out GroundMovement movement)) return false;
        float maneuverRadius = MathF.Sqrt(visit.Pose.HalfLength * visit.Pose.HalfLength + visit.Pose.HalfWidth * visit.Pose.HalfWidth) + 1;
        bool atEntry = VehicleDockPose.HorizontalDistanceSquared(position, visit.Pose.Entry) <= maneuverRadius * maneuverRadius;
        bool local = atEntry || visit.SettledTicks > 0 ||
            (approached && visit.Pose.Contains(position, 0));
        Vector3 destination = local ? visit.Pose.Service : visit.Pose.Entry;
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
        Put(entities, truck, visit with { Status = local ? VehicleDockStatus.Approaching : VehicleDockStatus.Reserved });
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
        float clearance = visit.Pose.HalfLength + 2;
        if (!visit.ExitTurning && Vector3.Dot(position - visit.Pose.Exit, forward) >= clearance - 0.2f)
        {
            visit = visit with { ExitTurning = true };
            StopDockMotion(entities, truck);
        }
        Vector3 outward = new(-forward.Z, 0, forward.X);
        if (entities.TryGetComponent(visit.Building, out WorldTransform buildingTransform) &&
            Vector3.Dot(outward, visit.Pose.Service - buildingTransform.Position) < 0) outward = -outward;
        // Clear the through lane before ending the job. Distinct departure rays
        // prevent consecutive deliveries from parking nose-to-tail on its exit.
        float departureLength = clearance + 16 * (1 + truck.Index % 4);
        Vector3 target = visit.ExitTurning
            ? visit.Pose.Exit + forward * departureLength + outward * 14
            : visit.Pose.Exit + forward * clearance;
        if (_terrain is not null && _terrain.TrySampleHeight(target.X, target.Z, out float height) &&
            entities.TryGetComponent(truck, out GroundMovement ground)) target.Y = height + ground.HeightOffset;
        if (!entities.HasComponent<MovementOrder>(truck))
            Put(entities, truck, new MovementOrder(slot.Owner, target, context.Tick, context.Tick, MovementOrderKind.LocalApproach,
                visit.ExitTurning ? MathF.Atan2(outward.X, outward.Z) : visit.Pose.Heading));
        Put(entities, truck, visit with { Status = VehicleDockStatus.Exiting, Reason = VehicleDockBlockReason.None });
        return false;
    }

    public static void Cancel(EntityRegistry entities, EntityId truck)
    {
        bool docking = entities.HasComponent<VehicleDockVisit>(truck) || entities.HasComponent<SupplyLoadingOrder>(truck);
        if (!docking) return;
        StopDockMotion(entities, truck);
        if (entities.HasComponent<VehicleDockVisit>(truck)) entities.RemoveComponent<VehicleDockVisit>(truck);
        if (entities.HasComponent<SupplyLoadingOrder>(truck)) entities.RemoveComponent<SupplyLoadingOrder>(truck);
        if (entities.HasComponent<CargoTransportOrder>(truck)) entities.RemoveComponent<CargoTransportOrder>(truck);
        // Slot remains on the building until the physical lane is clear. The
        // existing distribution owner observes cancellation and releases stock.
    }

    public void Cleanup(EntityRegistry entities)
    {
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

    private static void StopDockMotion(EntityRegistry entities, EntityId truck)
    {
        if (!entities.IsAlive(truck)) return;
        if (entities.HasComponent<MovementOrder>(truck)) entities.RemoveComponent<MovementOrder>(truck);
        if (entities.HasComponent<NavigationPendingPath>(truck)) entities.RemoveComponent<NavigationPendingPath>(truck);
        if (entities.HasComponent<NavigationRouteState>(truck)) entities.RemoveComponent<NavigationRouteState>(truck);
    }

    public bool CanTransfer(SimulationContext context, EntityId truck, ulong generation, double quantity, double unitsPerSecond = 40)
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
        if (!entities.TryGetComponent(building, out VehicleDockSlot slot) || slot.Truck != truck || slot.Generation != generation || OwnerInLane(entities, slot) ||
            !LaneClear(entities, building, slot.Truck, slot.Pose)) return false;
        entities.RemoveComponent<VehicleDockSlot>(building);
        if (entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Building == building && visit.Generation == generation)
            entities.RemoveComponent<VehicleDockVisit>(truck);
        return true;
    }

    private bool LaneClear(EntityRegistry entities, EntityId building, EntityId truck, in VehicleDockPose pose)
    {
        var bounds = pose.CorridorBounds;
        Vector3 minimum = bounds.Minimum;
        Vector3 maximum = bounds.Maximum;
        if (_terrain is not null && (minimum.X < _terrain.WorldBounds.Minimum.X || minimum.Z < _terrain.WorldBounds.Minimum.Z ||
            maximum.X > _terrain.WorldBounds.Maximum.X || maximum.Z > _terrain.WorldBounds.Maximum.Z)) return false;
        if (_terrain is not null)
        {
            for (int i = 0; i <= 8; i++)
            {
                Vector3 sample = Vector3.Lerp(pose.Entry, pose.Exit, i / 8.0f);
                if (!_terrain.TrySampleHeight(sample.X, sample.Z, out float height) || !float.IsFinite(height) ||
                    !_terrain.TrySampleNormal(sample.X, sample.Z, out Vector3 normal) ||
                    !float.IsFinite(normal.Y) || normal.Y < MathF.Cos(24 * MathF.PI / 180)) return false;
            }
        }
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
        Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
        Vector3 half = new(MathF.Abs(right.X) * transform.Scale.X * 0.5f + MathF.Abs(forward.X) * transform.Scale.Z * 0.5f,
            transform.Scale.Y * 0.5f, MathF.Abs(right.Z) * transform.Scale.X * 0.5f + MathF.Abs(forward.Z) * transform.Scale.Z * 0.5f);
        return slot.Pose.IntersectsLane(new AxisAlignedBounds(transform.Position - half, transform.Position + half));
    }

    private static bool Operational(EntityRegistry entities, EntityId entity) =>
        entities.IsAlive(entity) && (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted);

    private static bool OwnsTruck(EntityRegistry entities, EntityId truck, PlayerId owner) =>
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
