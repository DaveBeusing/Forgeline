namespace ForgeLine.Economy;

public sealed record InventoryStoreSnapshot(
    uint NextInventoryId,
    InventoryStoreMetrics Metrics,
    IReadOnlyList<InventoryStateSnapshot> Inventories);

public sealed record InventoryStateSnapshot(
    InventoryId InventoryId,
    double TotalCapacity,
    double TotalQuantity,
    IReadOnlyList<InventoryResourceQuantity> Resources);
