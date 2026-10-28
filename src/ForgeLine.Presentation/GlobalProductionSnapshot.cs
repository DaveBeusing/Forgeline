using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum GlobalProductionFilter : byte { All, Selected, Infantry, Vehicles, Logistics, Blocked }
public readonly record struct GlobalProductionMaterial(ResourceId Resource, string Name, double Required);
public readonly record struct GlobalProductionStockAmount(ResourceId Resource, string Name, double Quantity, double Reserved)
{
    public double Available => Math.Max(0, Quantity - Reserved);
}
public sealed record GlobalProductionStock(InventoryId Inventory, bool Available,
    IReadOnlyList<GlobalProductionStockAmount> Amounts);
public sealed record GlobalProductionOrder(EntityId Request, UnitId Unit, string Name,
    ProductionPriority Priority, bool Paused, bool Active, bool InputsReserved,
    IReadOnlyList<GlobalProductionMaterial> Materials);
public sealed record GlobalProductionFactory(EntityId Entity, string Name, UnitProductionCapability Capabilities,
    UnitProductionStatus Status, UnitProductionBlockReason BlockReason, EntityId ActiveRequest,
    double Progress, int QueueCount, int StockIndex, Vector3? RallyPoint,
    IReadOnlyList<GlobalProductionOrder> Orders);

/// <summary>Bounded copied facts from one completed tick. Reservations are inventory stock, not free resources.</summary>
public sealed class GlobalProductionSnapshot
{
    public const int MaximumFactories = 256;
    public const int MaximumOrdersPerFactory = 16;
    public const int MaximumMaterials = 8;

    public GlobalProductionSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IReadOnlyList<GlobalProductionFactory> factories, IReadOnlyList<GlobalProductionStock> stocks,
        int factoryCount, int orderCount)
    {
        Session = session; Tick = tick; Player = player;
        Factories = Array.AsReadOnly(factories.Take(MaximumFactories).Select(factory => factory with
        {
            Orders = Array.AsReadOnly(factory.Orders.Take(MaximumOrdersPerFactory).Select(order => order with
            { Materials = Array.AsReadOnly(order.Materials.Take(MaximumMaterials).ToArray()) }).ToArray())
        }).ToArray());
        Stocks = Array.AsReadOnly(stocks.Take(MaximumFactories).Select(stock => stock with
        { Amounts = Array.AsReadOnly(stock.Amounts.Take(MaximumMaterials).ToArray()) }).ToArray());
        FactoryCount = factoryCount; OrderCount = orderCount;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<GlobalProductionFactory> Factories { get; }
    public IReadOnlyList<GlobalProductionStock> Stocks { get; }
    public int FactoryCount { get; }
    public int OrderCount { get; }
    public bool Limited
    {
        get { if (FactoryCount > Factories.Count) return true; for (int i = 0; i < Factories.Count; i++) if (Factories[i].QueueCount > Factories[i].Orders.Count) return true; return false; }
    }

    public static GlobalProductionSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        OperationsSnapshot.Resolve(snapshot)?.Production is { } data && data.Session == snapshot!.SessionId &&
        data.Tick == snapshot.Tick && data.Player == snapshot.PlayerExperience!.Value.Player ? data : null;

    public static bool Matches(GlobalProductionFactory factory, GlobalProductionFilter filter,
        IReadOnlyCollection<EntityId>? selection) => filter switch
    {
        GlobalProductionFilter.Selected => selection?.Contains(factory.Entity) == true,
        GlobalProductionFilter.Infantry => (factory.Capabilities & UnitProductionCapability.Infantry) != 0,
        GlobalProductionFilter.Vehicles => (factory.Capabilities & UnitProductionCapability.Vehicle) != 0,
        GlobalProductionFilter.Logistics => (factory.Capabilities & UnitProductionCapability.Logistics) != 0,
        GlobalProductionFilter.Blocked => factory.BlockReason != UnitProductionBlockReason.None,
        _ => true
    };

    public GlobalProductionFactory? Find(EntityId entity)
    {
        for (int i = 0; i < Factories.Count; i++) if (Factories[i].Entity == entity) return Factories[i];
        return null;
    }

    /// <summary>Deduplicate shared input inventories within the current role/selection filter.</summary>
    public (double Available, double Reserved) Sum(ResourceId resource, GlobalProductionFilter filter,
        IReadOnlyCollection<EntityId>? selection)
    {
        Span<bool> seen = stackalloc bool[MaximumFactories];
        seen.Clear();
        double available = 0, reserved = 0;
        for (int i = 0; i < Factories.Count; i++)
        {
            var factory = Factories[i];
            int index = factory.StockIndex;
            if (!Matches(factory, filter, selection) || index < 0 || index >= Stocks.Count || seen[index]) continue;
            seen[index] = true;
            if (!Stocks[index].Available) continue;
            for (int j = 0; j < Stocks[index].Amounts.Count; j++)
            { var amount = Stocks[index].Amounts[j]; if (amount.Resource == resource) { available += amount.Available; reserved += amount.Reserved; } }
        }
        return (available, reserved);
    }
}

internal static class GlobalProductionCapture
{
    private readonly record struct Request(EntityId Entity, UnitProductionRequest Value, bool Active);
    private sealed class Factory(EntityId entity, UnitProductionFacility value)
    {
        internal EntityId Entity { get; } = entity;
        internal UnitProductionFacility Value { get; } = value;
        internal int QueueCount { get; set; }
        internal List<Request> Requests { get; } = new(GlobalProductionSnapshot.MaximumOrdersPerFactory);
    }

