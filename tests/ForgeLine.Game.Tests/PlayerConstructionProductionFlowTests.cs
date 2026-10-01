using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PlayerConstructionProductionFlowTests
{
    [Fact]
    public void PlayerCommandsBuildProductionChainAndProduceInfantryVehicleAndSupportUnit()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4110);
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);

        BuildingId[] buildings =
        [
            BuildingIds.PowerPlant,
            BuildingIds.PowerPlant,
            BuildingIds.Smelter,
            BuildingIds.Barracks,
            BuildingIds.VehicleFactory
        ];

        foreach (BuildingId buildingId in buildings)
        {
            Vector3 position =
                FindOpenPlacement(
                    scenario,
                    buildingId);
            PlayerCommandSubmissionReceipt receipt =
                gateway.SubmitBuild(
                    scenario.West.Player,
                    buildingId,
                    position,
                    BuildingOrientation.North,
                    scenario.West.CommandCore,
                    scenario.Simulation.CurrentTick);

            Assert.True(receipt.Accepted);

            scenario.Simulation.AdvanceOneTick();

            Assert.True(
                gateway.Results.TryRead(
                    out PlayerCommandResultReadModel result));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                result.State);
        }

        uint longestConstruction =
            buildings
                .Select(
                    id =>
                        scenario.Services.BuildingDefinitions[id]
                            .ConstructionTicks)
                .Max();

        scenario.Simulation.RunTicks(
            longestConstruction + 2,
            TestContext.Current.CancellationToken);

        EntityId smelter =
            FindCompletedBuilding(
                scenario,
                BuildingIds.Smelter);
        EntityId barracks =
            FindCompletedBuilding(
                scenario,
                BuildingIds.Barracks);
        EntityId vehicleFactory =
            FindCompletedBuilding(
                scenario,
                BuildingIds.VehicleFactory);

        ProductionFacility processing =
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                smelter);
        UnitProductionFacility infantryFacility =
            scenario.Simulation.Entities.GetComponent<UnitProductionFacility>(
                barracks);
        UnitProductionFacility vehicleFacility =
            scenario.Simulation.Entities.GetComponent<UnitProductionFacility>(
                vehicleFactory);

        Assert.True(
            scenario.Inventories.Transfer(
                scenario.West.StartingInventory,
                processing.InputInventory,
                ResourceIds.FerrousOre,
                10.0).Succeeded);

        PlayerCommandSubmissionReceipt steelReceipt =
            gateway.SubmitProduction(
                scenario.West.Player,
                smelter,
                RecipeIds.Steel,
                scenario.Simulation.CurrentTick);

        Assert.True(steelReceipt.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel steelResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            steelResult.State);

        scenario.Simulation.RunTicks(
            scenario.Services.ProductionRecipes[RecipeIds.Steel]
                .DurationTicks + 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            10.0,
            scenario.Inventories.GetQuantity(
                processing.OutputInventory,
                ResourceIds.Steel));

        UnitDefinition rifle =
            scenario.Services.UnitDefinitions[UnitIds.RifleSquad];
        UnitDefinition scout =
            scenario.Services.UnitDefinitions[UnitIds.ScoutVehicle];
        UnitDefinition supply =
            scenario.Services.UnitDefinitions[UnitIds.SupplyTruck];

        Assert.True(
            scenario.Inventories.Transfer(
                processing.OutputInventory,
                infantryFacility.InputInventory,
                ResourceIds.Steel,
                10.0).Succeeded);
        TransferRemainingUnitCost(
            scenario,
            infantryFacility.InputInventory,
            rifle,
            ResourceIds.Steel,
            alreadyTransferred: 10.0);
        TransferUnitCost(
            scenario,
            vehicleFacility.InputInventory,
            scout);
        TransferUnitCost(
            scenario,
            vehicleFacility.InputInventory,
            supply);

        QueueUnit(
            scenario,
            gateway,
            barracks,
            UnitIds.RifleSquad);
        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.ScoutVehicle);
        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.SupplyTruck);

        scenario.Simulation.RunTicks(
            rifle.ProductionTicks +
            scout.ProductionTicks +
            supply.ProductionTicks +
            8,
            TestContext.Current.CancellationToken);

        Assert.True(
            CountOwnedUnits(
                scenario,
                UnitIds.RifleSquad) >=
            1);
        Assert.True(
            CountOwnedUnits(
                scenario,
                UnitIds.ScoutVehicle) >=
            1);
        Assert.True(
            CountOwnedUnits(
                scenario,
                UnitIds.SupplyTruck) >=
            1);
    }

    [Fact]
    public void PlayerPoliciesPhysicallyDeliverForwardSupplyAndRecoverUnit()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4113);
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);

        foreach (BuildingId buildingId in
                 new[]
                 {
                     BuildingIds.PowerPlant,
                     BuildingIds.SupplyDepot
                 })
        {
            PlayerCommandSubmissionReceipt receipt =
                gateway.SubmitBuild(
                    scenario.West.Player,
                    buildingId,
                    FindOpenPlacement(
                        scenario,
                        buildingId),
                    BuildingOrientation.North,
                    scenario.West.CommandCore,
                    scenario.Simulation.CurrentTick);

            Assert.True(receipt.Accepted);
            scenario.Simulation.AdvanceOneTick();
            Assert.True(
                gateway.Results.TryRead(
                    out PlayerCommandResultReadModel result));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                result.State);
        }

        uint constructionTicks =
            Math.Max(
                scenario.Services.BuildingDefinitions[
                    BuildingIds.PowerPlant].ConstructionTicks,
                scenario.Services.BuildingDefinitions[
                    BuildingIds.SupplyDepot].ConstructionTicks);

        scenario.Simulation.RunTicks(
            constructionTicks + 4,
            TestContext.Current.CancellationToken);

        EntityId supplyDepot =
            FindCompletedBuilding(
                scenario,
                BuildingIds.SupplyDepot);
        InventoryStorage depotStorage =
            scenario.Simulation.Entities
                .GetComponent<InventoryStorage>(
                    supplyDepot);

        double sourceFuelBefore =
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Fuel);
        double sourceAmmoBefore =
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Ammunition);

        foreach ((ResourceId Resource, double Minimum, double Target, double Maximum)
                 policy in
                 new[]
                 {
                     (ResourceIds.Fuel, 20.0, 60.0, 100.0),
                     (ResourceIds.Ammunition, 20.0, 60.0, 100.0)
                 })
        {
            PlayerCommandSubmissionReceipt receipt =
                gateway.SubmitLogisticsStockPolicy(
                    scenario.West.Player,
                    supplyDepot,
                    policy.Resource,
                    policy.Minimum,
                    policy.Target,
                    policy.Maximum,
                    LogisticsStockPriority.Critical,
                    enabled: true,
                    scenario.Simulation.CurrentTick);

            Assert.True(receipt.Accepted);
            scenario.Simulation.AdvanceOneTick();
            Assert.True(
                gateway.Results.TryRead(
                    out PlayerCommandResultReadModel result));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                result.State);
        }

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    depotStorage.InventoryId,
                    ResourceIds.Fuel) >= 60.0 &&
                scenario.Inventories.GetQuantity(
                    depotStorage.InventoryId,
                    ResourceIds.Ammunition) >= 60.0,
            maximumTicks: 4_000);

        Assert.True(
            scenario.AutomatedDistribution.Metrics
                .CompletedRequestCount > 0);
        Assert.True(
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Fuel) <
            sourceFuelBefore);
        Assert.True(
            scenario.Inventories.GetQuantity(
                scenario.West.StartingInventory,
                ResourceIds.Ammunition) <
            sourceAmmoBefore);

        SupplyProvider coreProvider =
            scenario.Simulation.Entities
                .GetComponent<SupplyProvider>(
                    scenario.West.CommandCore);
        scenario.Simulation.Entities.SetComponent(
            scenario.West.CommandCore,
            new SupplyProvider(
                coreProvider.InventoryId,
                coreProvider.Owner,
                coreProvider.ResupplyRangeMeters,
                enabled: false));

        EntityId recipient =
            scenario.West.StartingUnits[0];
        UnitFuelState fuelState =
            scenario.Simulation.Entities
                .GetComponent<UnitFuelState>(
                    recipient);
        AmmunitionState ammunitionState =
            scenario.Simulation.Entities
                .GetComponent<AmmunitionState>(
                    recipient);

        double initialFuel =
            scenario.Inventories.GetQuantity(
                fuelState.InventoryId,
                ResourceIds.Fuel);
        double retainedFuel =
            Math.Max(
                5.0,
                initialFuel * 0.35);
        Assert.True(
            scenario.Inventories.Remove(
                fuelState.InventoryId,
                ResourceIds.Fuel,
                initialFuel - retainedFuel).Succeeded);

        double initialAmmo =
            scenario.Inventories.GetQuantity(
                ammunitionState.InventoryId,
                ResourceIds.Ammunition);
        Assert.True(
            scenario.Inventories.Remove(
                ammunitionState.InventoryId,
                ResourceIds.Ammunition,
                initialAmmo).Succeeded);

        PlayerCommandSubmissionReceipt resupply =
            gateway.SubmitResupply(
                scenario.West.Player,
                recipient,
                scenario.Simulation.CurrentTick);

        Assert.True(resupply.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel resupplyResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            resupplyResult.State);

        Assert.True(
            scenario.Simulation.Entities.TryGetComponent(
                recipient,
                out ResupplyOrder order));
        Assert.Equal(
            supplyDepot,
            order.Provider);

        double fuelBeforeRecovery =
            scenario.Inventories.GetQuantity(
                fuelState.InventoryId,
                ResourceIds.Fuel);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    fuelState.InventoryId,
                    ResourceIds.Fuel) >
                    fuelBeforeRecovery &&
                scenario.Inventories.GetQuantity(
                    ammunitionState.InventoryId,
                    ResourceIds.Ammunition) >
                    0.0,
            maximumTicks: 3_000);

        Assert.True(
            scenario.BattlefieldSupply.Metrics
                .TotalFuelTransferred > 0.0);
        Assert.True(
            scenario.BattlefieldSupply.Metrics
                .TotalAmmunitionTransferred > 0.0);
    }

    [Fact]
    public void PlayerCommandsBuildSupplyAndConquerThroughNaturalCombat()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(4119);
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity,
                intelligence:
                    scenario.Intelligence,
                weapons:
                    scenario.Services.Weapons,
                artilleryWeapons:
                    scenario.Services.ArtilleryWeapons);
        scenario.Simulation.RegisterTickObserver(gateway);

        Assert.Equal(
            0,
            scenario.Simulation.Entities
                .GetComponentCount<SkirmishOpponentController>());

        WorldTransform westCore =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.West.CommandCore);
        WorldTransform eastCore =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.East.CommandCore);
        Vector3 towardWest =
            Vector3.Normalize(
                new Vector3(
                    westCore.Position.X -
                        eastCore.Position.X,
                    0.0f,
                    westCore.Position.Z -
                        eastCore.Position.Z));
        var built =
            new List<(BuildingId Id, Vector3 Position)>();

        foreach (BuildingId buildingId in
                 new[]
                 {
                     BuildingIds.PowerPlant,
                     BuildingIds.PowerPlant,
                     BuildingIds.Smelter
                 })
        {
            Vector3 position =
                FindOpenPlacement(
                    scenario,
                    buildingId);
            BuildPlayerBuilding(
                scenario,
                gateway,
                buildingId,
                position);
            built.Add((buildingId, position));
        }

        Vector3 vehicleFactoryPosition =
            FindOpenPlacement(
                scenario,
                BuildingIds.VehicleFactory);
        BuildPlayerBuilding(
            scenario,
            gateway,
            BuildingIds.VehicleFactory,
            vehicleFactoryPosition);
        built.Add(
            (BuildingIds.VehicleFactory,
             vehicleFactoryPosition));

        Vector3 supplyDepotPosition =
            FindOpenPlacement(
                scenario,
                BuildingIds.SupplyDepot);
        BuildPlayerBuilding(
            scenario,
            gateway,
            BuildingIds.SupplyDepot,
            supplyDepotPosition);
        built.Add(
            (BuildingIds.SupplyDepot,
             supplyDepotPosition));

        uint longestConstruction =
            built
                .Select(
                    item =>
                        scenario.Services.BuildingDefinitions[
                            item.Id].ConstructionTicks)
                .Max();

        scenario.Simulation.RunTicks(
            longestConstruction + 6,
            TestContext.Current.CancellationToken);

        EntityId smelter =
            FindCompletedBuilding(
                scenario,
                BuildingIds.Smelter);
        EntityId vehicleFactory =
            FindCompletedBuilding(
                scenario,
                BuildingIds.VehicleFactory);
        EntityId supplyDepot =
            FindCompletedBuilding(
                scenario,
                BuildingIds.SupplyDepot);
        InventoryStorage supplyStorage =
            scenario.Simulation.Entities
                .GetComponent<InventoryStorage>(
                    supplyDepot);

        SubmitStockPolicy(
            scenario,
            gateway,
            supplyDepot,
            ResourceIds.Fuel,
            minimum: 50.0,
            target: 100.0,
            maximum: 150.0);
        SubmitStockPolicy(
            scenario,
            gateway,
            supplyDepot,
            ResourceIds.Ammunition,
            minimum: 40.0,
            target: 80.0,
            maximum: 120.0);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    supplyStorage.InventoryId,
                    ResourceIds.Fuel) >= 100.0 &&
                scenario.Inventories.GetQuantity(
                    supplyStorage.InventoryId,
                    ResourceIds.Ammunition) >= 80.0,
            maximumTicks: 4_000);

        ProductionFacility steelFacility =
            scenario.Simulation.Entities
                .GetComponent<ProductionFacility>(
                    smelter);
        UnitProductionFacility vehicleProduction =
            scenario.Simulation.Entities
                .GetComponent<UnitProductionFacility>(
                    vehicleFactory);

        SubmitStockPolicy(
            scenario,
            gateway,
            smelter,
            ResourceIds.FerrousOre,
            minimum: 120.0,
            target: 200.0,
            maximum: 240.0);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    steelFacility.InputInventory,
                    ResourceIds.FerrousOre) >=
                200.0,
            maximumTicks: 5_000);

        PlayerCommandResultReadModel steelResult =
            DispatchAction(
                scenario,
                gateway,
                PlayerActionRequest.QueueProduction(
                    smelter,
                    RecipeIds.Steel,
                    ProductionPriority.High,
                    ProductionRequestMode.DesiredStock,
                    ResourceIds.Steel,
                    200.0));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            steelResult.State);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    steelFacility.OutputInventory,
                    ResourceIds.Steel) >=
                200.0,
            maximumTicks: 2_000);

        foreach ((ResourceId Resource, double Target) stock in
                 new[]
                 {
                     (ResourceIds.Steel, 570.0),
                     (ResourceIds.Electronics, 180.0),
                     (ResourceIds.Fuel, 410.0),
                     (ResourceIds.Ammunition, 90.0)
                 })
        {
            SubmitStockPolicy(
                scenario,
                gateway,
                vehicleFactory,
                stock.Resource,
                minimum:
                    stock.Target * 0.5,
                target:
                    stock.Target,
                maximum:
                    stock.Target + 80.0);
        }

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    vehicleProduction.InputInventory,
                    ResourceIds.Steel) >= 555.0 &&
                scenario.Inventories.GetQuantity(
                    vehicleProduction.InputInventory,
                    ResourceIds.Electronics) >= 172.0 &&
                scenario.Inventories.GetQuantity(
                    vehicleProduction.InputInventory,
                    ResourceIds.Fuel) >= 396.0 &&
                scenario.Inventories.GetQuantity(
                    vehicleProduction.InputInventory,
                    ResourceIds.Ammunition) >= 80.0,
            maximumTicks: 8_000);

        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.MainBattleTank);
        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.MainBattleTank);
        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.ScoutVehicle);

        scenario.Simulation.RunTicks(
            scenario.Services.UnitDefinitions[
                UnitIds.MainBattleTank].ProductionTicks * 2 +
            scenario.Services.UnitDefinitions[
                UnitIds.ScoutVehicle].ProductionTicks +
            12,
            TestContext.Current.CancellationToken);

        EntityId[] tanks =
            FindOwnedUnits(
                scenario,
                UnitIds.MainBattleTank);
        EntityId scout =
            Assert.Single(
                FindOwnedUnits(
                    scenario,
                    UnitIds.ScoutVehicle));

        Assert.Equal(2, tanks.Length);

        foreach (EntityId tank in tanks)
        {
            PlayerCommandResultReadModel policyResult =
                DispatchAction(
                    scenario,
                    gateway,
                    PlayerActionRequest.SetAutomaticResupplyPolicy(
                        tank,
                        fuelThreshold: 0.0,
                        ammunitionThreshold: 0.0,
                        enabled: false));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                policyResult.State);
        }

        QueueUnit(
            scenario,
            gateway,
            vehicleFactory,
            UnitIds.SupplyTruck);
        scenario.Simulation.RunTicks(
            scenario.Services.UnitDefinitions[
                UnitIds.SupplyTruck].ProductionTicks +
            8,
            TestContext.Current.CancellationToken);

        EntityId supplyTruck =
            Assert.Single(
                FindOwnedUnits(
                    scenario,
                    UnitIds.SupplyTruck));
        SupplyTruck supplyTruckState =
            scenario.Simulation.Entities
                .GetComponent<SupplyTruck>(
                    supplyTruck);
        Vector3 supplyTruckPosition =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    supplyTruck).Position;
        Vector3 supplyStagingDirection =
            Vector3.Normalize(
                new Vector3(
                    supplyTruckPosition.X -
                        supplyDepotPosition.X,
                    0.0f,
                    supplyTruckPosition.Z -
                        supplyDepotPosition.Z));
        Vector3 supplyTruckLoadPoint =
            supplyDepotPosition +
            supplyStagingDirection *
                (supplyTruckState.LoadRangeMeters - 1.0f);

        Assert.True(
            gateway.SubmitMovement(
                scenario.West.Player,
                [supplyTruck],
                supplyTruckLoadPoint,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        RunUntil(
            scenario,
            () =>
                Vector3.DistanceSquared(
                    scenario.Simulation.Entities
                        .GetComponent<WorldTransform>(
                            supplyTruck).Position,
                    supplyTruckLoadPoint) <=
                4.0f * 4.0f,
            maximumTicks: 1_200);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    supplyTruckState.InventoryId,
                    ResourceIds.Fuel) > 0.0 &&
                scenario.Inventories.GetQuantity(
                    supplyTruckState.InventoryId,
                    ResourceIds.Ammunition) > 0.0,
            maximumTicks: 1_200);

        Assert.True(
            gateway.SubmitMovement(
                scenario.West.Player,
                [scout],
                supplyTruckLoadPoint,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        RunUntil(
            scenario,
            () =>
                Vector3.DistanceSquared(
                    scenario.Simulation.Entities
                        .GetComponent<WorldTransform>(
                            scout).Position,
                    supplyTruckLoadPoint) <=
                4.0f * 4.0f,
            maximumTicks: 1_200);

        UnitFuelState scoutFuel =
            scenario.Simulation.Entities
                .GetComponent<UnitFuelState>(
                    scout);

        RunUntil(
            scenario,
            () =>
                scenario.Inventories.GetQuantity(
                    scoutFuel.InventoryId,
                    ResourceIds.Fuel) >=
                scoutFuel.Capacity - 0.001 &&
                scenario.Inventories.GetQuantity(
                    supplyTruckState.InventoryId,
                    ResourceIds.Fuel) >=
                supplyTruckState.FuelTarget - 0.001,
            maximumTicks: 1_200);

        RemoveStockPolicies(
            scenario,
            gateway,
            smelter,
            vehicleFactory);

        Vector3 reconnaissancePoint =
            eastCore.Position +
            towardWest * 180.0f;
        Assert.True(
            gateway.SubmitMovement(
                scenario.West.Player,
                [scout, supplyTruck],
                reconnaissancePoint,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        FactionId westFaction =
            new((uint)scenario.West.Player.Value);
        WorldTransform scoutBeforeRecon =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scout);
        float reconnaissanceDistance =
            Vector3.Distance(
                scoutBeforeRecon.Position,
                reconnaissancePoint);
        int reconnaissanceTickBudget =
            Math.Max(
                20_000,
                checked(
                    (int)MathF.Ceiling(
                        reconnaissanceDistance /
                        6.0f *
                        20.0f) +
                    1_600));

        RunUntil(
            scenario,
            () =>
                scenario.Intelligence
                    .IsEntityCurrentlyIdentified(
                        westFaction,
                        scenario.East.CommandCore),
            maximumTicks: reconnaissanceTickBudget);

        double ammunitionBeforeCombat =
            tanks.Sum(
                tank =>
                {
                    AmmunitionState ammunition =
                        scenario.Simulation.Entities
                            .GetComponent<AmmunitionState>(
                                tank);
                    return scenario.Inventories.GetQuantity(
                        ammunition.InventoryId,
                        ResourceIds.Ammunition);
                });

        Vector3 assaultStagingPoint =
            eastCore.Position +
            towardWest * 135.0f;

        Assert.True(
            gateway.SubmitMovement(
                scenario.West.Player,
                tanks,
                assaultStagingPoint,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Line).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        RunUntil(
            scenario,
            () =>
                tanks.All(
                    tank =>
                        Vector3.DistanceSquared(
                            scenario.Simulation.Entities
                                .GetComponent<WorldTransform>(
                                    tank).Position,
                            assaultStagingPoint) <=
                        42.0f * 42.0f),
            maximumTicks: 3_000);

        Vector3 supplyTruckStagingPoint =
            assaultStagingPoint +
            towardWest *
                (supplyTruckState.ResupplyRangeMeters + 60.0f);
        Assert.True(
            gateway.SubmitMovement(
                scenario.West.Player,
                [supplyTruck],
                supplyTruckStagingPoint,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        RunUntil(
            scenario,
            () =>
                Vector3.DistanceSquared(
                    scenario.Simulation.Entities
                        .GetComponent<WorldTransform>(
                            supplyTruck).Position,
                    supplyTruckStagingPoint) <=
                6.0f * 6.0f,
            maximumTicks: 3_000);

        var fuelBeforeResupply =
            new Dictionary<EntityId, double>();

        foreach (EntityId tank in tanks)
        {
            UnitFuelState fuel =
                scenario.Simulation.Entities
                    .GetComponent<UnitFuelState>(
                        tank);
            double currentFuel =
                scenario.Inventories.GetQuantity(
                    fuel.InventoryId,
                    ResourceIds.Fuel);
            Assert.True(currentFuel < fuel.Capacity);
            fuelBeforeResupply[tank] = currentFuel;

            PlayerCommandResultReadModel resupplyResult =
                DispatchAction(
                    scenario,
                    gateway,
                    PlayerActionRequest.RequestResupply(
                        tank));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                resupplyResult.State);
        }

        RunUntil(
            scenario,
            () =>
                tanks.All(
                    tank =>
                    {
                        UnitFuelState fuel =
                            scenario.Simulation.Entities
                                .GetComponent<UnitFuelState>(
                                    tank);
                        return scenario.Inventories.GetQuantity(
                                   fuel.InventoryId,
                                   ResourceIds.Fuel) >
                               fuelBeforeResupply[tank];
                    }),
            maximumTicks: 2_000);

        PlayerCommandResultReadModel attackResult =
            DispatchAction(
                scenario,
                gateway,
                PlayerActionRequest.Attack(
                    tanks,
                    scenario.East.CommandCore));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            attackResult.State);

        RunUntil(
            scenario,
            () =>
                scenario.GetMatchState().IsTerminal,
            maximumTicks: 2_000);

        MatchState match =
            scenario.GetMatchState();

        Assert.Equal(
            MatchStatus.Victory,
            match.Status);
        Assert.Equal(
            scenario.West.Player,
            match.Winner);
        Assert.False(
            scenario.Simulation.Entities.IsAlive(
                scenario.East.CommandCore));

        double ammunitionAfterCombat =
            tanks
                .Where(
                    tank =>
                        scenario.Simulation.Entities.IsAlive(
                            tank))
                .Sum(
                    tank =>
                    {
                        AmmunitionState ammunition =
                            scenario.Simulation.Entities
                                .GetComponent<AmmunitionState>(
                                    tank);
                        return scenario.Inventories.GetQuantity(
                            ammunition.InventoryId,
                            ResourceIds.Ammunition);
                    });

        Assert.True(
            ammunitionAfterCombat <
            ammunitionBeforeCombat);
        Assert.True(
            scenario.AutomatedDistribution.Metrics
                .CompletedRequestCount > 0);
        Assert.True(
            scenario.BattlefieldSupply.Metrics
                .TotalFuelTransferred > 0.0);
    }

    private static PlayerCommandResultReadModel DispatchAction(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        in PlayerActionRequest request)
    {
        Assert.True(
            PlayerActionRequestDispatcher.TryDispatch(
                request,
                scenario.West.Player,
                gateway,
                scenario.Simulation.CurrentTick,
                out PlayerCommandSubmissionReceipt receipt));
        Assert.True(receipt.Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));

        return result;
    }

    private static void BuildPlayerBuilding(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        BuildingId buildingId,
        Vector3 position)
    {
        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitBuild(
                scenario.West.Player,
                buildingId,
                position,
                BuildingOrientation.North,
                scenario.West.CommandCore,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
    }

    private static void RemoveStockPolicies(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        params EntityId[] targets)
    {
        var policies =
            new List<EntityId>();

        foreach (EntityId policyEntity in
                 scenario.Simulation.Entities.Query<LogisticsStockPolicy>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy policy =
                scenario.Simulation.Entities
                    .GetComponent<LogisticsStockPolicy>(
                        policyEntity);

            if (targets.Contains(
                    policy.TargetEntity))
            {
                policies.Add(
                    policyEntity);
            }
        }

        foreach (EntityId policyEntity in policies)
        {
            PlayerCommandResultReadModel result =
                DispatchAction(
                    scenario,
                    gateway,
                    PlayerActionRequest.RemoveStockPolicy(
                        policyEntity));
            Assert.Equal(
                PlayerCommandFeedbackState.Accepted,
                result.State);
        }
    }

    private static void SubmitStockPolicy(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        EntityId targetEntity,
        ResourceId resource,
        double minimum,
        double target,
        double maximum)
    {
        PlayerCommandResultReadModel result =
            DispatchAction(
                scenario,
                gateway,
                PlayerActionRequest.SetStockPolicy(
                    targetEntity,
                    resource,
                    minimum,
                    target,
                    maximum,
                    LogisticsStockPriority.Critical,
                    enabled: true));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
    }

    private static EntityId[] FindOwnedUnits(
        VerticalSliceScenario scenario,
        UnitId unitId)
    {
        var result =
            new List<EntityId>();

        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<
                     UnitIdentity,
                     ControllableEntity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            UnitIdentity identity =
                scenario.Simulation.Entities
                    .GetComponent<UnitIdentity>(
                        entity);
            ControllableEntity controllable =
                scenario.Simulation.Entities
                    .GetComponent<ControllableEntity>(
                        entity);

            if (identity.UnitId == unitId &&
                controllable.Owner ==
                    scenario.West.Player)
            {
                result.Add(entity);
            }
        }

        return result.ToArray();
    }

    private static Vector3 FindOpenPlacementNear(
        VerticalSliceScenario scenario,
        BuildingId buildingId,
        Vector3 center)
    {
        for (int radius = 0;
             radius <= 10;
             radius++)
        {
            for (int z = -radius;
                 z <= radius;
                 z++)
            {
                for (int x = -radius;
                     x <= radius;
                     x++)
                {
                    if (radius > 0 &&
                        Math.Abs(x) != radius &&
                        Math.Abs(z) != radius)
                    {
                        continue;
                    }

                    Vector3 candidate =
                        center +
                        new Vector3(
                            x * 32.0f,
                            0.0f,
                            z * 32.0f);
                    BuildingPlacementPreview preview =
                        scenario.Services.BuildingPlacement
                            .CreatePreview(
                                scenario.Simulation.Entities,
                                scenario.West.Player,
                                buildingId,
                                candidate,
                                BuildingOrientation.North);

                    if (preview.IsValid)
                    {
                        return preview.GroundPosition;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"No valid placement was found near {center} for building '{buildingId}'.");
    }

    private static void QueueUnit(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        EntityId facility,
        UnitId unitId)
    {
        PlayerCommandResultReadModel result =
            DispatchAction(
                scenario,
                gateway,
                PlayerActionRequest.QueueUnitProduction(
                    facility,
                    unitId,
                    ProductionPriority.Normal));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
    }

    private static void TransferUnitCost(
        VerticalSliceScenario scenario,
        InventoryId destination,
        UnitDefinition definition)
    {
        foreach (UnitResourceCost cost in definition.Costs)
        {
            Assert.True(
                scenario.Inventories.Transfer(
                    scenario.West.StartingInventory,
                    destination,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }
    }

    private static void TransferRemainingUnitCost(
        VerticalSliceScenario scenario,
        InventoryId destination,
        UnitDefinition definition,
        ResourceId partiallyTransferredResource,
        double alreadyTransferred)
    {
        foreach (UnitResourceCost cost in definition.Costs)
        {
            double quantity =
                cost.ResourceId ==
                partiallyTransferredResource
                    ? cost.Quantity -
                      alreadyTransferred
                    : cost.Quantity;

            if (quantity <= 0.0)
            {
                continue;
            }

            Assert.True(
                scenario.Inventories.Transfer(
                    scenario.West.StartingInventory,
                    destination,
                    cost.ResourceId,
                    quantity).Succeeded);
        }
    }

    private static Vector3 FindOpenPlacement(
        VerticalSliceScenario scenario,
        BuildingId buildingId)
    {
        WorldTransform commandCore =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

        for (int radius = 2;
             radius <= 9;
             radius++)
        {
            for (int z = -radius;
                 z <= radius;
                 z++)
            {
                for (int x = -radius;
                     x <= radius;
                     x++)
                {
                    if (Math.Abs(x) != radius &&
                        Math.Abs(z) != radius)
                    {
                        continue;
                    }

                    Vector3 candidate =
                        commandCore.Position +
                        new Vector3(
                            x * 36.0f,
                            0.0f,
                            z * 36.0f);
                    BuildingPlacementPreview preview =
                        scenario.Services.BuildingPlacement.CreatePreview(
                            scenario.Simulation.Entities,
                            scenario.West.Player,
                            buildingId,
                            candidate,
                            BuildingOrientation.North);

                    if (preview.IsValid)
                    {
                        return preview.GroundPosition;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"No valid placement was found for building '{buildingId}'.");
    }

    private static EntityId FindCompletedBuilding(
        VerticalSliceScenario scenario,
        BuildingId buildingId)
    {
        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding completed =
                scenario.Simulation.Entities.GetComponent<CompletedBuilding>(
                    entity);

            if (completed.Owner ==
                    scenario.West.Player &&
                completed.BuildingId ==
                    buildingId)
            {
                return entity;
            }
        }

        throw new InvalidOperationException(
            $"Completed building '{buildingId}' was not found.");
    }

    private static int CountOwnedUnits(
        VerticalSliceScenario scenario,
        UnitId unitId)
    {
        int count = 0;

        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<
                     UnitIdentity,
                     ControllableEntity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            UnitIdentity identity =
                scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                    entity);
            ControllableEntity controllable =
                scenario.Simulation.Entities.GetComponent<ControllableEntity>(
                    entity);

            if (identity.UnitId == unitId &&
                controllable.Owner ==
                    scenario.West.Player)
            {
                count++;
            }
        }

        return count;
    }

    private static void RunUntil(
        VerticalSliceScenario scenario,
        Func<bool> condition,
        int maximumTicks)
    {
        for (int tick = 0;
             tick < maximumTicks &&
             !condition();
             tick++)
        {
            scenario.Simulation.AdvanceOneTick();
        }

        Assert.True(condition());
    }

    private static VerticalSliceScenario CreateScenario(
        ulong seed)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        return VerticalSliceScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
