using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyResearchSystemTests
{
    private static readonly PlayerId Player = new(1);

    [Fact]
    public void ResearchConsumesPhysicalMaterialsAndUnlocksOnDeterministicTick()
    {
        TestWorld world =
            CreateWorld(
                powerFraction: 1.0,
                steel: 200.0,
                electronics: 100.0);

        EntityId request =
            AddRequest(
                world,
                TechnologyIds.IndustrialStandardization);

        world.Simulation.AdvanceOneTick();

        TechnologyResearchRequest running =
            world.Simulation.Entities
                .GetComponent<TechnologyResearchRequest>(
                    request);

        Assert.True(
            running.MaterialsConsumed);
        Assert.Equal(
            TechnologyResearchStatus.Researching,
            running.Status);
        Assert.Equal(
            120.0,
            world.Inventories.GetQuantity(
                world.Inventory,
                ResourceIds.Steel));
        Assert.Equal(
            80.0,
            world.Inventories.GetQuantity(
                world.Inventory,
                ResourceIds.Electronics));

        for (int tick = 1;
             tick < 120;
             tick++)
        {
            world.Simulation.AdvanceOneTick();
        }

        Assert.False(
            world.Simulation.Entities.IsAlive(
                request));
        Assert.True(
            TechnologyStateQueries.IsCompleted(
                world.Simulation.Entities,
                Player,
                TechnologyIds.IndustrialStandardization));
        Assert.True(
            TechnologyStateQueries.IsCapabilityUnlocked(
                world.Simulation.Entities,
                Player,
                TechnologyCapabilityIds.FieldEngineering));
    }

    [Fact]
    public void UnmetPrerequisiteBlocksWithoutConsumingMaterials()
    {
        TestWorld world =
            CreateWorld(
                BuildingIds.VehicleFactory,
                powerFraction: 1.0,
                steel: 300.0,
                electronics: 200.0);

        EntityId request =
            AddRequest(
                world,
                TechnologyIds.MechanizedSystems);

        world.Simulation.AdvanceOneTick();

        TechnologyResearchRequest blocked =
            world.Simulation.Entities
                .GetComponent<TechnologyResearchRequest>(
                    request);

        Assert.Equal(
            TechnologyResearchStatus.Blocked,
            blocked.Status);
        Assert.Equal(
            TechnologyResearchBlockReason.UnmetPrerequisite,
            blocked.BlockReason);
        Assert.Equal(
            300.0,
            world.Inventories.GetQuantity(
                world.Inventory,
                ResourceIds.Steel));
        Assert.Equal(
            0u,
            blocked.ProgressTicks);
    }

    [Fact]
    public void InsufficientPowerPausesProgressUntilFacilityRecovers()
    {
        TestWorld world =
            CreateWorld(
                powerFraction: 0.5,
                steel: 200.0,
                electronics: 100.0);

        EntityId request =
            AddRequest(
                world,
                TechnologyIds.IndustrialStandardization);

        world.Simulation.AdvanceOneTick();

        TechnologyResearchRequest blocked =
            world.Simulation.Entities
                .GetComponent<TechnologyResearchRequest>(
                    request);

        Assert.Equal(
            TechnologyResearchBlockReason.InsufficientPower,
            blocked.BlockReason);
        Assert.False(
            blocked.MaterialsConsumed);
        Assert.Equal(
            0u,
            blocked.ProgressTicks);

        world.Simulation.Entities.SetComponent(
            world.Facility,
            new PowerConsumer(
                20.0,
                PowerPriority.Critical,
                enabled: true,
                allocatedPower: 20.0,
                PowerOperationalState.Powered));

        world.Simulation.AdvanceOneTick();

        TechnologyResearchRequest resumed =
            world.Simulation.Entities
                .GetComponent<TechnologyResearchRequest>(
                    request);

        Assert.Equal(
            TechnologyResearchStatus.Researching,
            resumed.Status);
        Assert.Equal(
            1u,
            resumed.ProgressTicks);
    }

    [Fact]
    public void MissingMaterialsBlocksWithoutPartialConsumption()
    {
        TestWorld world =
            CreateWorld(
                powerFraction: 1.0,
                steel: 80.0,
                electronics: 10.0);

        EntityId request =
            AddRequest(
                world,
                TechnologyIds.IndustrialStandardization);

        world.Simulation.AdvanceOneTick();

        TechnologyResearchRequest blocked =
            world.Simulation.Entities
                .GetComponent<TechnologyResearchRequest>(
                    request);

        Assert.Equal(
            TechnologyResearchBlockReason.MissingMaterials,
            blocked.BlockReason);
        Assert.Equal(
            80.0,
            world.Inventories.GetQuantity(
                world.Inventory,
                ResourceIds.Steel));
        Assert.Equal(
            10.0,
            world.Inventories.GetQuantity(
                world.Inventory,
                ResourceIds.Electronics));
    }

    private static TestWorld CreateWorld(
        BuildingId buildingId = default,
        double powerFraction = 1.0,
        double steel = 200.0,
        double electronics = 100.0)
    {
        if (!buildingId.IsSpecified)
        {
            buildingId =
                BuildingIds.CommandCore;
        }

        var simulation =
            new SimulationCoordinator();
        var inventories =
            new InventoryStore();
        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    10_000.0));
        Assert.True(
            inventories.Add(
                inventory,
                ResourceIds.Steel,
                steel).Succeeded);
        Assert.True(
            inventories.Add(
                inventory,
                ResourceIds.Electronics,
                electronics).Succeeded);

        EntityId facility =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            facility,
            new CompletedBuilding(
                buildingId,
                Player,
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            facility,
            new ControllableEntity(
                Player,
                ControllableEntityCategory.Building));
        simulation.Entities.AddComponent(
            facility,
            new InventoryStorage(
                inventory));

        const double demand = 20.0;
        double allocated =
            demand *
            powerFraction;
        PowerOperationalState state =
            allocated <= 0.0
                ? PowerOperationalState.Offline
                : allocated >= demand
                    ? PowerOperationalState.Powered
                    : PowerOperationalState.Brownout;

        simulation.Entities.AddComponent(
            facility,
            new PowerConsumer(
                demand,
                PowerPriority.Critical,
                enabled: true,
                allocatedPower: allocated,
                state));

        TechnologyDefinitionCatalog catalog =
            DirectorateTechnologyDefinitions.CreateCatalog();
        simulation.RegisterSystem(
            new TechnologyResearchSystem(
                catalog,
                inventories));

        return new TestWorld(
            simulation,
            inventories,
            inventory,
            facility);
    }

    private static EntityId AddRequest(
        TestWorld world,
        TechnologyId technologyId)
    {
        EntityId request =
            world.Simulation.Entities.CreateEntity();
        world.Simulation.Entities.AddComponent(
            request,
            new TechnologyResearchRequest(
                Player,
                technologyId,
                world.Facility,
                world.Inventory,
                world.Simulation.CurrentTick));

        return request;
    }

    private sealed record TestWorld(
        SimulationCoordinator Simulation,
        InventoryStore Inventories,
        InventoryId Inventory,
        EntityId Facility);
}
