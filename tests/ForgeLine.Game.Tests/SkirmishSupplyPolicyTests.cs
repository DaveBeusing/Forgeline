using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishSupplyPolicyTests
{
    [Fact]
    public void EmptySupplyTruckDrivesToDepotAndLoadsPhysicalStock()
    {
        VerticalSliceScenario scenario = VerticalSliceScenario.Create(
            VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        InventoryId inventory = scenario.Inventories.CreateInventory(new InventorySpecification(2_500.0));
        Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Fuel, 600.0).Succeeded);
        Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Ammunition, 800.0).Succeeded);
        EntityId depot = entities.CreateEntity();
        entities.AddComponent(depot, new WorldTransform(new Vector3(420.0f, 0.0f, 1800.0f), Quaternion.Identity, Vector3.One));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, scenario.West.Player, SimulationTick.Zero));
        entities.AddComponent(depot, new SupplyDepot(inventory, scenario.West.Player));
        entities.AddComponent(depot, new SpatialPresence(new Vector3(8.0f, 4.0f, 8.0f),
            new SpatialEntryMetadata(scenario.West.Player.Value, 0, SpatialMobility.Static)));
        EntityId truck = scenario.UnitFactory.Create(
            DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck],
            new Vector3(360.0f, 0.0f, 1800.0f), scenario.West.Player);
        SupplyTruck supply = entities.GetComponent<SupplyTruck>(truck);

        scenario.Simulation.RunTicks(600, TestContext.Current.CancellationToken);

        Assert.Equal(supply.FuelTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), precision: 6);
        Assert.Equal(supply.AmmunitionTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition), precision: 6);
        Assert.Equal(600.0 - supply.FuelTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Fuel), precision: 6);
        Assert.Equal(800.0 - supply.AmmunitionTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Ammunition), precision: 6);
    }

    [Fact]
    public void FuelRecoveryPoliciesPrioritizeFieldSupplyOverVehicleProduction()
    {
        VerticalSliceScenario scenario = VerticalSliceScenario.Create(
            VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;

        scenario.Simulation.AdvanceOneTick();

        EntityId refinery = FindOwnedBuilding(
            entities,
            scenario.West.Player,
            BuildingIds.Refinery);
        EntityId supplyDepot = FindOwnedBuilding(
            entities,
            scenario.West.Player,
            BuildingIds.SupplyDepot);
        EntityId vehicleFactory = FindOwnedBuilding(
            entities,
            scenario.West.Player,
            BuildingIds.VehicleFactory);

        Assert.Equal(
            LogisticsStockPriority.Critical,
            FindStockPolicy(
                entities,
                refinery,
                ResourceIds.Volatiles).Priority);
        Assert.Equal(
            LogisticsStockPriority.Critical,
            FindStockPolicy(
                entities,
                supplyDepot,
                ResourceIds.Fuel).Priority);
        Assert.Equal(
            LogisticsStockPriority.High,
            FindStockPolicy(
                entities,
                supplyDepot,
                ResourceIds.Ammunition).Priority);
        Assert.Equal(
            LogisticsStockPriority.High,
            FindStockPolicy(
                entities,
                vehicleFactory,
                ResourceIds.Fuel).Priority);
    }

    private static EntityId FindOwnedBuilding(
        EntityRegistry entities,
        PlayerId owner,
        BuildingId buildingId)
    {
        foreach (EntityId entity in entities.Query<CompletedBuilding>())
        {
            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(entity);

            if (building.Owner == owner &&
                building.BuildingId == buildingId)
            {
                return entity;
            }
        }

        return EntityId.Invalid;
    }

    private static LogisticsStockPolicy FindStockPolicy(
        EntityRegistry entities,
        EntityId target,
        ResourceId resource)
    {
        Assert.True(target.IsValid);

        foreach (EntityId entity in entities.Query<LogisticsStockPolicy>())
        {
            LogisticsStockPolicy policy =
                entities.GetComponent<LogisticsStockPolicy>(entity);

            if (policy.TargetEntity == target &&
                policy.ResourceId == resource)
            {
                return policy;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"No stock policy found for target {target} and resource {resource}.");
    }
}
