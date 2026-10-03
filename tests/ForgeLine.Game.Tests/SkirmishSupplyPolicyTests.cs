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
    public void EmptySupplyTruckAvoidsBlockedLoadingFaceAndLoadsPhysicalStock()
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
                    scenario.West.Player.Value,
                    0,
                    SpatialMobility.Static)));

        EntityId truck = scenario.UnitFactory.Create(
            DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck],
            new Vector3(360.0f, 0.0f, 1800.0f), scenario.West.Player);
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
        VerticalSliceScenario scenario = VerticalSliceScenario.Create(
            VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation));
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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                scenario.West.Player));
        entities.AddComponent(
            depot,
            new SpatialPresence(
                new Vector3(8.0f, 4.0f, 8.0f),
                new SpatialEntryMetadata(
                    scenario.West.Player.Value,
                    0,
                    SpatialMobility.Static)));

        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        EntityId recoveryUnit =
            scenario.UnitFactory.Create(
                units[UnitIds.RifleSquad],
                new Vector3(500.0f, 0.0f, 1800.0f),
                scenario.West.Player);
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
                scenario.West.Player);
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
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

        EntityId cargo =
            scenario.UnitFactory.Create(
                units[UnitIds.CargoTruck],
                core.Position +
                    new Vector3(20.0f, 0.0f, 0.0f),
                scenario.West.Player);
        EntityId supply =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(30.0f, 0.0f, 0.0f),
                scenario.West.Player);
        EntityId combat =
            scenario.UnitFactory.Create(
                units[UnitIds.RifleSquad],
                core.Position +
                    new Vector3(40.0f, 0.0f, 0.0f),
                scenario.West.Player);
        EntityId scout =
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                core.Position +
                    new Vector3(50.0f, 0.0f, 0.0f),
                scenario.West.Player);

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
            scenario.RuntimeSettings.Scenario.WestOpponent.OffensiveFuelThreshold,
            combatPolicy.FuelThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.WestOpponent.ResupplyThreshold,
            combatPolicy.AmmunitionThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.WestOpponent.ResupplyThreshold,
            scoutPolicy.FuelThreshold,
            precision: 6);
        Assert.Equal(
            scenario.RuntimeSettings.Scenario.WestOpponent.ResupplyThreshold,
            scoutPolicy.AmmunitionThreshold,
            precision: 6);
        Assert.True(
            scoutPolicy.FuelThreshold <
            combatPolicy.FuelThreshold);
    }

    [Fact]
    public void ReconnaissanceAdvanceSharesRouteWithPhysicalSupplyEscort()
    {
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

        EntityId scout =
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                core.Position +
                    new Vector3(30.0f, 0.0f, 0.0f),
                scenario.West.Player);
        EntityId supply =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(35.0f, 0.0f, 0.0f),
                scenario.West.Player);
        SupplyTruck supplyState =
            entities.GetComponent<SupplyTruck>(
                supply);

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
            20,
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
    }

    [Fact]
    public void PartiallyLoadedSupplyTruckReturnsForOffensiveReserve()
    {
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

        EntityId truck =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(120.0f, 0.0f, 0.0f),
                scenario.West.Player);
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
                scenario.West.Player,
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
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);
        SupplyProvider coreProvider =
            entities.GetComponent<SupplyProvider>(
                scenario.West.CommandCore);

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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            remoteDepot,
            new SupplyDepot(
                remoteInventory,
                scenario.West.Player));

        EntityId truck =
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                core.Position +
                    new Vector3(
                        120.0f,
                        0.0f,
                        0.0f),
                scenario.West.Player);
        SupplyTruck supply =
            entities.GetComponent<SupplyTruck>(
                truck);

        Vector3 remoteLoadingTarget =
            remotePosition;
        entities.AddComponent(
            truck,
            new MovementOrder(
                scenario.West.Player,
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


    [Fact]
    public void FuelRecoveryPoliciesPrioritizeFieldSupplyOverVehicleProduction()
    {
        VerticalSliceScenario scenario = VerticalSliceScenario.Create(
            VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;
        WorldTransform transform =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            refinery,
            new ControllableEntity(
                scenario.West.Player,
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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            supplyDepot,
            new ControllableEntity(
                scenario.West.Player,
                ControllableEntityCategory.Building |
                    ControllableEntityCategory.Logistics));
        entities.AddComponent(
            supplyDepot,
            new SupplyDepot(
                depotInventory,
                scenario.West.Player));

        InventoryId factoryInput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId vehicleFactory = entities.CreateEntity();
        entities.AddComponent(vehicleFactory, transform);
        entities.AddComponent(
            vehicleFactory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            vehicleFactory,
            new ControllableEntity(
                scenario.West.Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            vehicleFactory,
            new UnitProductionFacility(
                factoryInput,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
                Vector3.Zero,
                SimulationTick.Zero));

        scenario.Simulation.AdvanceOneTick();

        LogisticsStockPolicy coreSteel =
            FindStockPolicy(
                entities,
                scenario.West.CommandCore,
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
            LogisticsStockPriority.High,
            factoryFuel.Priority);
        Assert.Equal(
            240.0,
            factoryFuel.DesiredMinimum,
            precision: 6);
        Assert.Equal(
            240.0,
            factoryFuel.DesiredTarget,
            precision: 6);
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
