using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishConstructionFundingTests
{
    [Theory]
    [InlineData(false, false, false, 0, true)]
    [InlineData(true, false, false, 0, false)]
    [InlineData(true, true, false, 0, true)]
    [InlineData(true, false, true, 0, true)]
    [InlineData(true, false, false, 1, true)]
    [InlineData(true, false, false, 2, true)]
    [InlineData(true, false, false, 3, true)]
    [InlineData(true, false, false, 4, true)]
    [InlineData(true, false, false, 5, true)]
    [InlineData(true, false, false, 6, true)]
    public void RadarCanUseOnlySurplusBeyondTheActiveInfantryInputs(bool active, bool reserved, bool surplus, int claimState, bool canFund)
    {
        var entities = new EntityRegistry();
        var inventories = new InventoryStore();
        var units = DirectorateContent.CreateUnitCatalog();
        var radar = InitialBuildingDefinitions.CreateCatalog()[BuildingIds.Radar];
        var infantry = units[UnitIds.RifleSquad];
        var inventory = inventories.CreateInventory(new InventorySpecification(2000));
        foreach (var cost in radar.Costs) Assert.True(inventories.Add(inventory, cost.ResourceId, cost.Quantity).Succeeded);
        if (reserved || surplus)
            foreach (var cost in infantry.Costs) Assert.True(inventories.Add(inventory, cost.ResourceId, cost.Quantity).Succeeded);
        if (reserved)
            foreach (var cost in infantry.Costs) Assert.True(inventories.Reserve(inventory, cost.ResourceId, cost.Quantity).Succeeded);
        var source = entities.CreateEntity();
        var request = entities.CreateEntity();
        entities.AddComponent(request, new UnitProductionRequest(claimState == 4 ? entities.CreateEntity() : source,
            claimState == 5 ? UnitIds.CargoTruck : UnitIds.RifleSquad, ProductionPriority.Normal, SimulationTick.Zero, paused: claimState == 1));
        entities.AddComponent(source, new UnitProductionFacility(claimState == 6 ? inventories.CreateInventory(new InventorySpecification(2000)) : inventory, UnitProductionCapability.Infantry, new(1), Vector3.Zero, SimulationTick.Zero)
        {
            ActiveRequest = active ? request : EntityId.Invalid, ActiveUnit = UnitIds.RifleSquad,
            InputsReserved = reserved, Status = reserved ? UnitProductionStatus.Running : UnitProductionStatus.NoInput
        });
        if (claimState == 2) entities.AddComponent(request, new UnitProductionCancellationRequest());
        if (claimState == 3) Assert.True(entities.DestroyEntity(request));
        double steel = inventories.GetAvailableQuantity(inventory, ResourceIds.Steel);
        double electronics = inventories.GetAvailableQuantity(inventory, ResourceIds.Electronics);
        Assert.Equal(canFund, SkirmishConstructionFunding.CanPay(entities, inventories, units, source, inventory, radar));
        Assert.Equal(steel, inventories.GetAvailableQuantity(inventory, ResourceIds.Steel));
        Assert.Equal(electronics, inventories.GetAvailableQuantity(inventory, ResourceIds.Electronics));
    }
}
