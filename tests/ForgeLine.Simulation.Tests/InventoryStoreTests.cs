using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class InventoryStoreTests
{
    [Fact]
    public void ExactCapacityRejectsFurtherAdds()
    {
        var store = new InventoryStore();
        InventoryId inventory = store.CreateInventory(
            new InventorySpecification(totalCapacity: 100.0));

        InventoryOperationResult first =
            store.Add(inventory, ResourceIds.FerrousOre, 100.0);
        InventoryOperationResult second =
            store.Add(inventory, ResourceIds.FerrousOre, 0.01);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(
            InventoryFailureReason.CapacityExceeded,
            second.Failure);
        Assert.Equal(100.0, store.GetTotalQuantity(inventory));
        Assert.Equal(0.0, store.GetRemainingCapacity(inventory));
    }

    [Fact]
    public void AcceptedResourceFilterRejectsOtherResources()
    {
        var store = new InventoryStore();
        InventoryId inventory = store.CreateInventory(
            new InventorySpecification(
                100.0,
                [ResourceIds.FerrousOre, ResourceIds.Silicates]));

        InventoryOperationResult accepted =
            store.Add(inventory, ResourceIds.Silicates, 25.0);
        InventoryOperationResult rejected =
            store.Add(inventory, ResourceIds.Volatiles, 10.0);

        Assert.True(accepted.Succeeded);
        Assert.False(rejected.Succeeded);
        Assert.Equal(
            InventoryFailureReason.ResourceRejected,
            rejected.Failure);
        Assert.Equal(25.0, store.GetTotalQuantity(inventory));
    }

    [Fact]
    public void PerResourceCapacityLimitsSpecificResource()
    {
        var store = new InventoryStore();
        InventoryId inventory = store.CreateInventory(
            new InventorySpecification(
                100.0,
                perResourceCapacities:
                    new Dictionary<ResourceId, double>
                    {
                        [ResourceIds.FerrousOre] = 30.0
                    }));

        Assert.True(
            store.Add(inventory, ResourceIds.FerrousOre, 30.0).Succeeded);
        Assert.Equal(
            0.0,
            store.GetAddableQuantity(
                inventory,
                ResourceIds.FerrousOre,
                10.0));
        Assert.True(
            store.Add(inventory, ResourceIds.Volatiles, 50.0).Succeeded);
        Assert.Equal(80.0, store.GetTotalQuantity(inventory));
    }

    [Fact]
    public void TransferFailureLeavesBothInventoriesUnchanged()
    {
        var store = new InventoryStore();
        InventoryId source = store.CreateInventory(
            new InventorySpecification(100.0));
        InventoryId destination = store.CreateInventory(
            new InventorySpecification(
                20.0,
                [ResourceIds.Volatiles]));

        Assert.True(
            store.Add(source, ResourceIds.FerrousOre, 40.0).Succeeded);

        InventoryOperationResult result =
            store.Transfer(
                source,
                destination,
                ResourceIds.FerrousOre,
                15.0);

        Assert.False(result.Succeeded);
        Assert.Equal(
            InventoryFailureReason.ResourceRejected,
            result.Failure);
        Assert.Equal(
            40.0,
            store.GetQuantity(source, ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            store.GetQuantity(destination, ResourceIds.FerrousOre));
        Assert.Equal(1, store.Metrics.TransferFailures);
    }

    [Fact]
    public void SuccessfulTransferConservesResources()
    {
        var store = new InventoryStore();
        InventoryId source = store.CreateInventory(
            new InventorySpecification(100.0));
        InventoryId destination = store.CreateInventory(
            new InventorySpecification(100.0));

        Assert.True(
            store.Add(source, ResourceIds.Silicates, 60.0).Succeeded);

        double before =
            store.GetTotalQuantity(source) +
            store.GetTotalQuantity(destination);

        InventoryOperationResult result =
            store.Transfer(
                source,
                destination,
                ResourceIds.Silicates,
                25.0);

        double after =
            store.GetTotalQuantity(source) +
            store.GetTotalQuantity(destination);

        Assert.True(result.Succeeded);
        Assert.Equal(before, after);
        Assert.Equal(
            35.0,
            store.GetQuantity(source, ResourceIds.Silicates));
        Assert.Equal(
            25.0,
            store.GetQuantity(destination, ResourceIds.Silicates));
    }

    [Fact]
    public void ReservationsReduceAvailableQuantity()
    {
        var store = new InventoryStore();
        InventoryId inventory = store.CreateInventory(
            new InventorySpecification(100.0));

        Assert.True(
            store.Add(inventory, ResourceIds.Volatiles, 50.0).Succeeded);
        Assert.True(
            store.Reserve(inventory, ResourceIds.Volatiles, 30.0).Succeeded);

        Assert.Equal(
            30.0,
            store.GetReservedQuantity(
                inventory,
                ResourceIds.Volatiles));
        Assert.Equal(
            20.0,
            store.GetAvailableQuantity(
                inventory,
                ResourceIds.Volatiles));
        Assert.False(
            store.Remove(
                inventory,
                ResourceIds.Volatiles,
                25.0).Succeeded);

        Assert.True(
            store.ConsumeReserved(
                inventory,
                ResourceIds.Volatiles,
                10.0).Succeeded);
        Assert.Equal(
            40.0,
            store.GetQuantity(inventory, ResourceIds.Volatiles));
        Assert.Equal(
            20.0,
            store.GetReservedQuantity(
                inventory,
                ResourceIds.Volatiles));
    }

    [Fact]
    public void DestroyedInventoryHandleIsNotReused()
    {
        var store = new InventoryStore();
        InventoryId destroyed = store.CreateInventory(
            new InventorySpecification(10.0));

        Assert.True(store.DestroyInventory(destroyed));

        InventoryId replacement = store.CreateInventory(
            new InventorySpecification(10.0));

        Assert.NotEqual(destroyed, replacement);
        Assert.False(store.Contains(destroyed));
        Assert.Equal(
            InventoryFailureReason.InventoryNotFound,
            store.Add(
                destroyed,
                ResourceIds.FerrousOre,
                1.0).Failure);
    }

    [Fact]
    public void DebugSnapshotReportsCapacityAndStorageDepotState()
    {
        var entities = new EntityRegistry();
        var store = new InventoryStore();
        InventoryId inventory = store.CreateInventory(
            new InventorySpecification(200.0));
        Assert.True(
            store.Add(inventory, ResourceIds.FerrousOre, 50.0).Succeeded);

        EntityId entity = entities.CreateEntity();
        entities.AddComponent(
            entity,
            new InventoryStorage(inventory));
        entities.AddComponent(
            entity,
            new StorageDepot(
                inventory,
                new FactionId(3)));

        InventoryDebugSnapshot snapshot =
            InventoryDebugSnapshot.Capture(
                entities,
                store,
                InitialResourceDefinitions.CreateCatalog());

        InventoryReadModel readModel = Assert.Single(snapshot.Inventories);
        StorageDepotReadModel depot = Assert.Single(snapshot.StorageDepots);

        Assert.True(readModel.IsValid);
        Assert.Equal(0.25, readModel.CapacityUtilization);
        Assert.Equal("resource.ferrous_ore", Assert.Single(readModel.Resources).ResourceKey);
        Assert.Equal(StorageDepotState.Operational, depot.State);
        Assert.Equal(0.25, depot.CapacityUtilization);
    }
}
