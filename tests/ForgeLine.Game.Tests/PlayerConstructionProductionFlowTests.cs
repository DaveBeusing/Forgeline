using System.Numerics;
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

    private static void QueueUnit(
        VerticalSliceScenario scenario,
        PlayerCommandGateway gateway,
        EntityId facility,
        UnitId unitId)
    {
        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitUnitProduction(
                scenario.West.Player,
                facility,
                unitId,
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