    internal static GlobalProductionSnapshot Capture(SimulationContext context, PresentationExtractionContext extraction,
        IReadOnlyCollection<EntityId> selection)
    {
        var entities = context.Entities;
        var scenario = extraction.Scenario;
        var selected = new HashSet<EntityId>();
        foreach (var entity in selection)
            if (Owned(entity) && selected.Count < GlobalProductionSnapshot.MaximumFactories) selected.Add(entity);
        var retained = new List<Factory>(GlobalProductionSnapshot.MaximumFactories);
        int count = 0, orderCount = 0;
        foreach (var entity in entities.Query<UnitProductionFacility>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!Owned(entity)) continue;
            count++;
            if (retained.Count < GlobalProductionSnapshot.MaximumFactories)
                retained.Add(new(entity, entities.GetComponent<UnitProductionFacility>(entity)));
            else if (selected.Contains(entity))
            {
                int replace = retained.FindLastIndex(f => !selected.Contains(f.Entity));
                if (replace >= 0) retained[replace] = new(entity, entities.GetComponent<UnitProductionFacility>(entity));
            }
        }
        retained.Sort(static (a, b) => a.Entity.CompareTo(b.Entity));
        var byId = retained.ToDictionary(f => f.Entity);
        foreach (var entity in entities.Query<UnitProductionRequest>(QueryIterationOrder.StableByEntityIndex))
        {
            var request = entities.GetComponent<UnitProductionRequest>(entity);
            if (!Owned(request.Facility)) continue;
            orderCount++;
            if (!byId.TryGetValue(request.Facility, out var factory)) continue;
            factory.QueueCount++;
            var candidate = new Request(entity, request, factory.Value.ActiveRequest == entity);
            int index = 0;
            while (index < factory.Requests.Count && Compare(candidate, factory.Requests[index]) >= 0) index++;
            if (index >= GlobalProductionSnapshot.MaximumOrdersPerFactory) continue;
            factory.Requests.Insert(index, candidate);
            if (factory.Requests.Count > GlobalProductionSnapshot.MaximumOrdersPerFactory)
                factory.Requests.RemoveAt(GlobalProductionSnapshot.MaximumOrdersPerFactory);
        }
        var stocks = new List<GlobalProductionStock>();
        var inventoryIndices = new Dictionary<InventoryId, int>();
        var rows = new List<GlobalProductionFactory>(retained.Count);
        foreach (var factory in retained)
        {
            var value = factory.Value;
            if (!inventoryIndices.TryGetValue(value.InputInventory, out int stockIndex))
            {
                stockIndex = stocks.Count;
                inventoryIndices.Add(value.InputInventory, stockIndex);
                bool available = scenario.Inventories.Contains(value.InputInventory);
                var amounts = new List<GlobalProductionStockAmount>();
                if (available)
                    foreach (var resource in scenario.Services.Resources.Definitions.Take(GlobalProductionSnapshot.MaximumMaterials))
                        amounts.Add(new(resource.Id, resource.DisplayName,
                            scenario.Inventories.GetQuantity(value.InputInventory, resource.Id),
                            scenario.Inventories.GetReservedQuantity(value.InputInventory, resource.Id)));
                stocks.Add(new(value.InputInventory, available, amounts));
            }
            var orders = new List<GlobalProductionOrder>(factory.Requests.Count);
            foreach (var request in factory.Requests)
            {
                var materials = new List<GlobalProductionMaterial>();
                string name = request.Value.UnitId.ToString();
                if (scenario.Services.UnitDefinitions.TryGet(request.Value.UnitId, out var definition))
                {
                    name = definition.DisplayName;
                    foreach (var cost in definition.Costs.Take(GlobalProductionSnapshot.MaximumMaterials))
                        materials.Add(new(cost.ResourceId, scenario.Services.Resources.TryGet(cost.ResourceId, out var resource)
                            ? resource.DisplayName : cost.ResourceId.ToString(), cost.Quantity));
                }
                orders.Add(new(request.Entity, request.Value.UnitId, name, request.Value.Priority,
                    request.Value.Paused, request.Active, request.Active && value.InputsReserved, materials));
            }
            string factoryName = "UNIT FACTORY";
            if (entities.TryGetComponent(factory.Entity, out CompletedBuilding building) &&
                scenario.Services.BuildingDefinitions.TryGet(building.BuildingId, out var buildingDefinition))
                factoryName = buildingDefinition.DisplayName;
            double progress = scenario.Services.UnitDefinitions.TryGet(value.ActiveUnit, out var active)
                ? Math.Clamp(value.ProgressTicks / (double)active.ProductionTicks, 0, 1) : 0;
            rows.Add(new(factory.Entity, factoryName, value.Capabilities, value.Status, value.BlockReason,
                value.ActiveRequest, progress, factory.QueueCount, stockIndex,
                entities.TryGetComponent(factory.Entity, out UnitProductionRallyPoint rally) ? rally.WorldPosition : null, orders));
        }
        return new(scenario.Simulation.SessionId, context.Tick, extraction.Player, rows, stocks, count, orderCount);

        bool Owned(EntityId entity) => entities.TryGetComponent(entity, out UnitProductionFacility facility) &&
            facility.Owner == extraction.Player && entities.TryGetComponent(entity, out ControllableEntity owner) &&
            owner.Owner == extraction.Player && owner.IsControllable &&
            (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted);
    }

    private static int Compare(Request a, Request b)
    {
        int active = b.Active.CompareTo(a.Active);
        if (active != 0) return active;
        int priority = a.Value.Priority.CompareTo(b.Value.Priority);
        if (priority != 0) return priority;
        int tick = a.Value.SubmittedAtTick.CompareTo(b.Value.SubmittedAtTick);
        return tick != 0 ? tick : a.Entity.CompareTo(b.Entity);
    }
}
