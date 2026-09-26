using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
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
}
