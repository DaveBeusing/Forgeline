using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum VehicleQueueService : byte { CargoPickup, CargoDelivery, SupplyLoading, CargoRefueling }
public enum VehicleQueueWaitReason : byte { None, Predecessor, OccupiedExit, Full, GeometryBlocked, Unavailable }

public readonly record struct VehicleQueueSequence(ulong Value);

/// <summary>A confirmed local arrival. Stock, graph capacity and dock ownership remain separate.</summary>
public readonly record struct VehicleQueueEntry(
    EntityId Building, PlayerId Owner, ulong Generation, SimulationTick RegisteredAtTick,
    VehicleQueueService Service, int Position = 0, int WaitingCount = 0,
    Vector3 Target = default, float Heading = 0, EntityId Predecessor = default,
    VehicleQueueWaitReason WaitReason = VehicleQueueWaitReason.Predecessor,
    SimulationTick ChangedAtTick = default, bool ArrivedAtService = false);

public readonly record struct VehicleQueueLane(Vector3 Front, float Heading, int Capacity);

/// <summary>FIFO roster only. The shared docking service alone owns physical bay admission/release.</summary>
public sealed class VehicleQueueCoordinator
{
    private readonly List<EntityId> _members = new();

    public bool TryRegister(EntityRegistry entities, EntityId truck, EntityId building, PlayerId owner,
        SimulationTick tick, VehicleQueueService service, int capacity = 20)
    {
        if (capacity <= 0 || !VehicleDockingService.OwnsTruck(entities, truck, owner) ||
            !VehicleDockingService.Operational(entities, building) ||
            !entities.TryGetComponent(building, out CompletedBuilding completed) || completed.Owner != owner ||
            entities.TryGetComponent(truck, out ControllableEntity control) && (control.Owner != owner || !control.IsControllable))
            return false;
        if (entities.TryGetComponent(truck, out VehicleQueueEntry existing))
            return existing.Building == building && existing.Owner == owner && existing.Service == service;
        Collect(entities, building);
        if (_members.Count >= capacity)
        {
            EntityId displaced = SameTickReplacement(entities, truck, building, tick, capacity);
            if (!displaced.IsValid) return false;
            var previous = entities.GetComponent<VehicleQueueEntry>(displaced);
            Remove(entities, displaced, building, previous.Generation);
            VehicleDockingService.RevokeUnconfirmedQueueAdmission(entities, displaced, building);
        }
        ulong generation = entities.TryGetComponent(truck, out VehicleQueueSequence sequence) ? checked(sequence.Value + 1) : 1;
        VehicleDockingService.Put(entities, truck, new VehicleQueueSequence(generation));
        VehicleDockingService.Put(entities, truck, new VehicleQueueEntry(building, owner, generation, tick, service, ChangedAtTick: tick));
        return true;
    }

    internal EntityId SameTickReplacement(EntityRegistry entities, EntityId truck, EntityId building, SimulationTick tick, int capacity)
    {
        Collect(entities, building);
        if (capacity <= 0 || _members.Count < capacity) return EntityId.Invalid;
        EntityId last = _members[^1];
        var arrival = entities.GetComponent<VehicleQueueEntry>(last);
        return arrival.RegisteredAtTick == tick && (truck.Index < last.Index || truck.Index == last.Index && truck.Generation < last.Generation)
            ? last : EntityId.Invalid;
    }

    public bool IsHead(EntityRegistry entities, EntityId truck, EntityId building, SimulationTick tick)
    {
        Collect(entities, building);
        return _members.Count > 0 && _members[0] == truck &&
            entities.GetComponent<VehicleQueueEntry>(truck).RegisteredAtTick.Value < tick.Value;
    }

    public IReadOnlyList<EntityId> Collect(EntityRegistry entities, EntityId building)
    {
        _members.Clear();
        foreach (EntityId entity in entities.Query<VehicleQueueEntry>())
        {
            var entry = entities.GetComponent<VehicleQueueEntry>(entity);
            if (entry.Building == building) _members.Add(entity);
        }
        _members.Sort((first, second) =>
        {
            int order = entities.GetComponent<VehicleQueueEntry>(first).RegisteredAtTick.Value.CompareTo(
                entities.GetComponent<VehicleQueueEntry>(second).RegisteredAtTick.Value);
            if (order != 0) return order;
            order = first.Index.CompareTo(second.Index);
            return order != 0 ? order : first.Generation.CompareTo(second.Generation);
        });
        return _members;
    }

    public static bool Remove(EntityRegistry entities, EntityId truck, EntityId building, ulong generation)
    {
        if (!entities.TryGetComponent(truck, out VehicleQueueEntry entry) || entry.Building != building || entry.Generation != generation)
            return false;
        entities.RemoveComponent<VehicleQueueEntry>(truck);
        if (entities.HasComponent<VehicleQueueMotion>(truck)) entities.RemoveComponent<VehicleQueueMotion>(truck);
        return true;
    }

    public void Cleanup(EntityRegistry entities)
    {
        _members.Clear();
        foreach (EntityId truck in entities.Query<VehicleQueueEntry>())
        {
            var entry = entities.GetComponent<VehicleQueueEntry>(truck);
            if (!VehicleDockingService.OwnsTruck(entities, truck, entry.Owner) ||
                !VehicleDockingService.Operational(entities, entry.Building) ||
                !entities.TryGetComponent(entry.Building, out CompletedBuilding building) || building.Owner != entry.Owner ||
                entities.TryGetComponent(truck, out ControllableEntity control) && (control.Owner != entry.Owner || !control.IsControllable) ||
                !entities.HasComponent<VehicleDockVisit>(truck) && !entities.HasComponent<CargoTransportOrder>(truck) &&
                !entities.HasComponent<SupplyLoadingOrder>(truck) &&
                !entities.HasComponent<CargoFuelServiceState>(truck)) _members.Add(truck);
        }
        foreach (EntityId truck in _members)
        {
            entities.RemoveComponent<VehicleQueueEntry>(truck);
            if (entities.HasComponent<VehicleQueueMotion>(truck)) entities.RemoveComponent<VehicleQueueMotion>(truck);
        }
    }
}
