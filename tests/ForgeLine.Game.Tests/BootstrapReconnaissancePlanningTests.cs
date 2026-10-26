using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BootstrapReconnaissancePlanningTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OptionalScoutGrowthWaitsForCoreForceButMissingScoutRecovers(bool coreBuilt, bool missingScout)
    {
        var settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        var owner = new PlayerId(1);
        var configuration = settings.OpponentConfigurations[owner.Value];
        using var scenario = CentralDivideScenario.Create(settings, seed: 2026);
        if (coreBuilt) SkirmishScenarioHarness.CompleteResearchForPolicyFixture(scenario, owner);
        else
        {
            var id = TechnologyIds.IndustrialStandardization;
            var complete = scenario.Simulation.Entities.CreateEntity();
            scenario.Simulation.Entities.AddComponent(complete, new CompletedTechnology(owner, id, SimulationTick.Zero));
            foreach (var capability in scenario.Services.TechnologyDefinitions[id].Unlocks)
            {
                var unlock = scenario.Simulation.Entities.CreateEntity();
                scenario.Simulation.Entities.AddComponent(unlock, new TechnologyCapabilityUnlock(owner, capability, id, SimulationTick.Zero));
            }
        }
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
        Ensure(UnitIds.CargoTruck, 4);
        Ensure(UnitIds.SupplyTruck, configuration.ResolveMatureSupplyTruckTarget(3));
        Ensure(UnitIds.ScoutVehicle, missingScout ? 0 : 1);
        Ensure(UnitIds.MainBattleTank, configuration.MinimumObjectivePressureUnits);
        if (coreBuilt) Ensure(UnitIds.MobileArtillery, 1);
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(4000));
        var factory = entities.CreateEntity();
        var request = entities.CreateEntity();
        entities.AddComponent(request, new UnitProductionRequest(factory, UnitIds.MainBattleTank, ProductionPriority.Normal, SimulationTick.Zero));
        entities.AddComponent(factory, home);
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, owner, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(owner, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new UnitProductionFacility(input, UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
            owner, Vector3.Zero, SimulationTick.Zero)
        {
            ActiveRequest = request,
            ActiveUnit = UnitIds.MainBattleTank,
            Status = UnitProductionStatus.NoInput
        });
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10003)));
        entities.AddComponent(factory, new PowerGenerator(10));
        entities.AddComponent(factory, new PowerConsumer(1, PowerPriority.Industrial, enabled: true));
        scenario.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        bool queuedScout = false;
        int tankRequests = 0;
        foreach (var entity in entities.Query<UnitProductionRequest>())
        {
            var queued = entities.GetComponent<UnitProductionRequest>(entity);
            queuedScout |= queued.Facility == factory && queued.UnitId == UnitIds.ScoutVehicle;
            if (queued.Facility == factory && queued.UnitId == UnitIds.MainBattleTank) tankRequests++;
        }
        Assert.Equal(coreBuilt || missingScout, queuedScout);
        if (!coreBuilt) Assert.Equal(missingScout ? 0 : 1, tankRequests);
        Assert.Equal(0, scenario.Inventories.GetQuantity(input, ResourceIds.Steel));

        void Ensure(UnitId id, int count)
        {
            while (scenario.CountUnits(owner, id) < count)
                scenario.UnitFactory.Create(units[id], home.Position + new Vector3(200, 0, scenario.CountUnits(owner, id) * 20), owner);
        }
    }
}
