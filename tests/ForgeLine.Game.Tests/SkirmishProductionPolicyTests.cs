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
    public void MissingCargoTruckPreemptsBlockedCombatProduction()
    {
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation));
        var entities =
            scenario.Simulation.Entities;

        EntityId lostCargo =
            EntityId.Invalid;
        foreach (EntityId entity in
                 entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<ControllableEntity>(entity).Owner ==
                    scenario.West.Player &&
                entities.GetComponent<UnitIdentity>(entity).UnitId ==
                    UnitIds.CargoTruck)
            {
                lostCargo = entity;
                break;
            }
        }

        Assert.True(lostCargo.IsValid);
        Assert.True(
            entities.DestroyEntity(
                lostCargo));

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

        UnitProductionFacility state =
            entities.GetComponent<UnitProductionFacility>(
                factory);
        Assert.Equal(
            UnitIds.CargoTruck,
            state.ActiveUnit);
        Assert.Equal(
            UnitProductionStatus.NoInput,
            state.Status);

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
