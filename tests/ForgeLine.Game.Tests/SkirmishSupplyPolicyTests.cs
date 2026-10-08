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
    [InlineData(false)]
    [InlineData(true)]
    public void UnaffordableForwardRetreatWaitsForPhysicalMobileResupply(bool reconnaissance)
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
                entities.SetComponent(controller, state with { DeepOffensiveCommitted = true });
            }
        }

        var catalog = DirectorateContent.CreateUnitCatalog();
        EntityId unit = scenario.UnitFactory.Create(
            catalog[reconnaissance ? UnitIds.ScoutVehicle : UnitIds.MainBattleTank],
            home + new Vector3(700.0f, 0.0f, 0.0f), owner);
        UnitFuelState fuel = entities.GetComponent<UnitFuelState>(unit);
        double initialFuel = fuel.Capacity * 0.05;
        double removed = scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) - initialFuel;
        Assert.True(scenario.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, removed).Succeeded);

        scenario.Simulation.RunTicks(22, TestContext.Current.CancellationToken);

        Assert.Equal(CombatOrderKind.HoldPosition, entities.GetComponent<CombatOrderState>(unit).Kind);
        Assert.Equal(initialFuel, scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), precision: 6);
        Assert.False(entities.HasComponent<MovementGroupMember>(unit));

        Vector3 position = entities.GetComponent<WorldTransform>(unit).Position;
        EntityId truck = scenario.UnitFactory.Create(catalog[UnitIds.SupplyTruck],
            position + new Vector3(15.0f, 0.0f, 0.0f), owner);
        SupplyTruck supply = entities.GetComponent<SupplyTruck>(truck);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Fuel, supply.FuelTarget).Succeeded);
        Assert.True(scenario.Inventories.Add(supply.InventoryId, ResourceIds.Ammunition, supply.AmmunitionTarget).Succeeded);

        scenario.Simulation.RunTicks(100, TestContext.Current.CancellationToken);

        Assert.True(scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) > initialFuel);
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
            home + new Vector3(700.0f, 0.0f, 0.0f), owner);
        UnitFuelState fuel = entities.GetComponent<UnitFuelState>(unit);
        double quantity = scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        Assert.True(scenario.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, quantity * 0.8).Succeeded);

        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        CombatOrderState initial = entities.GetComponent<CombatOrderState>(unit);
        Assert.Equal(CombatOrderKind.Retreat, initial.Kind);

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

        Assert.Equal(supply.FuelTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), precision: 6);
        Assert.Equal(supply.AmmunitionTarget, scenario.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition), precision: 6);
        Assert.Equal(600.0 - supply.FuelTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Fuel), precision: 6);
        Assert.Equal(800.0 - supply.AmmunitionTarget, scenario.Inventories.GetQuantity(inventory, ResourceIds.Ammunition), precision: 6);
        Assert.False(
            entities.HasComponent<MovementOrder>(
                truck));
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
    public void ReconnaissanceAdvanceSharesRouteWithPhysicalSupplyEscort(
        bool unrelatedEnemyDetected,
        bool scoutRecovered)
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
                    new Vector3(unrelatedEnemyDetected || scoutRecovered ? 600.0f : 30.0f, 0.0f, 0.0f),
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
}
