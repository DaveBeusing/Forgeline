using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class BootstrapInfantryPlanningTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RequiredInfantryPrecedesOptionalGrowthAndIdleRefills(bool coreBuilt, bool infantryMissing, bool explicitOrder)
    {
        var settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        var owner = new PlayerId(1);
        var configuration = settings.OpponentConfigurations[owner.Value];
        using var scenario = CentralDivideScenario.Create(settings, seed: 2026);
        SkirmishScenarioHarness.CompleteResearchForPolicyFixture(scenario, owner);
        var entities = scenario.Simulation.Entities;
        var home = entities.GetComponent<WorldTransform>(scenario.GetBase(owner).CommandCore);
        for (int index = 0; index < 3; index++)
        {
            var stock = scenario.Inventories.CreateInventory(new InventorySpecification(2500));
            var depot = entities.CreateEntity();
            entities.AddComponent(depot, home with { Position = home.Position + new Vector3(index * 40, 0, 80) });
            entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, owner, SimulationTick.Zero));
            entities.AddComponent(depot, new SupplyDepot(stock, owner));
        }
        Assert.True(configuration.ResolveMatureSupplyTruckTarget(3) > configuration.MinimumSupplyTrucks);
        var units = DirectorateContent.CreateUnitCatalog();
        Ensure(UnitIds.CargoTruck, Math.Max(configuration.MinimumCargoTrucks, 3));
        Ensure(UnitIds.SupplyTruck, configuration.MinimumSupplyTrucks);
        Ensure(UnitIds.ScoutVehicle, 1);
        Ensure(UnitIds.MainBattleTank, configuration.MinimumObjectivePressureUnits);
        if (coreBuilt) Ensure(UnitIds.MobileArtillery, 1);

        int initial = Math.Max(1, configuration.MinimumAttackUnits - configuration.MinimumObjectivePressureUnits);
        if (configuration.Raids.Enabled) initial = Math.Max(initial, configuration.Raids.MinimumUnits + configuration.Raids.HomeDefenders);
        Assert.True(initial < 6);
        Ensure(UnitIds.RifleSquad, initial - (infantryMissing ? 1 : 0));
        Ensure(UnitIds.CombatEngineer, 1);
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(4000));
        var factory = entities.CreateEntity();
        entities.AddComponent(factory, home);
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.Barracks, owner, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(owner, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new UnitProductionFacility(input, UnitProductionCapability.Infantry, owner, Vector3.Zero, SimulationTick.Zero));
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10003)));
        entities.AddComponent(factory, new PowerGenerator(10));
        entities.AddComponent(factory, new PowerConsumer(1, PowerPriority.Industrial, enabled: true));
        if (explicitOrder)
            scenario.Simulation.SubmitCommand(new QueueUnitProductionCommand(owner, factory, UnitIds.RifleSquad, SimulationTick.Zero), SimulationTick.Zero.Next());
        scenario.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        bool needsInfantry = coreBuilt || infantryMissing || explicitOrder;
        bool queuedInfantry = false;
        foreach (var e in entities.Query<UnitProductionRequest>())
        { var request = entities.GetComponent<UnitProductionRequest>(e); queuedInfantry |= request.Facility == factory && request.UnitId == UnitIds.RifleSquad; }
        Assert.Equal(needsInfantry, queuedInfantry);
        foreach (var resource in new[] { ResourceIds.Steel, ResourceIds.Electronics, ResourceIds.Fuel, ResourceIds.Ammunition })
        {
            LogisticsStockPolicy policy = default;
            bool found = false;
            foreach (var e in entities.Query<LogisticsStockPolicy>())
            { var p = entities.GetComponent<LogisticsStockPolicy>(e); if (p.TargetEntity == factory && p.ResourceId == resource) { Assert.False(found); policy = p; found = true; } }
            Assert.True(found);
            if (needsInfantry) { Assert.True(policy.Enabled); Assert.True(policy.DesiredMinimum > 0); }
            else { Assert.False(policy.Enabled); Assert.Equal(0, policy.DesiredMinimum); }
            Assert.Equal(0, scenario.Inventories.GetQuantity(input, resource));
        }
        void Ensure(UnitId id, int count)
        {
            while (scenario.CountUnits(owner, id) < count)
                scenario.UnitFactory.Create(units[id], home.Position + new Vector3(200, 0, scenario.CountUnits(owner, id) * 20), owner);
        }
    }
}
