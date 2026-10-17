using System.Numerics;
using ForgeLine.Core;
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
    None, MissingGeometry, InvalidOwner, Obstructed, OutsideTerrain, PoseLost, ExitBlocked
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
    public static bool TryCreate(in BuildingLoadingDock definition, in WorldTransform building,
        in WorldTransform truck, out VehicleDockPose pose)
    {
        pose = default;
        if (!definition.IsValid || !float.IsFinite(truck.Scale.X) || !float.IsFinite(truck.Scale.Z) ||
            truck.Scale.X <= 0 || truck.Scale.Z <= 0 || !float.IsFinite(building.Rotation.LengthSquared()) ||
            MathF.Abs(building.Rotation.LengthSquared() - 1) > 0.001f) return false;
        Vector3 direction = Vector3.Transform(definition.Forward, building.Rotation);
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
        return MathF.Abs(Vector3.Dot(relative, direction)) <= Vector3.Distance(Entry, Exit) * 0.5f + HalfLength + radius &&
            MathF.Abs(Vector3.Dot(relative, side)) <= HalfWidth + 1 + radius;
    }

    public bool IsSettled(in WorldTransform transform, in GroundMovementState movement) =>
        HorizontalDistanceSquared(transform.Position, Service) <= 0.25f * 0.25f &&
        MathF.Abs(GroundMovementState.NormalizeAngle(movement.HeadingRadians - Heading)) <= MathF.PI / 60 &&
        movement.Velocity.LengthSquared() <= 0.05f * 0.05f;

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
    ulong ServiceStartedTick = 0, ulong LastTransferTick = 0);

public readonly record struct VehicleDockGeneration(ulong Value);

/// <summary>One exclusive lane owner shared by cargo and supply service. No queue or timeout release.</summary>
public sealed class VehicleDockingService
{
    private readonly ITerrainQuery? _terrain;
    private readonly SpatialGridIndex? _spatial;
    private readonly SpatialQueryBuffer _neighbors = new(32);

    public VehicleDockingService(ITerrainQuery? terrain = null, SpatialGridIndex? spatial = null)
    {
        _terrain = terrain;
        _spatial = spatial;
    }

    public bool TryReserve(SimulationContext context, EntityId truck, EntityId building, PlayerId owner,
        out VehicleDockVisit visit)
    {
        var entities = context.Entities;
        visit = default;
        if (!entities.IsAlive(truck) || !entities.IsAlive(building) ||
            !entities.TryGetComponent(truck, out WorldTransform truckTransform) ||
            !entities.TryGetComponent(building, out WorldTransform buildingTransform) ||
            !entities.TryGetComponent(building, out BuildingLoadingDock definition) ||
            !entities.TryGetComponent(building, out CompletedBuilding completed) ||
            completed.Owner != owner || !VehicleDockPose.TryCreate(definition, buildingTransform, truckTransform, out var pose))
            return false;

        if (entities.TryGetComponent(building, out VehicleDockSlot slot))
        {
            if (slot.Truck == truck && entities.TryGetComponent(truck, out visit) &&
                visit.Building == building && visit.Generation == slot.Generation) return true;
            // Even a dead/cancelled owner does not free an occupied lane.
            bool active = entities.TryGetComponent(slot.Truck, out VehicleDockVisit ownerVisit) &&
                ownerVisit.Building == building && ownerVisit.Generation == slot.Generation;
            if (!active && !OwnerInLane(entities, slot) && LaneClear(entities, building, EntityId.Invalid, slot.Pose)) entities.RemoveComponent<VehicleDockSlot>(building);
            else
            {
                visit = new(building, 0, VehicleDockStatus.Busy, VehicleDockBlockReason.Obstructed, pose);
                return false;
            }
        }
        if (!LaneClear(entities, building, truck, pose))
        {
            visit = new(building, 0, VehicleDockStatus.Blocked, VehicleDockBlockReason.Obstructed, pose);
            return false;
        }
        ulong generation = entities.TryGetComponent(truck, out VehicleDockGeneration sequence) ? checked(sequence.Value + 1) : 1;
        Put(entities, truck, new VehicleDockGeneration(generation));
        visit = new(building, generation, VehicleDockStatus.Reserved, VehicleDockBlockReason.None, pose);
        Put(entities, building, new VehicleDockSlot(truck, generation, pose, owner));
        Put(entities, truck, visit);
        return true;
    }

