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
    [InlineData(4, 150.0)]
    [InlineData(6, 50.0)]
    [InlineData(5, 100.0)]
    public void QueuedTankRequestsInputsAboveTheDefaultRefillThreshold(
        int resourceValue,
        double availableQuantity)
    {
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        var resource = new ResourceId(checked((uint)resourceValue));
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        UnitDefinition tank =
            units[UnitIds.MainBattleTank];
        WorldTransform coreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);
        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.West.Player);
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
                scenario.West.CommandCore));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.West.Player,
                SimulationTick.Zero));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.West.Player,
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle | UnitProductionCapability.Logistics,
                scenario.West.Player,
                Vector3.Zero,
                SimulationTick.Zero));
        var command = new QueueUnitProductionCommand(
            scenario.West.Player,
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
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            barracks,
            new ControllableEntity(
                scenario.West.Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            barracks,
            new UnitProductionFacility(
                barracksInput,
                UnitProductionCapability.Infantry,
                scenario.West.Player,
                Vector3.Zero,
                SimulationTick.Zero));

        int supplyTrucks = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.West.Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.SupplyTruck)
            {
                supplyTrucks++;
            }
        }

        while (supplyTrucks <
               scenario.RuntimeSettings.Scenario.WestOpponent.MinimumSupplyTrucks)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                coreTransform.Position,
                scenario.West.Player);
            supplyTrucks++;
        }

        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.West.Player);

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
                vehicleInput,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
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
        VerticalSliceScenarioSettings validation =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                validation);
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

        int supplyTrucks = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.West.Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.SupplyTruck)
            {
                supplyTrucks++;
            }
        }

        while (supplyTrucks <
               validation.WestOpponent.MinimumSupplyTrucks)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                coreTransform.Position,
                scenario.West.Player);
            supplyTrucks++;
        }

        scenario.UnitFactory.Create(
            units[UnitIds.ScoutVehicle],
            coreTransform.Position,
            scenario.West.Player);

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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new ControllableEntity(
                scenario.West.Player,
                ControllableEntityCategory.Building));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
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
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        WorldTransform coreTransform =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);

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
                    scenario.West.Player,
                    SimulationTick.Zero));
            entities.AddComponent(
                depot,
                new SupplyDepot(
                    depotInventory,
                    scenario.West.Player));
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
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
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
        VerticalSliceScenarioSettings validation =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                validation with
                {
                    WestOpponent =
                        validation.WestOpponent with
                        {
                            MinimumCargoTrucks = 2,
                            MinimumSupplyTrucks = 1
                        }
                });
        EntityRegistry entities =
            scenario.Simulation.Entities;
        UnitDefinitionCatalog units =
            DirectorateContent.CreateUnitCatalog();
        Vector3 stagingPosition =
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore).Position;

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
                scenario.West.Player)
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
                    scenario.West.Player));
        }

        if (!hasSupply)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.SupplyTruck],
                stagingPosition,
                scenario.West.Player);
        }

        if (scouts.Count == 0)
        {
            scouts.Add(
                scenario.UnitFactory.Create(
                    units[UnitIds.ScoutVehicle],
                    stagingPosition,
                    scenario.West.Player));
        }

        int tankCount = 0;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.West.Player &&
                entities.GetComponent<UnitIdentity>(
                    entity).UnitId ==
                    UnitIds.MainBattleTank)
            {
                tankCount++;
            }
        }

        while (tankCount <
               validation.WestOpponent.MinimumObjectivePressureUnits)
        {
            scenario.UnitFactory.Create(
                units[UnitIds.MainBattleTank],
                stagingPosition,
                scenario.West.Player);
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
                scenario.West.CommandCore));
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
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
                scenario.West.Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);
        var secondTank =
            new QueueUnitProductionCommand(
                scenario.West.Player,
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
            validation.WestOpponent.ReactionCadenceTicks + 2,
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
    public void MissingCargoTruckPreemptsBlockedCombatProduction()
    {
        VerticalSliceScenarioSettings validation =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                validation with
                {
                    WestOpponent =
                        validation.WestOpponent with
                        {
                            MinimumCargoTrucks = 2,
                            MinimumSupplyTrucks = 1
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
                scenario.West.Player)
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
                scenario.West.CommandCore).Position;
        while (westCargo.Count < 2)
        {
            westCargo.Add(
                scenario.UnitFactory.Create(
                    cargoDefinition,
                    stagingPosition,
                    scenario.West.Player));
        }

        if (!hasSupplyTruck)
        {
            scenario.UnitFactory.Create(
                supplyDefinition,
                stagingPosition,
                scenario.West.Player);
        }

        bool hasScout = false;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(
                    entity).Owner ==
                    scenario.West.Player &&
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
                scenario.West.Player);
        }

        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId factory =
            entities.CreateEntity();
        entities.AddComponent(
            factory,
            entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore));
        entities.AddComponent(
            factory,
            new CompletedBuilding(
                BuildingIds.VehicleFactory,
                scenario.West.Player,
                SimulationTick.Zero));
        entities.AddComponent(
            factory,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle |
                UnitProductionCapability.Logistics,
                scenario.West.Player,
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
                scenario.West.Player,
                factory,
                UnitIds.MainBattleTank,
                SimulationTick.Zero);
        var secondTank =
            new QueueUnitProductionCommand(
                scenario.West.Player,
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
