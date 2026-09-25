using ForgeLine.Core;
using ForgeLine.Ecs;

namespace ForgeLine.Economy;

public readonly record struct InventoryResourceReadModel(
    ResourceId ResourceId,
    string ResourceKey,
    double Quantity,
    double ReservedQuantity,
    double AvailableQuantity);

public readonly record struct InventoryReadModel(
    EntityId Entity,
    InventoryId InventoryId,
    bool IsValid,
    double TotalQuantity,
    double TotalCapacity,
    double CapacityUtilization,
    IReadOnlyList<InventoryResourceReadModel> Resources);

public readonly record struct StorageDepotReadModel(
    EntityId Entity,
    InventoryId InventoryId,
    FactionId Owner,
    StorageDepotState State,
    double TotalQuantity,
    double TotalCapacity,
    double CapacityUtilization);

public sealed class InventoryDebugSnapshot
{
    public InventoryDebugSnapshot(
        IReadOnlyList<InventoryReadModel> inventories,
        IReadOnlyList<StorageDepotReadModel> storageDepots,
        InventoryStoreMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(storageDepots);

        Inventories = inventories;
        StorageDepots = storageDepots;
        Metrics = metrics;
    }

    public IReadOnlyList<InventoryReadModel> Inventories { get; }

    public IReadOnlyList<StorageDepotReadModel> StorageDepots { get; }

    public InventoryStoreMetrics Metrics { get; }

    public static InventoryDebugSnapshot Capture(
        EntityRegistry entities,
        InventoryStore store,
        ResourceCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(store);

        var inventories = new List<InventoryReadModel>(
            entities.GetComponentCount<InventoryStorage>());
        var storageDepots = new List<StorageDepotReadModel>(
            entities.GetComponentCount<StorageDepot>());

        foreach (EntityId entity in
                 entities.Query<InventoryStorage>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            InventoryStorage storage =
                entities.GetComponent<InventoryStorage>(entity);
            InventoryId inventoryId = storage.InventoryId;

            if (!store.Contains(inventoryId))
            {
                inventories.Add(
                    new InventoryReadModel(
                        entity,
                        inventoryId,
                        false,
                        0.0,
                        0.0,
                        0.0,
                        Array.Empty<InventoryResourceReadModel>()));
                continue;
            }

            double totalQuantity = store.GetTotalQuantity(inventoryId);
            double totalCapacity = store.GetTotalCapacity(inventoryId);
            InventoryResourceQuantity[] quantities =
                store.GetResourceQuantities(inventoryId);
            var resources =
                new InventoryResourceReadModel[quantities.Length];

            for (int index = 0; index < quantities.Length; index++)
            {
                InventoryResourceQuantity quantity = quantities[index];
                string resourceKey =
                    catalog is not null &&
                    catalog.TryGet(
                        quantity.ResourceId,
                        out ResourceDefinition? definition)
                        ? definition.Key
                        : quantity.ResourceId.ToString();

                resources[index] =
                    new InventoryResourceReadModel(
                        quantity.ResourceId,
                        resourceKey,
                        quantity.Quantity,
                        quantity.ReservedQuantity,
                        quantity.AvailableQuantity);
            }

            inventories.Add(
                new InventoryReadModel(
                    entity,
                    inventoryId,
                    true,
                    totalQuantity,
                    totalCapacity,
                    totalCapacity > 0.0
                        ? totalQuantity / totalCapacity
                        : 0.0,
                    resources));
        }

        foreach (EntityId entity in
                 entities.Query<StorageDepot>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            StorageDepot depot =
                entities.GetComponent<StorageDepot>(entity);
            double totalQuantity = store.Contains(depot.InventoryId)
                ? store.GetTotalQuantity(depot.InventoryId)
                : 0.0;
            double totalCapacity = store.Contains(depot.InventoryId)
                ? store.GetTotalCapacity(depot.InventoryId)
                : 0.0;

            storageDepots.Add(
                new StorageDepotReadModel(
                    entity,
                    depot.InventoryId,
                    depot.Owner,
                    depot.State,
                    totalQuantity,
                    totalCapacity,
                    totalCapacity > 0.0
                        ? totalQuantity / totalCapacity
                        : 0.0));
        }

        return new InventoryDebugSnapshot(
            inventories,
            storageDepots,
            store.Metrics);
    }
}
