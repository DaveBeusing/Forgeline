using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BattlefieldSupplyDebugVisualizationTests
{
    [Fact]
    public void ProviderRangeAndUnsuppliedUnitProduceDebugGeometry()
    {
        var inventories = new InventoryStore();
        var simulation = new SimulationCoordinator();
        var supply = new BattlefieldSupplySystem(inventories);
        simulation.RegisterSystem(supply);

        InventoryId depotInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));
        Assert.True(
            inventories.Add(
                depotInventory,
                ResourceIds.Fuel,
                25.0).Succeeded);

        EntityId depot = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                new Vector3(10.0f, 0.0f, 10.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                new PlayerId(1)));
        simulation.Entities.AddComponent(
            depot,
            new SupplyProvider(
                depotInventory,
                new PlayerId(1),
                resupplyRangeMeters: 12.0f));

        EntityId unit = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            unit,
            new WorldTransform(
                new Vector3(40.0f, 0.0f, 40.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            unit,
            new ControllableEntity(
                new PlayerId(1),
                ControllableEntityCategory.Unit));
        BattlefieldSupplyFactory.AttachUnitSupply(
            simulation.Entities,
            inventories,
            unit,
            fuelCapacity: 20.0,
            ammunitionCapacity: 10.0,
            fuelConsumptionPerMeter: 0.0);

        simulation.AdvanceOneTick();

        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        BattlefieldSupplyDebugVisualization.Draw(
            debugDraw,
            supply.LastDebugSnapshot);

        Assert.True(debugDraw.Lines.Length >= 24);
        Assert.Contains(
            debugDraw.Labels,
            label => label.Text.Contains("SUPPLY DEPOT", StringComparison.Ordinal));
        Assert.Contains(
            debugDraw.Labels,
            label => label.Text.Contains("Unsupplied", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptySnapshotProducesNoGeometry()
    {
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };

        BattlefieldSupplyDebugVisualization.Draw(
            debugDraw,
            BattlefieldSupplyDebugSnapshot.Empty);

        Assert.Empty(debugDraw.Labels);
        Assert.Equal(0, debugDraw.Lines.Length);
    }
}
