using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishMatureSupplyPlanningTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AdditionalSupplyGrowthRequiresTheActualCoreForce(bool artilleryBuilt, bool minimumSupplyMissing)
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
        Ensure(UnitIds.SupplyTruck, configuration.MinimumSupplyTrucks - (minimumSupplyMissing ? 1 : 0));
        Ensure(UnitIds.ScoutVehicle, 1);
        Ensure(UnitIds.MainBattleTank, configuration.MinimumObjectivePressureUnits);
        if (artilleryBuilt) Ensure(UnitIds.MobileArtillery, 1);
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(4000));
        var factory = entities.CreateEntity();
        var request = entities.CreateEntity();
        entities.AddComponent(request, new UnitProductionRequest(factory, UnitIds.MobileArtillery, ProductionPriority.Normal, SimulationTick.Zero));
        entities.AddComponent(factory, home);
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, owner, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(owner, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new UnitProductionFacility(input, UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
            owner, Vector3.Zero, SimulationTick.Zero)
        {
            ActiveRequest = request, ActiveUnit = UnitIds.MobileArtillery, Status = UnitProductionStatus.NoInput
        });
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10003)));
        entities.AddComponent(factory, new PowerGenerator(10));
        entities.AddComponent(factory, new PowerConsumer(1, PowerPriority.Industrial, enabled: true));
        scenario.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        bool criticalSupply = false;
        bool anySupply = false;
        foreach (var entity in entities.Query<UnitProductionRequest>())
        {
            var queued = entities.GetComponent<UnitProductionRequest>(entity);
            anySupply |= queued.Facility == factory && queued.UnitId == UnitIds.SupplyTruck;
            criticalSupply |= queued.Facility == factory && queued.UnitId == UnitIds.SupplyTruck && queued.Priority == ProductionPriority.High;
        }
        Assert.Equal(artilleryBuilt || minimumSupplyMissing, criticalSupply);
        Assert.Equal(artilleryBuilt || minimumSupplyMissing, anySupply);
        Assert.Equal(artilleryBuilt || minimumSupplyMissing ? UnitIds.SupplyTruck : UnitIds.MobileArtillery,
            entities.GetComponent<UnitProductionFacility>(factory).ActiveUnit);
        Assert.Equal(0, scenario.Inventories.GetQuantity(input, ResourceIds.Steel));

        void Ensure(UnitId id, int count)
        {
            while (scenario.CountUnits(owner, id) < count)
                scenario.UnitFactory.Create(units[id], home.Position + new Vector3(200, 0, scenario.CountUnits(owner, id) * 20), owner);
        }
    }
}
