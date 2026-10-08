using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishProductionPolicyTests
{
    [Theory]
    [InlineData(true, false, LogisticsStockPriority.Critical)]
    [InlineData(true, true, LogisticsStockPriority.High)]
    [InlineData(false, false, LogisticsStockPriority.High)]
    public void ForwardConstructionReserveYieldsToCargoRecoveryAndInitialMobilization(
        bool establishedForce, bool cargoLost, LogisticsStockPriority expectedPriority)
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;
        var owner = new ForgeLine.Game.PlayerId(1);
        EntityId core = scenario.GetBase(owner).CommandCore;
        Vector3 position = entities.GetComponent<WorldTransform>(core).Position;
        entities.AddComponent(core, new PowerGenerator(100.0));
        if (establishedForce)
        {
            UnitDefinitionCatalog units = DirectorateContent.CreateUnitCatalog();
            scenario.UnitFactory.Create(units[UnitIds.MainBattleTank], position, owner);
            for (int index = 0; index < 3; index++)
                scenario.UnitFactory.Create(units[UnitIds.RifleSquad], position, owner);
        }
        if (cargoLost)
        {
            var cargo = new List<EntityId>();
            foreach (EntityId entity in entities.Query<ControllableEntity, UnitIdentity>())
                if (entities.GetComponent<ControllableEntity>(entity).Owner == owner &&
                    entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck)
                    cargo.Add(entity);
            foreach (EntityId entity in cargo)
                Assert.True(entities.DestroyEntity(entity));
        }

        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);

        LogisticsStockPolicy policy = FindStockPolicy(entities, core, ResourceIds.FerrousOre);
        Assert.Equal(expectedPriority, policy.Priority);
        Assert.Equal(220.0, policy.DesiredMinimum);
        Assert.Equal(650.0, policy.DesiredTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedCargoRecoveryPreemptsOnlyBlockedSupplyProduction(bool supplyInputsAvailable)
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;
        var owner = new ForgeLine.Game.PlayerId(1);
        EntityId lostCargo = EntityId.Invalid;
        foreach (EntityId entity in entities.Query<ControllableEntity, UnitIdentity>())
            if (entities.GetComponent<ControllableEntity>(entity).Owner == owner &&
                entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck)
            { lostCargo = entity; break; }
        Assert.True(entities.DestroyEntity(lostCargo));
        InventoryId input = scenario.Inventories.CreateInventory(new InventorySpecification(4_000.0));
        foreach (UnitResourceCost cost in DirectorateContent.CreateUnitCatalog()[
                     supplyInputsAvailable ? UnitIds.SupplyTruck : UnitIds.CargoTruck].Costs)
            Assert.True(scenario.Inventories.Add(input, cost.ResourceId, cost.Quantity).Succeeded);
        EntityId factory = entities.CreateEntity();
        entities.AddComponent(factory, entities.GetComponent<WorldTransform>(scenario.GetBase(owner).CommandCore));
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, owner, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(owner, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new UnitProductionFacility(input,
            UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
            owner, Vector3.Zero, SimulationTick.Zero));
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10_006)));
        entities.AddComponent(factory, new PowerGenerator(10.0));
        entities.AddComponent(factory, new PowerConsumer(1.0, PowerPriority.Industrial, enabled: true));
        var supply = new QueueUnitProductionCommand(owner, factory, UnitIds.SupplyTruck,
            SimulationTick.Zero, ProductionPriority.High);
        var cargo = new QueueUnitProductionCommand(owner, factory, UnitIds.CargoTruck,
            SimulationTick.Zero, ProductionPriority.High);
        scenario.Simulation.SubmitCommand(supply, SimulationTick.Zero.Next());
        scenario.Simulation.SubmitCommand(cargo, SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(3, TestContext.Current.CancellationToken);

        Assert.True(supply.Accepted);
        Assert.True(cargo.Accepted);
        UnitProductionFacility recovered = entities.GetComponent<UnitProductionFacility>(factory);
        Assert.Equal(supplyInputsAvailable ? UnitIds.SupplyTruck : UnitIds.CargoTruck, recovered.ActiveUnit);
        Assert.Equal(UnitProductionStatus.Running, recovered.Status);
        Assert.True(recovered.InputsReserved);
    }

    [Fact]
    public void PendingCargoReplacementsDoNotReleaseTheirMaterialReserveForSupplyProduction()
    {
        using MatchRuntime scenario = CentralDivideScenario.Create(
            CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation));
        EntityRegistry entities = scenario.Simulation.Entities;
        var owner = new ForgeLine.Game.PlayerId(1);
        var cargo = new List<EntityId>();
        foreach (EntityId entity in entities.Query<ControllableEntity, UnitIdentity>())
            if (entities.GetComponent<ControllableEntity>(entity).Owner == owner &&
                entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck)
                cargo.Add(entity);
        foreach (EntityId entity in cargo)
            Assert.True(entities.DestroyEntity(entity));

        InventoryId input = scenario.Inventories.CreateInventory(new InventorySpecification(4_000.0));
        UnitDefinition supply = DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck];
        foreach (UnitResourceCost cost in supply.Costs)
            Assert.True(scenario.Inventories.Add(input, cost.ResourceId, cost.Quantity).Succeeded);

        EntityId factory = entities.CreateEntity();
        entities.AddComponent(factory, entities.GetComponent<WorldTransform>(scenario.GetBase(owner).CommandCore));
        entities.AddComponent(factory, new CompletedBuilding(BuildingIds.VehicleFactory, owner, SimulationTick.Zero));
        entities.AddComponent(factory, new ControllableEntity(owner, ControllableEntityCategory.Building));
        entities.AddComponent(factory, new UnitProductionFacility(input,
            UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
            owner, Vector3.Zero, SimulationTick.Zero));
        entities.AddComponent(factory, new PowerNetworkMembership(new PowerNetworkId(10_005)));
        // Keep requests pending so their unbuilt vehicles cannot count as a live fleet.
        entities.AddComponent(factory, new PowerGenerator(10.0, enabled: false));
        entities.AddComponent(factory, new PowerConsumer(1.0, PowerPriority.Industrial, enabled: true));
        var requests = new[]
        {
            new QueueUnitProductionCommand(owner, factory, UnitIds.CargoTruck, SimulationTick.Zero),
            new QueueUnitProductionCommand(owner, factory, UnitIds.CargoTruck, SimulationTick.Zero)
        };
        foreach (var request in requests)
            scenario.Simulation.SubmitCommand(request, SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(3, TestContext.Current.CancellationToken);

        Assert.All(requests, request => Assert.True(request.Accepted));
        foreach (EntityId entity in entities.Query<UnitProductionRequest>())
            if (entities.GetComponent<UnitProductionRequest>(entity).Facility == factory)
                Assert.NotEqual(UnitIds.SupplyTruck, entities.GetComponent<UnitProductionRequest>(entity).UnitId);
        Assert.NotEqual(UnitIds.SupplyTruck, entities.GetComponent<UnitProductionFacility>(factory).ActiveUnit);
        Assert.Equal(0, scenario.CountUnits(owner, UnitIds.CargoTruck));
        foreach (UnitResourceCost cost in supply.Costs)
            Assert.Equal(cost.Quantity, scenario.Inventories.GetAvailableQuantity(input, cost.ResourceId));
    }

    [Theory]
    [InlineData(4, 150.0)]
    [InlineData(6, 50.0)]
    [InlineData(5, 100.0)]
    public void QueuedTankRequestsInputsAboveTheDefaultRefillThreshold(
        int resourceValue,
        double availableQuantity)
    {
        MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        var resource = new ResourceId(checked((uint)resourceValue));
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        UnitDefinition tank =
            units[UnitIds.MainBattleTank];
        WorldTransform coreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        InventoryId input = scenario.Inventories.CreateInventory(
            new InventorySpecification(4_000.0));

        foreach (UnitResourceCost cost in tank.Costs)
        {
            Assert.True(scenario.Inventories.Add(
                input,
                cost.ResourceId,
                cost.ResourceId == resource ? availableQuantity : 500.0).Succeeded);
        }

        EntityId factory = scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            factory,
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var command = new QueueUnitProductionCommand(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
            factory,
            UnitIds.MainBattleTank,
            SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(command, SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(12, TestContext.Current.CancellationToken);

        Assert.True(command.Accepted);
        EntityId policyEntity = EntityId.Invalid;
        foreach (EntityId entity in scenario.Simulation.Entities.Query<LogisticsStockPolicy>())
        {
            LogisticsStockPolicy policy =
                scenario.Simulation.Entities.GetComponent<LogisticsStockPolicy>(entity);
            if (policy.TargetEntity == factory && policy.ResourceId == resource)
            {
                policyEntity = entity;
                break;
            }
        }

        Assert.True(policyEntity.IsValid);
        Assert.Contains(
            scenario.AutomatedDistribution.LastDebugSnapshot.Requests,
            request => request.PolicyEntity == policyEntity &&
                request.RequestedQuantity >= tank.Costs.Single(cost => cost.ResourceId == resource).Quantity - availableQuantity);
    }

    [Fact]
    public void ObjectivePressureFactoryOutranksRoutineUnitMaterialDemand()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        InventoryId barracksInput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId barracks =
            entities.CreateEntity();
        entities.AddComponent(barracks, coreTransform);
        entities.AddComponent(
            barracks,
            new CompletedBuilding(
                BuildingIds.Barracks,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            barracks,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            barracks,
            new UnitProductionFacility(
                barracksInput,
                UnitProductionCapability.Infantry,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));

        int supplyTrucks = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.SupplyTruck)
            {
                supplyTrucks++;
            }
        }

        while (supplyTrucks <
               scenario.RuntimeSettings.Scenario.OpponentConfigurations[1].MinimumSupplyTrucks)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            supplyTrucks++;
        }

        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);

        InventoryId vehicleInput =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId vehicleFactory =
            entities.CreateEntity();
        entities.AddComponent(vehicleFactory, coreTransform);
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
                vehicleInput,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        LogisticsStockPolicy barracksSteel =
            FindStockPolicy(
                entities,
                barracks,
                ResourceIds.Steel);
        LogisticsStockPolicy vehicleSteel =
            FindStockPolicy(
                entities,
                vehicleFactory,
                ResourceIds.Steel);

        Assert.Equal(
            LogisticsStockPriority.High,
            barracksSteel.Priority);
        Assert.Equal(
            LogisticsStockPriority.Critical,
            vehicleSteel.Priority);
        Assert.True(
            vehicleSteel.DesiredMinimum >=
            units[UnitIds.MainBattleTank]
                .Costs
                .Single(
                    cost =>
                        cost.ResourceId ==
                        ResourceIds.Steel)
                .Quantity);
    }

    [Fact]
    public void ObjectivePressureUnitsPrecedeOptionalVehicleGrowth()
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                validation);
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        int supplyTrucks = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.SupplyTruck)
            {
                supplyTrucks++;
            }
        }

        while (supplyTrucks <
               validation.OpponentConfigurations[1].MinimumSupplyTrucks)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            supplyTrucks++;
        }

        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            coreTransform);
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var network =
            new PowerNetworkId(10_002);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                network));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        foreach (UnitResourceCost cost in
                 units[UnitIds.MainBattleTank].Costs)
        {
            Assert.True(
                scenario.Inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        foreach (UnitResourceCost cost in
                 units[UnitIds.CargoTruck].Costs)
        {
            Assert.True(
                scenario.Inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        bool queuedObjectivePressureUnit = false;

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility == factory &&
                request.UnitId ==
                    UnitIds.MainBattleTank)
            {
                queuedObjectivePressureUnit = true;
                break;
            }
        }

        Assert.True(queuedObjectivePressureUnit);

        LogisticsStockPolicy? fuelPolicy = null;

        foreach (EntityId policyEntity in
                 entities.Query<LogisticsStockPolicy>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy policy =
                entities.GetComponent<LogisticsStockPolicy>(
                    policyEntity);

            if (policy.TargetEntity == factory &&
                policy.ResourceId ==
                    ResourceIds.Fuel)
            {
                fuelPolicy = policy;
                break;
            }
        }

        Assert.True(fuelPolicy.HasValue);
        Assert.Equal(
            LogisticsStockPriority.Critical,
            fuelPolicy.Value.Priority);
        Assert.True(
            fuelPolicy.Value.DesiredMinimum >=
            units[UnitIds.MainBattleTank]
                .Costs
                .Single(
                    cost =>
                        cost.ResourceId ==
                        ResourceIds.Fuel)
                .Quantity);
    }

    [Fact]
    public void HealthyCargoFleetDoesNotBlockArtilleryProductionReserve()
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                validation);
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        int depotCount = 0;
        int cargoCount = 0;
        int supplyCount = 0;
        int scoutCount = 0;
        int tankCount = 0;

        foreach (EntityId entity in
                 entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                building.BuildingId ==
                    BuildingIds.SupplyDepot)
            {
                depotCount++;
            }
        }

        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner !=
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player)
            {
                continue;
            }

            UnitId unitId =
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId;

            if (unitId == UnitIds.CargoTruck)
            {
                cargoCount++;
            }
            else if (unitId == UnitIds.SupplyTruck)
            {
                supplyCount++;
            }
            else if (unitId == UnitIds.ScoutVehicle)
            {
                scoutCount++;
            }
            else if (unitId == UnitIds.MainBattleTank)
            {
                tankCount++;
            }
        }

        int cargoTarget =
            Math.Max(
                validation.OpponentConfigurations[1].MinimumCargoTrucks,
                Math.Clamp(
                    depotCount,
                    2,
                    4));

        while (cargoCount < cargoTarget)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.CargoTruck],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            cargoCount++;
        }

        while (supplyCount <
               validation.OpponentConfigurations[1].MinimumSupplyTrucks)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            supplyCount++;
        }

        while (scoutCount < 1)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            scoutCount++;
        }

        while (tankCount <
               validation.OpponentConfigurations[1].MinimumObjectivePressureUnits)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.MainBattleTank],
                coreTransform.Position,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            tankCount++;
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            coreTransform);
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));

        var network =
            new PowerNetworkId(10_004);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                network));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        foreach (UnitResourceCost cost in
                 units[UnitIds.MobileArtillery].Costs)
        {
            Assert.True(
                scenario.Inventories.Add(
                    input,
                    cost.ResourceId,
                    cost.Quantity).Succeeded);
        }

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        bool artilleryQueued = false;

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility ==
                    factory &&
                request.UnitId ==
                    UnitIds.MobileArtillery)
            {
                artilleryQueued = true;
                break;
            }
        }

        UnitProductionFacility state =
            entities.GetComponent<UnitProductionFacility>(
                factory);

        Assert.True(
            artilleryQueued ||
            state.ActiveUnit ==
                UnitIds.MobileArtillery,
            "A healthy Cargo Truck fleet must not reserve an additional replacement vehicle's materials before required artillery can enter production.");
    }


    private static LogisticsStockPolicy FindStockPolicy(
        EntityRegistry entities,
        EntityId target,
        ResourceId resource)
    {
        foreach (EntityId entity in
                 entities.Query<LogisticsStockPolicy>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy policy =
                entities.GetComponent<LogisticsStockPolicy>(
                    entity);

            if (policy.TargetEntity == target &&
                policy.ResourceId == resource)
            {
                return policy;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"No stock policy found for target {target} and resource {resource}.");
    }

    [Fact]
    public void ExpandedSupplyNetworkQueuesAdditionalCargoRecovery()
    {
        MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        for (int index = 0; index < 3; index++)
        {
            InventoryId depotInventory =
                scenario.Inventories.CreateInventory(
                    new InventorySpecification(2_500.0));
            EntityId depot =
                entities.CreateEntity();
            entities.AddComponent(
                depot,
                coreTransform);
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
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            coreTransform);
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var isolatedNetwork =
            new PowerNetworkId(10_001);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                isolatedNetwork));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        bool queuedRecoveryCargo = false;
        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility == factory &&
                request.UnitId == UnitIds.CargoTruck &&
                request.Priority == ProductionPriority.High)
            {
                queuedRecoveryCargo = true;
                break;
            }
        }

        Assert.True(queuedRecoveryCargo);
    }

    [Fact]
    public void LostScoutPreemptsBlockedRoutineVehicleProduction()
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        MatchRuntime scenario =
            CentralDivideScenario.Create(
                validation with
                {
                    OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(validation.OpponentConfigurations)
                    {
                        [1] =
                        validation.OpponentConfigurations[1] with
                        {
                            MinimumCargoTrucks = 2,
                            MinimumSupplyTrucks = 1
                        }
                    }
                });
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        Vector3 stagingPosition =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore).Position;

        var cargo =
            new List<EntityId>();
        bool hasSupply =
            false;
        var scouts =
            new List<EntityId>();

        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner !=
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player)
            {
                continue;
            }

            UnitId unitId =
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId;

            if (unitId == UnitIds.CargoTruck)
            {
                cargo.Add(entity);
            }
            else if (unitId == UnitIds.SupplyTruck)
            {
                hasSupply = true;
            }
            else if (unitId == UnitIds.ScoutVehicle)
            {
                scouts.Add(entity);
            }
        }

        while (cargo.Count < 2)
        {
            cargo.Add(
                scenario.UnitFactory.Create(
                    units[UnitIds.CargoTruck],
                    stagingPosition,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        }

        if (!hasSupply)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        }

        if (scouts.Count == 0)
        {
            scouts.Add(
                scenario.UnitFactory.Create(
                    units[UnitIds.ScoutVehicle],
                    stagingPosition,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        }

        int tankCount = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.MainBattleTank)
            {
                tankCount++;
            }
        }

        while (tankCount <
               validation.OpponentConfigurations[1].MinimumObjectivePressureUnits)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.MainBattleTank],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            tankCount++;
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore));
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));

        var isolatedNetwork =
            new PowerNetworkId(10_002);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                isolatedNetwork));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        var firstTank =
            new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);
        var secondTank =
            new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);

        scenario.Simulation.SubmitCommand(
            firstTank,
            SimulationTick.Zero.Next());
        scenario.Simulation.SubmitCommand(
            secondTank,
            SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        UnitProductionFacility blocked =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.MainBattleTank,
            blocked.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            blocked.Status);

        for (int index = 0;
             index < scouts.Count;
             index++)
        {
            Assert.True(
                entities.DestroyEntity(
                    scouts[index]));
        }

        scenario.Simulation.RunTicks(
            validation.OpponentConfigurations[1].ReactionCadenceTicks + 2,
            TestContext.Current.CancellationToken);

        UnitProductionFacility recovered =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.ScoutVehicle,
            recovered.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            recovered.Status);

        var live =
            new List<UnitProductionRequest>();

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility == factory)
            {
                live.Add(request);
            }
        }

        Assert.Contains(
            live,
            request =>
                request.UnitId ==
                    UnitIds.ScoutVehicle &&
                request.Priority ==
                    ProductionPriority.High);
        Assert.Single(
            live,
            request =>
                request.UnitId ==
                    UnitIds.MainBattleTank);

        LogisticsStockPolicy steelPolicy =
            FindStockPolicy(
                entities,
                factory,
                ResourceIds.Steel);
        Assert.Equal(
            LogisticsStockPriority.Critical,
            steelPolicy.Priority);
    }


    [Fact]
    public void MatureSupplyRecoveryPreemptsBlockedOptionalVehicleProduction()
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                validation);
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        Vector3 stagingPosition =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore).Position;

        int depotCount = 0;
        int cargoCount = 0;
        var supplyTrucks =
            new List<EntityId>();
        int scoutCount = 0;
        int tankCount = 0;
        int artilleryCount = 0;

        foreach (EntityId entity in
                 entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                building.BuildingId ==
                    BuildingIds.SupplyDepot)
            {
                depotCount++;
            }
        }

        while (depotCount < 3)
        {
            InventoryId depotInventory =
                scenario.Inventories.CreateInventory(
                    new InventorySpecification(
                        2_500.0));
            EntityId depot =
                entities.CreateEntity();
            entities.AddComponent(
                depot,
                new WorldTransform(
                    stagingPosition +
                        new Vector3(
                            40.0f * depotCount,
                            0.0f,
                            80.0f),
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
            depotCount++;
        }

        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner !=
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player)
            {
                continue;
            }

            UnitId unitId =
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId;

            if (unitId == UnitIds.CargoTruck)
            {
                cargoCount++;
            }
            else if (unitId == UnitIds.SupplyTruck)
            {
                supplyTrucks.Add(entity);
            }
            else if (unitId == UnitIds.ScoutVehicle)
            {
                scoutCount++;
            }
            else if (unitId == UnitIds.MainBattleTank)
            {
                tankCount++;
            }
            else if (unitId == UnitIds.MobileArtillery)
            {
                artilleryCount++;
            }
        }

        int cargoTarget =
            Math.Max(
                validation.OpponentConfigurations[1].MinimumCargoTrucks,
                Math.Clamp(
                    depotCount,
                    2,
                    4));
        while (cargoCount < cargoTarget)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.CargoTruck],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            cargoCount++;
        }

        int matureSupplyTarget =
            validation.OpponentConfigurations[1].ResolveMatureSupplyTruckTarget(
                depotCount);
        while (supplyTrucks.Count <
               matureSupplyTarget)
        {
            supplyTrucks.Add(
                scenario.UnitFactory.Create(
                    units[UnitIds.SupplyTruck],
                    stagingPosition,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        }

        while (scoutCount < 1)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            scoutCount++;
        }

        while (tankCount <
               validation.OpponentConfigurations[1].MinimumObjectivePressureUnits)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.MainBattleTank],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            tankCount++;
        }

        while (artilleryCount < 1)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.MobileArtillery],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
            artilleryCount++;
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore));
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var isolatedNetwork =
            new PowerNetworkId(10_003);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                isolatedNetwork));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        var optionalArtillery =
            new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                factory,
                UnitIds.MobileArtillery,
                SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(
            optionalArtillery,
            SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        UnitProductionFacility blocked =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.MobileArtillery,
            blocked.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            blocked.Status);

        Assert.True(
            entities.DestroyEntity(
                supplyTrucks[^1]));

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        UnitProductionFacility recovered =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.SupplyTruck,
            recovered.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            recovered.Status);

        bool queuedMatureSupplyRecovery =
            false;

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility ==
                    factory &&
                request.UnitId ==
                    UnitIds.SupplyTruck &&
                request.Priority ==
                    ProductionPriority.High)
            {
                queuedMatureSupplyRecovery =
                    true;
                break;
            }
        }

        Assert.True(
            queuedMatureSupplyRecovery);
    }


    [Fact]
    public void MissingCargoTruckPreemptsBlockedCombatProduction()
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        MatchRuntime scenario =
            CentralDivideScenario.Create(
                validation with
                {
                    OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>(validation.OpponentConfigurations)
                    {
                        [1] =
                        validation.OpponentConfigurations[1] with
                        {
                            MinimumCargoTrucks = 2,
                            MinimumSupplyTrucks = 1
                        }
                    }
                });
        var entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        UnitDefinition cargoDefinition =
            units[UnitIds.CargoTruck];
        UnitDefinition supplyDefinition =
            units[UnitIds.SupplyTruck];

        var westCargo =
            new List<EntityId>();
        bool hasSupplyTruck =
            false;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(entity).Owner !=
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player)
            {
                continue;
            }

            UnitId unitId =
                entities.GetComponent<UnitIdentity>(entity).UnitId;
            if (unitId ==
                UnitIds.CargoTruck)
            {
                westCargo.Add(entity);
            }
            else if (unitId ==
                     UnitIds.SupplyTruck)
            {
                hasSupplyTruck = true;
            }
        }

        Vector3 stagingPosition =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore).Position;
        while (westCargo.Count < 2)
        {
            westCargo.Add(
                scenario.UnitFactory.Create(
                    cargoDefinition,
                    stagingPosition,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        }

        if (!hasSupplyTruck)
        {
            scenario.UnitFactory.Create(
                supplyDefinition,
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        }

        bool hasScout = false;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.ScoutVehicle)
            {
                hasScout = true;
                break;
            }
        }

        if (!hasScout)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.ScoutVehicle],
                stagingPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore));
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var isolatedNetwork =
            new PowerNetworkId(10_000);
        entities.AddComponent(
            factory,
            new PowerNetworkMembership(
                isolatedNetwork));
        entities.AddComponent(
            factory,
            new PowerGenerator(
                10.0));
        entities.AddComponent(
            factory,
            new PowerConsumer(
                1.0,
                PowerPriority.Industrial,
                enabled: true));

        var firstTank =
            new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);
        var secondTank =
            new QueueUnitProductionCommand(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(
            firstTank,
            SimulationTick.Zero.Next());
        scenario.Simulation.SubmitCommand(
            secondTank,
            SimulationTick.Zero.Next());

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        UnitProductionFacility blocked =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.MainBattleTank,
            blocked.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            blocked.Status);

        for (int index = 1;
             index < westCargo.Count;
             index++)
        {
            Assert.True(
                entities.DestroyEntity(
                    westCargo[index]));
        }

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        UnitProductionFacility recovered =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.CargoTruck,
            recovered.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            recovered.Status);

        var live =
            new List<UnitProductionRequest>();
        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);
            if (request.Facility ==
                factory)
            {
                live.Add(request);
            }
        }

        Assert.Contains(
            live,
            request =>
                request.UnitId ==
                    UnitIds.CargoTruck &&
                request.Priority ==
                    ProductionPriority.High);
        Assert.Single(
            live,
            request =>
                request.UnitId ==
                    UnitIds.MainBattleTank);
    }


}
