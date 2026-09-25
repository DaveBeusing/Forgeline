using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class ResourceExtractionInventoryTests
{
    [Fact]
    public void ExtractorStoresOutputInInventory()
    {
        var inventories = new InventoryStore();
        var system = new ResourceExtractionSystem(inventories: inventories);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId output = AddInventory(
            simulation,
            inventories,
            totalCapacity: 10.0);
        EntityId deposit = AddDeposit(
            simulation,
            totalQuantity: 100.0);
        EntityId extractor = AddExtractor(
            simulation,
            deposit,
            output);

        simulation.AdvanceOneTick();

        InventoryId inventoryId =
            simulation.Entities.GetComponent<InventoryStorage>(output).InventoryId;
        ResourceDeposit depositState =
            simulation.Entities.GetComponent<ResourceDeposit>(deposit);
        ResourceExtractor extractorState =
            simulation.Entities.GetComponent<ResourceExtractor>(extractor);

        Assert.InRange(
            inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre),
            0.499999999,
            0.500000001);
        Assert.InRange(
            depositState.RemainingQuantity,
            99.499999999,
            99.500000001);
        Assert.Equal(ResourceExtractorState.Extracting, extractorState.State);
        Assert.Equal(0, system.Metrics.BlockedExtractorCount);
    }

    [Fact]
    public void FullOutputBlocksWithoutDrainingDeposit()
    {
        var inventories = new InventoryStore();
        var system = new ResourceExtractionSystem(inventories: inventories);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId output = AddInventory(
            simulation,
            inventories,
            totalCapacity: 1.0);
        InventoryId inventoryId =
            simulation.Entities.GetComponent<InventoryStorage>(output).InventoryId;
        Assert.True(
            inventories.Add(
                inventoryId,
                ResourceIds.FerrousOre,
                1.0).Succeeded);

        EntityId deposit = AddDeposit(
            simulation,
            totalQuantity: 100.0);
        EntityId extractor = AddExtractor(
            simulation,
            deposit,
            output);

        simulation.AdvanceOneTick();

        ResourceDeposit depositState =
            simulation.Entities.GetComponent<ResourceDeposit>(deposit);
        ResourceExtractor extractorState =
            simulation.Entities.GetComponent<ResourceExtractor>(extractor);

        Assert.Equal(100.0, depositState.RemainingQuantity);
        Assert.Equal(
            1.0,
            inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre));
        Assert.Equal(
            ResourceExtractorState.OutputBlocked,
            extractorState.State);
        Assert.Equal(1, system.Metrics.BlockedExtractorCount);
        Assert.Equal(0.0, system.Metrics.LastTickExtractedQuantity);
    }

    [Fact]
    public void PartialOutputCapacityThrottlesAndConservesResources()
    {
        var inventories = new InventoryStore();
        var system = new ResourceExtractionSystem(inventories: inventories);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId output = AddInventory(
            simulation,
            inventories,
            totalCapacity: 0.25);
        InventoryId inventoryId =
            simulation.Entities.GetComponent<InventoryStorage>(output).InventoryId;
        EntityId deposit = AddDeposit(
            simulation,
            totalQuantity: 10.0);
        EntityId extractor = AddExtractor(
            simulation,
            deposit,
            output);

        double before =
            simulation.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity +
            inventories.GetTotalQuantity(inventoryId);

        simulation.AdvanceOneTick();

        double after =
            simulation.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity +
            inventories.GetTotalQuantity(inventoryId);
        ResourceExtractor extractorState =
            simulation.Entities.GetComponent<ResourceExtractor>(extractor);

        Assert.Equal(before, after);
        Assert.Equal(
            0.25,
            inventories.GetQuantity(
                inventoryId,
                ResourceIds.FerrousOre));
        Assert.Equal(
            ResourceExtractorState.OutputConstrained,
            extractorState.State);

        simulation.AdvanceOneTick();

        Assert.Equal(
            ResourceExtractorState.OutputBlocked,
            simulation.Entities.GetComponent<ResourceExtractor>(extractor).State);
        Assert.Equal(
            9.75,
            simulation.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity);
    }

    [Fact]
    public void StaleOutputEntityStopsExtractionSafely()
    {
        var inventories = new InventoryStore();
        var system = new ResourceExtractionSystem(inventories: inventories);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId output = AddInventory(
            simulation,
            inventories,
            totalCapacity: 10.0);
        EntityId deposit = AddDeposit(
            simulation,
            totalQuantity: 10.0);
        EntityId extractor = AddExtractor(
            simulation,
            deposit,
            output);

        Assert.True(simulation.Entities.DestroyEntity(output));
        simulation.AdvanceOneTick();

        Assert.Equal(
            10.0,
            simulation.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity);
        Assert.Equal(
            ResourceExtractorState.OutputUnavailable,
            simulation.Entities.GetComponent<ResourceExtractor>(extractor).State);
        Assert.Equal(1, system.Metrics.BlockedExtractorCount);
    }

    private static EntityId AddInventory(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        double totalCapacity)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        InventoryId inventoryId =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity,
                    [ResourceIds.FerrousOre]));
        simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(inventoryId));
        return entity;
    }

    private static EntityId AddDeposit(
        SimulationCoordinator simulation,
        double totalQuantity)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new ResourceDeposit(
                ResourceIds.FerrousOre,
                new AxisAlignedBounds(
                    Vector3.Zero,
                    new Vector3(2.0f, 1.0f, 2.0f)),
                totalQuantity,
                baseExtractionRatePerSecond: 10.0));
        return entity;
    }

    private static EntityId AddExtractor(
        SimulationCoordinator simulation,
        EntityId deposit,
        EntityId output)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new ResourceExtractor(
                deposit,
                ResourceIds.FerrousOre,
                maximumExtractionRatePerSecond: 10.0,
                outputInventory: output));
        return entity;
    }
}