    public bool ObservePose(SimulationContext context, EntityId truck, out VehicleDockVisit visit)
    {
        var entities = context.Entities;
        if (!entities.TryGetComponent(truck, out visit) ||
            !entities.TryGetComponent(visit.Building, out VehicleDockSlot slot) || slot.Truck != truck ||
            slot.Generation != visit.Generation || !entities.TryGetComponent(visit.Building, out CompletedBuilding completed) || completed.Owner != slot.Owner ||
            (entities.TryGetComponent(truck, out ControllableEntity control) && control.Owner != slot.Owner) ||
            !entities.TryGetComponent(truck, out WorldTransform transform) ||
            !entities.TryGetComponent(truck, out GroundMovementState movement)) return false;
        bool settled = visit.Pose.IsSettled(transform, movement) && LaneClear(entities, visit.Building, truck, visit.Pose);
        if (visit.LastObservedTick == context.Tick.Value && settled) return visit.SettledTicks >= 2;
        int ticks = settled ? (visit.LastObservedTick + 1 == context.Tick.Value ? visit.SettledTicks + 1 : 1) : 0;
        visit = visit with { LastObservedTick = context.Tick.Value, SettledTicks = ticks,
            Status = ticks >= 2 ? VehicleDockStatus.Docked : VehicleDockStatus.Aligning,
            Reason = settled ? VehicleDockBlockReason.None : VehicleDockBlockReason.PoseLost,
            ServiceStartedTick = settled ? visit.ServiceStartedTick : 0 };
        Put(entities, truck, visit);
        return ticks >= 2;
    }

    public bool CanTransfer(SimulationContext context, EntityId truck, ulong generation, double quantity, double unitsPerSecond = 40)
    {
        if (!double.IsFinite(quantity) || quantity <= 0 || !double.IsFinite(unitsPerSecond) || unitsPerSecond <= 0 ||
            !ObservePose(context, truck, out var visit) || visit.Generation != generation ||
            visit.LastTransferTick == context.Tick.Value) return false;
        if (visit.ServiceStartedTick == 0)
        {
            Put(context.Entities, truck, visit with { ServiceStartedTick = context.Tick.Value, Status = VehicleDockStatus.Servicing });
            return false;
        }
        return (context.Tick.Value - visit.ServiceStartedTick) * context.TickDuration.TotalSeconds >= quantity / unitsPerSecond;
    }

    public static void RecordTransfer(SimulationContext context, EntityId truck, ulong generation)
    {
        if (context.Entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Generation == generation)
            Put(context.Entities, truck, visit with { LastTransferTick = context.Tick.Value, ServiceStartedTick = 0 });
    }

    public bool TryRelease(EntityRegistry entities, EntityId truck, EntityId building, ulong generation)
    {
        if (!entities.TryGetComponent(building, out VehicleDockSlot slot) || slot.Truck != truck || slot.Generation != generation || OwnerInLane(entities, slot) ||
            !LaneClear(entities, building, EntityId.Invalid, slot.Pose)) return false;
        entities.RemoveComponent<VehicleDockSlot>(building);
        if (entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.Building == building && visit.Generation == generation)
            entities.RemoveComponent<VehicleDockVisit>(truck);
        return true;
    }

    private bool LaneClear(EntityRegistry entities, EntityId building, EntityId truck, in VehicleDockPose pose)
    {
        float extent = pose.HalfWidth + 1;
        Vector3 minimum = Vector3.Min(pose.Entry, pose.Exit) - new Vector3(extent, 12, extent);
        Vector3 maximum = Vector3.Max(pose.Entry, pose.Exit) + new Vector3(extent, 12, extent);
        if (_terrain is not null && (minimum.X < _terrain.WorldBounds.Minimum.X || minimum.Z < _terrain.WorldBounds.Minimum.Z ||
            maximum.X > _terrain.WorldBounds.Maximum.X || maximum.Z > _terrain.WorldBounds.Maximum.Z)) return false;
        if (_spatial is null) return true;
        _spatial.QueryAabb(new AxisAlignedBounds(minimum, maximum), _neighbors);
        foreach (EntityId neighbor in _neighbors.Results)
        {
            if (neighbor == building || neighbor == truck || !entities.IsAlive(neighbor)) continue;
            if (entities.TryGetComponent(neighbor, out WorldTransform transform) &&
                entities.TryGetComponent(neighbor, out SpatialPresence presence) &&
                pose.Contains(transform.Position, MathF.Max(presence.HalfExtents.X, presence.HalfExtents.Z))) return false;
        }
        return true;
    }

    private static bool OwnerInLane(EntityRegistry entities, in VehicleDockSlot slot) =>
        entities.TryGetComponent(slot.Truck, out WorldTransform transform) && slot.Pose.Contains(transform.Position, slot.Pose.HalfLength);

    internal static void Put<T>(EntityRegistry entities, EntityId entity, T value) where T : struct
    {
        if (entities.HasComponent<T>(entity)) entities.SetComponent(entity, value);
        else entities.AddComponent(entity, value);
    }
}
