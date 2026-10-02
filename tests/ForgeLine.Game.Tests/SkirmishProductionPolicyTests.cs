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
        UnitDefinition tank =
            DirectorateContent.CreateUnitCatalog()[UnitIds.MainBattleTank];
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
