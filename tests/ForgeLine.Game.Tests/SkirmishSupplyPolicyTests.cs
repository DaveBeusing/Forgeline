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
    [Theory]
    [InlineData(100, 80, false, true)]
    [InlineData(55, 30, false, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(54, 80, false, false)]
    [InlineData(100, 29, false, false)]
    [InlineData(100, 80, true, false)]
    public void ExhaustedLoadCompletesWithDispatchableCargoWhenNoSourceCanFillTheRemainder(
        double fuel, double ammunition, bool stockedAlternative, bool completed)
    {
        using var scenario = CentralDivideScenario.Create(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new PlayerId(1));
        Vector3 position = entities.GetComponent<WorldTransform>(side.CommandCore).Position + Vector3.UnitX * 200;
        var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(4_000));
        EntityId depot = entities.CreateEntity();
        entities.AddComponent(depot, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, side.Player, SimulationTick.Zero));
        entities.AddComponent(depot, new ControllableEntity(side.Player, ControllableEntityCategory.Building));
        entities.AddComponent(depot, new InventoryStorage(inventory));
        entities.AddComponent(depot, new SupplyDepot(inventory, side.Player));
        entities.AddComponent(depot, Assert.IsType<BuildingLoadingDock>(DirectorateContent.CreateBuildingCatalog()[BuildingIds.SupplyDepot].LoadingDock));
        if (!stockedAlternative)
            Assert.True(scenario.Inventories.Remove(side.StartingInventory, ResourceIds.Ammunition,
                scenario.Inventories.GetAvailableQuantity(side.StartingInventory, ResourceIds.Ammunition)).Succeeded);
        EntityId truck = scenario.UnitFactory.Create(DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck],
            position + Vector3.UnitZ * 30, side.Player);
        var supply = entities.GetComponent<SupplyTruck>(truck);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Fuel, fuel).Succeeded);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Ammunition, ammunition).Succeeded);
        entities.AddComponent(truck, new SupplyLoadingOrder(depot, SimulationTick.Zero,
            VehicleDockBlockReason.SourceEmpty, 1_000));
        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.Equal(!completed, entities.HasComponent<SupplyLoadingOrder>(truck));
        Assert.Equal(fuel, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(ammunition, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition));
        if (stockedAlternative)
            Assert.Equal(side.CommandCore, entities.GetComponent<SupplyLoadingOrder>(truck).Source);
        if (completed)
        {
            Assert.False(entities.HasComponent<VehicleDockVisit>(truck));
            Assert.False(entities.HasComponent<VehicleQueueEntry>(truck));
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void StrategicCadenceReconsidersAnEmptySourceEvenWhenSupplyRetriesFallBetweenDecisions(
        bool sourceEmpty, bool retryDue, bool coreStocked)
    {
        using var scenario = CentralDivideScenario.Create(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new PlayerId(1));
        var position = entities.GetComponent<WorldTransform>(side.CommandCore).Position + Vector3.UnitX * 200;
        var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(4_000));
        EntityId depot = entities.CreateEntity();
        entities.AddComponent(depot, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, side.Player, SimulationTick.Zero));
        entities.AddComponent(depot, new ControllableEntity(side.Player, ControllableEntityCategory.Building));
        entities.AddComponent(depot, new InventoryStorage(inventory));
        entities.AddComponent(depot, new SupplyDepot(inventory, side.Player));
        entities.AddComponent(depot, Assert.IsType<BuildingLoadingDock>(DirectorateContent.CreateBuildingCatalog()[BuildingIds.SupplyDepot].LoadingDock));
        if (!coreStocked)
            foreach (var resource in new[] { ResourceIds.Fuel, ResourceIds.Ammunition })
                Assert.True(scenario.Inventories.Remove(side.StartingInventory, resource,
                    scenario.Inventories.GetAvailableQuantity(side.StartingInventory, resource)).Succeeded);
        EntityId truck = scenario.UnitFactory.Create(DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck],
            position + Vector3.UnitZ * 30, side.Player);
        var supply = entities.GetComponent<SupplyTruck>(truck);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Fuel, 100).Succeeded);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Ammunition, 80).Succeeded);
        var original = new SupplyLoadingOrder(depot, SimulationTick.Zero,
            sourceEmpty ? VehicleDockBlockReason.SourceEmpty : VehicleDockBlockReason.TargetFull,
            sourceEmpty ? (retryDue ? 0UL : 2UL) : 1_000UL);
        entities.AddComponent(truck, original);
        scenario.Simulation.RunTicks(52, TestContext.Current.CancellationToken);
        if (sourceEmpty && !coreStocked)
        {
            Assert.False(entities.HasComponent<SupplyLoadingOrder>(truck));
            Assert.False(entities.HasComponent<VehicleDockVisit>(truck));
            Assert.False(entities.HasComponent<VehicleQueueEntry>(truck));
        }
        else
        {
            var current = entities.GetComponent<SupplyLoadingOrder>(truck);
            Assert.Equal(sourceEmpty && coreStocked ? side.CommandCore : depot, current.Source);
            if (current.Source == side.CommandCore) Assert.Equal(VehicleDockBlockReason.None, current.WaitReason);
        }
        Assert.Equal(100, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(80, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition));
    }

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    public void SupplyLoadingPrefersAStockedSourceBeforeDistanceAndRetainsPartialFallback(
        bool depotHasAmmunition, bool coreHasAmmunition, bool truckHasAmmunition, bool useCore)
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new PlayerId(1));
        var position = entities.GetComponent<WorldTransform>(side.CommandCore).Position + Vector3.UnitX * 200;
        if (!coreHasAmmunition)
            Assert.True(scenario.Inventories.Remove(side.StartingInventory, ResourceIds.Ammunition,
                scenario.Inventories.GetAvailableQuantity(side.StartingInventory, ResourceIds.Ammunition)).Succeeded);
        var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(4_000));
        EntityId depot = entities.CreateEntity();
        entities.AddComponent(depot, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, side.Player, SimulationTick.Zero));
        entities.AddComponent(depot, new ControllableEntity(side.Player, ControllableEntityCategory.Building));
        entities.AddComponent(depot, new InventoryStorage(inventory));
        entities.AddComponent(depot, new SupplyDepot(inventory, side.Player));
        entities.AddComponent(depot, Assert.IsType<BuildingLoadingDock>(DirectorateContent.CreateBuildingCatalog()[BuildingIds.SupplyDepot].LoadingDock));
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck];
        Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Fuel, definition.SupplyFuelTarget).Succeeded);
        if (depotHasAmmunition)
            Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Ammunition, definition.SupplyAmmunitionTarget).Succeeded);
        EntityId truck = scenario.UnitFactory.Create(definition, position + Vector3.UnitZ * 30, side.Player);
        if (truckHasAmmunition)
            Assert.True(scenario.Inventories.Add(entities.GetComponent<SupplyTruck>(truck).InventoryId,
                ResourceIds.Ammunition, definition.SupplyAmmunitionTarget).Succeeded);
        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.Equal(useCore ? side.CommandCore : depot, entities.GetComponent<SupplyLoadingOrder>(truck).Source);
    }
    [Fact]
    public void ExpansionFundingKeepsPartialHubCostsAheadOfOptionalExtractors()
    {
        using var scenario = CentralDivideScenario.Create(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation), seed: 2026);
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new PlayerId(2));
        bool funded = scenario.RunUntil(current => SkirmishScenarioHarness.HasExpansionFundingPrerequisites(current, side.Player),
            24_000, TestContext.Current.CancellationToken);
        var queueDiagnostics = new List<string>();
        if (!funded)
            foreach (var truck in entities.Query<VehicleDockVisit, WorldTransform>())
                queueDiagnostics.Add($"{truck}: {entities.GetComponent<WorldTransform>(truck).Position}; {entities.GetComponent<VehicleDockVisit>(truck)}; " +
                    (entities.TryGetComponent(truck, out VehicleQueueEntry entry) ? entry.ToString() : "unregistered"));
        Assert.True(funded, string.Join("\n", queueDiagnostics));
        void SetAvailable(ResourceId resource, double quantity)
        {
            double current = scenario.Inventories.GetAvailableQuantity(side.StartingInventory, resource);
            if (current < quantity) Assert.True(scenario.Inventories.Add(side.StartingInventory, resource, quantity - current).Succeeded);
            else if (current > quantity) Assert.True(scenario.Inventories.Remove(side.StartingInventory, resource, current - quantity).Succeeded);
        }
        List<ConstructionSite> Sites()
        {
            var result = new List<ConstructionSite>();
            foreach (var entity in entities.Query<ConstructionSite>())
            {
                var site = entities.GetComponent<ConstructionSite>(entity);
                if (site.Owner == side.Player) result.Add(site);
            }
            return result;
        }
        var costs = scenario.Services.BuildingDefinitions[BuildingIds.LogisticsHub].Costs;
        double oreRequired = costs.Single(cost => cost.ResourceId == ResourceIds.FerrousOre).Quantity;
        double partial = scenario.Services.BuildingDefinitions[BuildingIds.Extractor].Costs.Single(cost => cost.ResourceId == ResourceIds.FerrousOre).Quantity;
        Assert.True(partial < oreRequired);
        foreach (var cost in costs) SetAvailable(cost.ResourceId, cost.ResourceId == ResourceIds.FerrousOre ? partial : cost.Quantity);
        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.Empty(Sites());
        Assert.Equal(LogisticsStockPriority.Critical, FindStockPolicy(entities, side.CommandCore, ResourceIds.FerrousOre).Priority);
        Assert.Equal(LogisticsStockPriority.High, FindStockPolicy(entities, side.CommandCore, ResourceIds.Silicates).Priority);
        Assert.Equal(LogisticsStockPriority.High, FindStockPolicy(entities, side.CommandCore, ResourceIds.Volatiles).Priority);
        Assert.InRange(scenario.Inventories.GetAvailableQuantity(side.StartingInventory, ResourceIds.FerrousOre), partial, oreRequired - 1);
        SetAvailable(ResourceIds.FerrousOre, oreRequired);
        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.Contains(Sites(), site => site.BuildingId == BuildingIds.LogisticsHub && site.SourceInventory == side.StartingInventory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PowerRecoveryKeepsPartialTruckloadsInsteadOfBuildingMoreUnpoweredExtractors(bool alreadyUnderpowered)
    {
        var settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        using var scenario = CentralDivideScenario.Create(settings with
        {
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(settings.OpponentConfigurations)
            {
                [1] = settings.OpponentConfigurations[1] with { ReactionCadenceTicks = 1 }
            }
        });
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new ForgeLine.Game.PlayerId(1));
        var catalog = scenario.Services.BuildingDefinitions;
        double partial = catalog[BuildingIds.Extractor].Costs.Single(cost => cost.ResourceId == ResourceIds.FerrousOre).Quantity;
        double required = catalog[BuildingIds.PowerPlant].Costs.Single(cost => cost.ResourceId == ResourceIds.FerrousOre).Quantity;
        Assert.True(partial < required);
        Assert.True(scenario.Inventories.Remove(side.StartingInventory, ResourceIds.FerrousOre,
            scenario.Inventories.GetQuantity(side.StartingInventory, ResourceIds.FerrousOre)).Succeeded);
        Assert.True(scenario.Inventories.Add(side.StartingInventory, ResourceIds.FerrousOre, partial).Succeeded);
        var membership = entities.GetComponent<PowerNetworkMembership>(side.CommandCore);
        var competingStock = scenario.Inventories.CreateInventory(new InventorySpecification(1_000));
        var store = entities.CreateEntity();
        entities.AddComponent(store, entities.GetComponent<WorldTransform>(side.CommandCore) with
            { Position = entities.GetComponent<WorldTransform>(side.CommandCore).Position + new Vector3(0, 0, 100) });
        entities.AddComponent(store, new CompletedBuilding(BuildingIds.StorageDepot, side.Player, SimulationTick.Zero));
        entities.AddComponent(store, new InventoryStorage(competingStock));
        entities.AddComponent(store, new StorageDepot(competingStock, side.Faction));
        var policy = entities.CreateEntity();
        entities.AddComponent(policy, new LogisticsStockPolicy(store, ResourceIds.FerrousOre, required, required, required * 2,
            LogisticsStockPriority.Critical));
        foreach (var id in new[] { BuildingIds.PowerPlant, BuildingIds.Radar })
        {
            var entity = entities.CreateEntity();
            entities.AddComponent(entity, entities.GetComponent<WorldTransform>(side.CommandCore));
            entities.AddComponent(entity, new CompletedBuilding(id, side.Player, SimulationTick.Zero));
            entities.AddComponent(entity, membership);
            if (id == BuildingIds.PowerPlant) entities.AddComponent(entity, new PowerGenerator(catalog[id].PowerGeneration));
            else entities.AddComponent(entity, new PowerConsumer(alreadyUnderpowered ? 100_000 :
                catalog[BuildingIds.PowerPlant].PowerGeneration - catalog[BuildingIds.CommandCore].PowerDemand - 9,
                PowerPriority.Industrial));
        }
        scenario.Simulation.RunTicks(4, TestContext.Current.CancellationToken);
        if (!alreadyUnderpowered)
        {
            var economy = scenario.Opponents.DebugSnapshot.Single(item => item.Player == side.Player).Economy;
            Assert.Equal(9, economy.PowerGeneration - economy.PowerDemand);
            Assert.Equal(0, economy.OfflineConsumers);
        }
        Assert.Equal(partial, scenario.Inventories.GetQuantity(side.StartingInventory, ResourceIds.FerrousOre));
        var sites = new List<ConstructionSite>();
        foreach (EntityId entity in entities.Query<ConstructionSite>())
            if (entities.GetComponent<ConstructionSite>(entity).Owner == side.Player)
                sites.Add(entities.GetComponent<ConstructionSite>(entity));
        Assert.Empty(sites);
        Assert.True(scenario.Inventories.Add(side.StartingInventory, ResourceIds.FerrousOre, required - partial).Succeeded);
        scenario.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        foreach (EntityId entity in entities.Query<ConstructionSite>())
            if (entities.GetComponent<ConstructionSite>(entity).Owner == side.Player)
                sites.Add(entities.GetComponent<ConstructionSite>(entity));
        Assert.Contains(sites, site => site.BuildingId == BuildingIds.PowerPlant);
        Assert.Equal(0, scenario.Inventories.GetAvailableQuantity(side.StartingInventory, ResourceIds.FerrousOre));
        Assert.Equal(required, scenario.Inventories.GetReservedQuantity(side.StartingInventory, ResourceIds.FerrousOre));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SatisfiedUnitCostDoesNotKeepOptionalStockBufferCritical(bool queuedSuccessor)
    {
        var settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        using var scenario = CentralDivideScenario.Create(settings with
        {
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(settings.OpponentConfigurations)
            {
                [1] = settings.OpponentConfigurations[1] with { ReactionCadenceTicks = 1, MaximumQueuedUnitsPerFacility = 1 }
            }
        });
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new ForgeLine.Game.PlayerId(1));
        var input = scenario.Inventories.CreateInventory(new InventorySpecification(4_000));
        var tank = DirectorateContent.CreateUnitCatalog()[UnitIds.MainBattleTank];
        foreach (UnitResourceCost cost in tank.Costs)
            if (cost.ResourceId != ResourceIds.Steel)
                Assert.True(scenario.Inventories.Add(input, cost.ResourceId, cost.Quantity).Succeeded);
        EntityId factory = entities.CreateEntity();
        entities.AddComponent(factory, entities.GetComponent<WorldTransform>(side.CommandCore));
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, side.Player, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(side.Player, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new InventoryStorage(input));
        entities.AddComponent(factory, new UnitProductionFacility(input, UnitProductionCapability.Vehicle,
            side.Player, Vector3.UnitZ * 32, SimulationTick.Zero));
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10_001)));
        entities.AddComponent(factory, new PowerGenerator(10));
        entities.AddComponent(factory, new PowerConsumer(1, PowerPriority.Industrial));
        var queue = new QueueUnitProductionCommand(side.Player, factory, UnitIds.MainBattleTank, SimulationTick.Zero, ProductionPriority.High);
        scenario.Simulation.SubmitCommand(queue, SimulationTick.Zero.Next());
        QueueUnitProductionCommand? next = null;
        if (queuedSuccessor)
        {
            next = new QueueUnitProductionCommand(side.Player, factory, UnitIds.ScoutVehicle, SimulationTick.Zero, ProductionPriority.Normal);
            scenario.Simulation.SubmitCommand(next, SimulationTick.Zero.Next());
        }
        scenario.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        Assert.True(queue.Accepted);
        if (next is not null) Assert.True(next.Accepted);
        Assert.Equal(UnitProductionStatus.NoInput, entities.GetComponent<UnitProductionFacility>(factory).Status);
        Assert.Equal(UnitIds.MainBattleTank, entities.GetComponent<UnitProductionFacility>(factory).ActiveUnit);
        LogisticsStockPolicy ammunition = FindStockPolicy(entities, factory, ResourceIds.Ammunition);
        Assert.Equal(queuedSuccessor ? LogisticsStockPriority.Critical : LogisticsStockPriority.High, ammunition.Priority);
        Assert.Equal(queuedSuccessor ? 60 : 50, ammunition.DesiredMinimum);
        Assert.Equal(120, ammunition.DesiredTarget);
        Assert.Equal(240, ammunition.DesiredMaximum);
        Assert.Equal(LogisticsStockPriority.Critical, FindStockPolicy(entities, factory, ResourceIds.Steel).Priority);
        Assert.Equal(20, scenario.Inventories.GetQuantity(input, ResourceIds.Ammunition));
    }
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void UnaffordableForwardRetreatWaitsForPhysicalMobileResupply(bool reconnaissance, bool partialRecovery, bool deepOffensive)
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        var owner = scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player;
        Vector3 home = entities.GetComponent<WorldTransform>(scenario.GetBase(owner).CommandCore).Position;
        foreach (EntityId controller in entities.Query<SkirmishOpponentController, SkirmishOpponentState>())
        {
            if (entities.GetComponent<SkirmishOpponentController>(controller).Player == owner)
            {
                var state = entities.GetComponent<SkirmishOpponentState>(controller);
                entities.SetComponent(controller, state with { DeepOffensiveCommitted = deepOffensive });
            }
        }

        var catalog = DirectorateContent.CreateUnitCatalog();
        EntityId unit = scenario.UnitFactory.Create(
            catalog[reconnaissance ? UnitIds.ScoutVehicle : UnitIds.MainBattleTank],
            home + new Vector3(700.0f, 0.0f, 0.0f), owner);
        UnitFuelState fuel = entities.GetComponent<UnitFuelState>(unit);
        double initialFuel = fuel.Capacity * (partialRecovery ? 0.30 : 0.05);
        double removed = scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) - initialFuel;
        Assert.True(scenario.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, removed).Succeeded);
        if (partialRecovery)
        {
            scenario.Simulation.SubmitCommand(new HoldPositionCommand(owner, [unit], SimulationTick.Zero),
                SimulationTick.Zero.Next());
        }

        scenario.Simulation.RunTicks(22, TestContext.Current.CancellationToken);

        Assert.Equal(CombatOrderKind.HoldPosition, entities.GetComponent<CombatOrderState>(unit).Kind);
        Assert.Equal(initialFuel, scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), precision: 6);
        Assert.False(entities.HasComponent<MovementGroupMember>(unit));

        Vector3 position = entities.GetComponent<WorldTransform>(unit).Position;
        EntityId truck = scenario.UnitFactory.Create(catalog[UnitIds.SupplyTruck],
            position + new Vector3(partialRecovery ? 80.0f : 15.0f, 0.0f, 0.0f), owner);
        SupplyTruck supply = entities.GetComponent<SupplyTruck>(truck);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Fuel, supply.FuelTarget).Succeeded);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Ammunition, supply.AmmunitionTarget).Succeeded);

        scenario.Simulation.RunTicks(partialRecovery ? 400UL : 100UL, TestContext.Current.CancellationToken);

        Assert.True(scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) > initialFuel,
            $"fuel={scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel)}; " +
            $"policy={entities.GetComponent<AutomaticResupplyPolicy>(unit)}; " +
            $"resupply={entities.HasComponent<ResupplyOrder>(unit)}; " +
            $"position={entities.GetComponent<WorldTransform>(unit).Position}; " +
            $"provider={entities.GetComponent<WorldTransform>(truck).Position}");
        Assert.True(scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel) < supply.FuelTarget);
    }

    [Fact]
    public void ContinuingRecoveryPreservesAcceptedRetreatOrder()
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        var owner = scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player;
        Vector3 home = entities.GetComponent<WorldTransform>(scenario.GetBase(owner).CommandCore).Position;
        EntityId unit = scenario.UnitFactory.Create(
            DirectorateContent.CreateUnitCatalog()[UnitIds.RifleSquad],
            home + new Vector3(160.0f, 0.0f, 0.0f), owner);
        UnitFuelState fuel = entities.GetComponent<UnitFuelState>(unit);
        double quantity = scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        Assert.True(scenario.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, quantity * 0.8).Succeeded);
        Assert.True(scenario.Inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) >= 160 * fuel.ConsumptionPerMeter);
        scenario.Simulation.SubmitCommand(new RetreatCommand(owner, [unit], home,
            SimulationTick.Zero, FormationTemplate.Column), SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        CombatOrderState initial = entities.GetComponent<CombatOrderState>(unit);
        Assert.Equal(CombatOrderKind.Retreat, initial.Kind);
        Assert.Equal(1UL, initial.AcceptedAtTick.Value);

        scenario.Simulation.RunTicks(10, TestContext.Current.CancellationToken);

        Assert.Equal(initial.AcceptedAtTick,
            entities.GetComponent<CombatOrderState>(unit).AcceptedAtTick);
    }

    [Fact]
    public void EmptySupplyTruckAvoidsBlockedLoadingFaceAndLoadsPhysicalStock()
    {
        MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        var entities = scenario.Simulation.Entities;
        InventoryId inventory = scenario.Inventories.CreateInventory(new InventorySpecification(2_500.0));
        Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Fuel, 600.0).Succeeded);
        Assert.True(scenario.Inventories.Add(inventory, ResourceIds.Ammunition, 800.0).Succeeded);
        EntityId depot = entities.CreateEntity();
        entities.AddComponent(depot, new WorldTransform(new Vector3(420.0f, 0.0f, 1800.0f), Quaternion.Identity, Vector3.One));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player, SimulationTick.Zero));
        entities.AddComponent(depot, new BuildingLoadingDock(new(0, 0, 12.5f), Vector3.UnitX, 18));
        entities.AddComponent(depot, new SupplyDepot(inventory, scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        entities.AddComponent(depot, new SpatialPresence(new Vector3(8.0f, 4.0f, 8.0f),
            new SpatialEntryMetadata(scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value, 0, SpatialMobility.Static)));

        EntityId blockedFace = entities.CreateEntity();
        entities.AddComponent(
            blockedFace,
            new WorldTransform(
                new Vector3(401.0f, 0.0f, 1800.0f),
                Quaternion.Identity,
                Vector3.One));
        entities.AddComponent(
            blockedFace,
            new SpatialPresence(
                new Vector3(6.0f, 4.0f, 6.0f),
                new SpatialEntryMetadata(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value,
                    0,
                    SpatialMobility.Static)));

        EntityId truck = scenario.UnitFactory.Create(
            DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck],
            new Vector3(360.0f, 0.0f, 1800.0f), scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SupplyTruck supply = entities.GetComponent<SupplyTruck>(truck);

        scenario.Simulation.RunTicks(600, TestContext.Current.CancellationToken);

        Assert.True(scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel) > 0, $"pose={entities.GetComponent<WorldTransform>(truck)}; dock={DockState(entities, truck)}; operational={entities.HasComponent<ResupplyOrder>(truck)}; load={entities.HasComponent<SupplyLoadingOrder>(truck)}");
        Assert.Equal(supply.FuelTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), precision: 6);
        Assert.Equal(supply.AmmunitionTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition), precision: 6);
        Assert.Equal(600.0 - supply.FuelTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Fuel), precision: 6);
        Assert.Equal(800.0 - supply.AmmunitionTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Ammunition), precision: 6);
        if (entities.HasComponent<MovementOrder>(truck))
        {
            var exiting = entities.GetComponent<VehicleDockVisit>(truck);
            Assert.Equal(VehicleDockStatus.Exiting, exiting.Status);
            Assert.Equal(truck, entities.GetComponent<VehicleDockSlot>(depot).Truck);
        }
        else Assert.False(entities.HasComponent<SupplyLoadingOrder>(truck));
        Assert.False(
            entities.HasComponent<NavigationPendingPath>(
                truck));
        Assert.False(
            entities.HasComponent<NavigationRouteState>(
                truck));
    }

    [Fact]
    public void CombatRecoveryDoesNotCancelSupplyTruckLoadingMovement()
    {
        MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;

        InventoryId depotInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(2_500.0));
        Assert.True(
            scenario.Inventories.Add(
                depotInventory,
                ResourceIds.Fuel,
                600.0).Succeeded);
        Assert.True(
            scenario.Inventories.Add(
                depotInventory,
                ResourceIds.Ammunition,
                800.0).Succeeded);

        EntityId depot = entities.CreateEntity();
        entities.AddComponent(
            depot,
            new WorldTransform(
                new Vector3(420.0f, 0.0f, 1800.0f),
                Quaternion.Identity,
                Vector3.One));
        entities.AddComponent(
            depot,
            new CompletedBuilding(
                BuildingIds.SupplyDepot,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(depot, new BuildingLoadingDock(new(0, 0, 12.5f), Vector3.UnitX, 18));
        entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        entities.AddComponent(
            depot,
            new SpatialPresence(
                new Vector3(8.0f, 4.0f, 8.0f),
                new SpatialEntryMetadata(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value,
                    0,
                    SpatialMobility.Static)));

        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        EntityId recoveryUnit =
            scenario.UnitFactory.Create(
                units[UnitIds.RifleSquad],
                new Vector3(500.0f, 0.0f, 1800.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        UnitFuelState recoveryFuel =
            entities.GetComponent<UnitFuelState>(
                recoveryUnit);
        double initialRecoveryFuel =
            scenario.Inventories.GetQuantity(
                recoveryFuel.InventoryId,
                ResourceIds.Fuel);
        Assert.True(
            scenario.Inventories.Remove(
                recoveryFuel.InventoryId,
                ResourceIds.Fuel,
                initialRecoveryFuel).Succeeded);

        EntityId truck =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                new Vector3(360.0f, 0.0f, 1800.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SupplyTruck supply =
            entities.GetComponent<SupplyTruck>(
                truck);

        scenario.Simulation.RunTicks(
            600,
            TestContext.Current.CancellationToken);

        double truckFuel =
            scenario.Inventories.GetQuantity(
                supply.InventoryId,
                ResourceIds.Fuel);
        double recoveredFuel =
            scenario.Inventories.GetQuantity(
                recoveryFuel.InventoryId,
                ResourceIds.Fuel);
        double remainingDepotFuel =
            scenario.Inventories.GetQuantity(
                depotInventory,
                ResourceIds.Fuel);

        Assert.True(
            remainingDepotFuel < 600.0,
            "The Supply Truck never reached a loading source while combat recovery was active.");
        Assert.True(
            truckFuel > 0.0 ||
            recoveredFuel > 0.0,
            "Loaded Fuel was neither retained by the Supply Truck nor delivered to the recovering combat unit.");
    }

    [Fact]
    public void FieldSupplyTruckPreservesFrontlineAvailabilityBeforeSelfRefuel()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        EntityId cargo =
            scenario.UnitFactory.Create(
                units[UnitIds.CargoTruck],
                core.Position +
                    new Vector3(20.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        EntityId supply =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(30.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        EntityId combat =
            scenario.UnitFactory.Create(
                units[UnitIds.RifleSquad],
                core.Position +
                    new Vector3(40.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        EntityId scout =
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                core.Position +
                    new Vector3(50.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);

        scenario.Simulation.RunTicks(
            25,
            TestContext.Current.CancellationToken);

        AutomaticResupplyPolicy cargoPolicy =
            entities.GetComponent<AutomaticResupplyPolicy>(
                cargo);
        AutomaticResupplyPolicy supplyPolicy =
            entities.GetComponent<AutomaticResupplyPolicy>(
                supply);
        AutomaticResupplyPolicy combatPolicy =
            entities.GetComponent<AutomaticResupplyPolicy>(
                combat);
        AutomaticResupplyPolicy scoutPolicy =
            entities.GetComponent<AutomaticResupplyPolicy>(
                scout);

        Assert.Equal(
            0.55,
            cargoPolicy.FuelThreshold,
            precision: 6);
        Assert.Equal(
            0.35,
            supplyPolicy.FuelThreshold,
            precision: 6);
        Assert.True(
            supplyPolicy.FuelThreshold <
            cargoPolicy.FuelThreshold);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.OpponentConfigurations[1].OffensiveFuelThreshold,
            combatPolicy.FuelThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.OpponentConfigurations[1].ResupplyThreshold,
            combatPolicy.AmmunitionThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.OpponentConfigurations[1].ResupplyThreshold,
            scoutPolicy.FuelThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.OpponentConfigurations[1].ResupplyThreshold,
            scoutPolicy.AmmunitionThreshold,
            precision: 6);
        Assert.True(
            scoutPolicy.FuelThreshold <
            combatPolicy.FuelThreshold);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, false, false, true)]
    public void ReconnaissanceAdvanceSharesRouteWithPhysicalSupplyEscort(
        bool unrelatedEnemyDetected,
        bool scoutRecovered,
        bool scoutAdvanceCompleted = false,
        bool scoutBetweenWaypoints = false,
        bool escortSeparated = false)
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        EntityId scout =
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                core.Position +
                    new Vector3(unrelatedEnemyDetected || scoutRecovered || escortSeparated ? 600.0f : 30.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        EntityId supply =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(unrelatedEnemyDetected || scoutRecovered ? 605.0f : 35.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SupplyTruck supplyState =
            entities.GetComponent<SupplyTruck>(
                supply);
        if (unrelatedEnemyDetected)
        {
            scenario.UnitFactory.Create(units[UnitIds.RifleSquad],
                core.Position + new Vector3(900.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player);
        }
        if (scoutRecovered)
        {
            scenario.Simulation.SubmitCommand(new RetreatCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [scout], core.Position, SimulationTick.Zero, FormationTemplate.Column),
                SimulationTick.Zero.Next());
        }
        if (scoutAdvanceCompleted)
        {
            Vector3 scoutPosition = entities.GetComponent<WorldTransform>(scout).Position;
            scenario.Simulation.SubmitCommand(new AttackMoveCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [scout], scoutPosition, SimulationTick.Zero, FormationTemplate.Column),
                SimulationTick.Zero.Next());
        }

        Assert.True(
            scenario.Inventories.Add(
                supplyState.InventoryId,
                ResourceIds.Fuel,
                supplyState.FuelTarget).Succeeded);
        Assert.True(
            scenario.Inventories.Add(
                supplyState.InventoryId,
                ResourceIds.Ammunition,
                supplyState.AmmunitionTarget).Succeeded);

        scenario.Simulation.RunTicks(
            30,
            TestContext.Current.CancellationToken);

        if (escortSeparated)
        {
            Assert.False(entities.HasComponent<MovementGroupMember>(scout));
            Vector3 waitingPosition = entities.GetComponent<WorldTransform>(scout).Position;
            Assert.InRange(Vector2.Distance(new Vector2(core.Position.X + 600.0f, core.Position.Z),
                new Vector2(waitingPosition.X, waitingPosition.Z)), 0.0f, 3.0f);
            Assert.Equal(GroundMovementStatus.Moving,
                entities.GetComponent<GroundMovementState>(supply).Status);
            for (int tick = 0; tick < 2_000 && !entities.HasComponent<MovementGroupMember>(scout); tick++)
            {
                scenario.Simulation.RunTicks(1, TestContext.Current.CancellationToken);
            }
            scenario.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        }

        Assert.True(
            entities.TryGetComponent(
                scout,
                out CombatOrderState scoutOrder) &&
            scoutOrder.Kind ==
                CombatOrderKind.AttackMove);

        MovementGroupMember scoutGroup =
            entities.GetComponent<MovementGroupMember>(
                scout);
        MovementGroupMember supplyGroup =
            entities.GetComponent<MovementGroupMember>(
                supply);

        Assert.Equal(
            scoutGroup.Group,
            supplyGroup.Group);
        Assert.True(scoutGroup.Group.IsValid);
        Vector3 opposingCore = entities.GetComponent<WorldTransform>(
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore).Position;
        float identificationRange = units[UnitIds.ScoutVehicle].RadarIdentificationRangeMeters;
        Assert.InRange(Vector2.Distance(new Vector2(scoutOrder.Destination.X, scoutOrder.Destination.Z),
            new Vector2(opposingCore.X, opposingCore.Z)), 0.0f, identificationRange);
        Assert.Equal(GroundMovementStatus.Moving,
            entities.GetComponent<GroundMovementState>(supply).Status);
        if (scoutBetweenWaypoints)
        {
            // Arriving at a formation slot clears the local order before
            // the shared route advances to its next waypoint.
            entities.RemoveComponent<MovementOrder>(scout);
            scenario.Simulation.RunTicks(1, TestContext.Current.CancellationToken);
            Assert.Equal(scoutGroup.Group,
                entities.GetComponent<MovementGroupMember>(scout).Group);
        }
        if (unrelatedEnemyDetected)
        {
            Assert.Contains(scenario.Intelligence.Capture(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Faction).Contacts,
                contact => contact.IsCurrent);
        }
    }

    [Fact]
    public void PartiallyLoadedSupplyTruckReturnsForOffensiveReserve()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        EntityId truck =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(120.0f, 0.0f, 0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SupplyTruck supply =
            entities.GetComponent<SupplyTruck>(
                truck);

        Assert.True(
            scenario.Inventories.Add(
                supply.InventoryId,
                ResourceIds.Fuel,
                supply.FuelTarget * 0.40).Succeeded);
        Assert.True(
            scenario.Inventories.Add(
                supply.InventoryId,
                ResourceIds.Ammunition,
                supply.AmmunitionTarget * 0.30).Succeeded);

        double initialFuel =
            scenario.Inventories.GetQuantity(
                supply.InventoryId,
                ResourceIds.Fuel);
        Vector3 staleForwardTarget =
            core.Position +
            new Vector3(500.0f, 0.0f, 0.0f);

        entities.AddComponent(
            truck,
            new MovementOrder(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                staleForwardTarget,
                scenario.Simulation.CurrentTick,
                scenario.Simulation.CurrentTick));

        scenario.Simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        double currentFuel =
            scenario.Inventories.GetQuantity(
                supply.InventoryId,
                ResourceIds.Fuel);

        bool loadedFuel =
            currentFuel > initialFuel;
        bool retaskedToLoadingSource =
            entities.TryGetComponent(
                truck,
                out MovementOrder loadingOrder) &&
            loadingOrder.WorldTarget !=
                staleForwardTarget &&
            Vector3.DistanceSquared(
                loadingOrder.WorldTarget,
                core.Position) <
            Vector3.DistanceSquared(
                staleForwardTarget,
                core.Position);

        Assert.True(
            loadedFuel ||
            retaskedToLoadingSource,
            "A Supply Truck below the offensive Fuel reserve retained unrelated forward movement instead of returning to a loading source.");
    }


    [Fact]
    public void SupplyTruckRetargetsFromRemoteDepotToCloserCommandCore()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        SupplyProvider coreProvider =
            entities.GetComponent<SupplyProvider>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        Assert.True(
            scenario.Inventories.GetAvailableQuantity(
                coreProvider.InventoryId,
                ResourceIds.Fuel) >
            0.0);

        InventoryId remoteInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    2_500.0));
        Assert.True(
            scenario.Inventories.Add(
                remoteInventory,
                ResourceIds.Fuel,
                600.0).Succeeded);

        Vector3 remotePosition =
            core.Position +
            new Vector3(
                700.0f,
                0.0f,
                0.0f);
        EntityId remoteDepot =
            entities.CreateEntity();
        entities.AddComponent(
            remoteDepot,
            new WorldTransform(
                remotePosition,
                Quaternion.Identity,
                Vector3.One));
        entities.AddComponent(
            remoteDepot,
            new CompletedBuilding(
                BuildingIds.SupplyDepot,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            remoteDepot,
            new SupplyDepot(
                remoteInventory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));

        EntityId truck =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(
                        120.0f,
                        0.0f,
                        0.0f),
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SupplyTruck supply =
            entities.GetComponent<SupplyTruck>(
                truck);

        Vector3 remoteLoadingTarget =
            remotePosition;
        entities.AddComponent(
            truck,
            new MovementOrder(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                remoteLoadingTarget,
                scenario.Simulation.CurrentTick,
                scenario.Simulation.CurrentTick));

        scenario.Simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        double loadedFuel =
            scenario.Inventories.GetQuantity(
                supply.InventoryId,
                ResourceIds.Fuel);

        bool retaskedTowardCore =
            entities.TryGetComponent(
                truck,
                out MovementOrder loadingOrder) &&
            Vector3.DistanceSquared(
                loadingOrder.WorldTarget,
                core.Position) <
            Vector3.DistanceSquared(
                remoteLoadingTarget,
                core.Position);

        Assert.True(
            loadedFuel > 0.0 ||
            retaskedTowardCore,
            "A depleted Supply Truck retained a farther loading route instead of using the closer stocked Command Core.");
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void FuelRecoveryPoliciesPrioritizeFieldSupplyAndLiveCargoRecovery(
        bool cargoLost,
        bool replacementQueued)
    {
        MatchScenarioSettings validation = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        using MatchRuntime scenario = CentralDivideScenario.Create(validation with
        {
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(validation.OpponentConfigurations)
            {
                [1] = validation.OpponentConfigurations[1] with { ReactionCadenceTicks = 1 }
            }
        });
        EntityRegistry entities = scenario.Simulation.Entities;
        WorldTransform transform =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        InventoryId refineryInput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        InventoryId refineryOutput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        EntityId refinery = entities.CreateEntity();
        entities.AddComponent(refinery, transform);
        entities.AddComponent(
            refinery,
            new CompletedBuilding(
                BuildingIds.Refinery,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            refinery,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            refinery,
            new ProductionFacility(
                refineryInput,
                refineryOutput,
                ProductionCapability.FuelProcessing,
                SimulationTick.Zero));

        InventoryId depotInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(2_500.0));
        EntityId supplyDepot = entities.CreateEntity();
        entities.AddComponent(supplyDepot, transform);
        entities.AddComponent(
            supplyDepot,
            new CompletedBuilding(
                BuildingIds.SupplyDepot,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            supplyDepot,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building |
                    ControllableEntityCategory.Logistics));
        entities.AddComponent(
            supplyDepot,
            new SupplyDepot(
                depotInventory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));

        InventoryId factoryInput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId vehicleFactory = entities.CreateEntity();
        entities.AddComponent(vehicleFactory, transform);
        entities.AddComponent(
            vehicleFactory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            vehicleFactory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            vehicleFactory,
            new UnitProductionFacility(
                factoryInput,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));

        if (cargoLost)
        {
            EntityId cargo = default;
            foreach (EntityId entity in entities.Query<ControllableEntity, UnitIdentity>())
            {
                if (entities.GetComponent<ControllableEntity>(entity).Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                    entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck)
                {
                    cargo = entity;
                    break;
                }
            }
            Assert.True(entities.DestroyEntity(cargo));
        }

        QueueUnitProductionCommand? replacement = null;
        if (replacementQueued)
        {
            var network = new PowerNetworkId(10_001);
            entities.AddComponent(vehicleFactory, new PowerNetworkMembership(network));
            entities.AddComponent(vehicleFactory, new PowerGenerator(10.0));
            entities.AddComponent(vehicleFactory, new PowerConsumer(1.0, PowerPriority.Industrial, enabled: true));
            replacement = new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                vehicleFactory,
                UnitIds.CargoTruck,
                SimulationTick.Zero,
                ProductionPriority.High);
            scenario.Simulation.SubmitCommand(replacement, SimulationTick.Zero.Next());
        }

        scenario.Simulation.RunTicks(replacementQueued ? 2UL : 1UL, TestContext.Current.CancellationToken);
        if (replacement is not null)
        {
            Assert.True(replacement.Accepted);
            bool queued = false;
            foreach (EntityId entity in entities.Query<UnitProductionRequest>())
            {
                UnitProductionRequest request = entities.GetComponent<UnitProductionRequest>(entity);
                queued |= request.Facility == vehicleFactory && request.UnitId == UnitIds.CargoTruck;
            }
            Assert.True(queued);
        }

        LogisticsStockPolicy coreSteel =
            FindStockPolicy(
                entities,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                ResourceIds.Steel);

        Assert.Equal(
            220.0,
            coreSteel.DesiredMinimum,
            precision: 6);
        Assert.Equal(
            500.0,
            coreSteel.DesiredTarget,
            precision: 6);

        LogisticsStockPriority refineryVolatilesPriority =
            FindStockPolicy(
                entities,
                refinery,
                ResourceIds.Volatiles).Priority;

        Assert.True(
            refineryVolatilesPriority is
                LogisticsStockPriority.High or
                LogisticsStockPriority.Critical,
            "Fuel processing input must remain elevated while field supply recovery stays Critical.");
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
        LogisticsStockPolicy factoryFuel =
            FindStockPolicy(
                entities,
                vehicleFactory,
                ResourceIds.Fuel);

        Assert.Equal(
            cargoLost || replacementQueued ? LogisticsStockPriority.Critical : LogisticsStockPriority.High,
            factoryFuel.Priority);
        Assert.Equal(
            cargoLost ? 360.0 : 240.0,
            factoryFuel.DesiredMinimum,
            precision: 6);
        Assert.Equal(
            cargoLost ? 360.0 : 240.0,
            factoryFuel.DesiredTarget,
            precision: 6);
        foreach (ResourceId resource in new[] { ResourceIds.Steel, ResourceIds.Electronics })
        {
            Assert.Equal(cargoLost || replacementQueued ? LogisticsStockPriority.Critical : LogisticsStockPriority.High,
                FindStockPolicy(entities, vehicleFactory, resource).Priority);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SteelInputRecoveryDoesNotRetainConstructionStockWhenProductionIsBlocked(
        bool outputStockSatisfied)
    {
        MatchScenarioSettings validation = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        using MatchRuntime scenario = CentralDivideScenario.Create(validation with
        {
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(validation.OpponentConfigurations)
            {
                [1] = validation.OpponentConfigurations[1] with { ReactionCadenceTicks = 1 }
            }
        });
        EntityRegistry entities = scenario.Simulation.Entities;
        var player = scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player;
        InventoryId input = scenario.Inventories.CreateInventory(new InventorySpecification(1_000.0));
        InventoryId output = scenario.Inventories.CreateInventory(new InventorySpecification(1_000.0));
        if (outputStockSatisfied)
        {
            Assert.True(scenario.Inventories.Add(output, ResourceIds.Steel, 600.0).Succeeded);
        }
        EntityId smelter = entities.CreateEntity();
        entities.AddComponent(smelter, entities.GetComponent<WorldTransform>(scenario.GetBase(player).CommandCore));
        entities.AddComponent(smelter, new CompletedBuilding(BuildingIds.Smelter, player, SimulationTick.Zero));
        entities.AddComponent(smelter, new ControllableEntity(player, ControllableEntityCategory.Building));
        entities.AddComponent(smelter, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
        entities.AddComponent(smelter, new PowerNetworkMembership(new PowerNetworkId(10_002)));
        entities.AddComponent(smelter, new PowerGenerator(10.0));
        entities.AddComponent(smelter, new PowerConsumer(1.0, PowerPriority.Industrial, enabled: true));

        scenario.Simulation.RunTicks(2, TestContext.Current.CancellationToken);

        ProductionFacility facility = entities.GetComponent<ProductionFacility>(smelter);
        Assert.Equal(outputStockSatisfied ? ProductionStatus.Idle : ProductionStatus.NoInput, facility.Status);
        LogisticsStockPolicy policy = FindStockPolicy(entities, smelter, ResourceIds.FerrousOre);
        Assert.Equal(outputStockSatisfied ? LogisticsStockPriority.High : LogisticsStockPriority.Critical, policy.Priority);
        Assert.Equal(80.0, policy.DesiredMinimum);
        Assert.Equal(240.0, policy.DesiredTarget);
        Assert.Equal(650.0,
            FindStockPolicy(entities, scenario.GetBase(player).CommandCore, ResourceIds.FerrousOre).DesiredTarget);
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
    private static string DockState(EntityRegistry entities, EntityId truck) => entities.TryGetComponent(truck, out VehicleDockVisit dock) ? dock.ToString() : "none";
}
