using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PlayerActionPanelTests
{
    [Fact]
    public void ConstructionPaletteNavigatesByKeyboardAndCreatesPlacementRequest()
    {
        PresentationSnapshot snapshot =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant"),
                    Construction(
                        BuildingIds.VehicleFactory,
                        "Vehicle Factory")
                ]);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(
            input,
            PlatformKey.B);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Construction,
            controller.Mode);

        Release(
            input,
            PlatformKey.B);
        Press(
            input,
            PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            1,
            controller.SelectedIndex);

        Release(
            input,
            PlatformKey.Tab);
        Press(
            input,
            PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.BeginBuildingPlacement,
            request.Kind);
        Assert.Equal(
            BuildingIds.VehicleFactory,
            request.BuildingId);
        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
    }

    [Fact]
    public void ProductionPaletteExposesModePriorityDesiredStockPauseAndCancel()
    {
        EntityId facility =
            new(50, 1);
        EntityId queuedRequest =
            new(51, 1);
        var recipe =
            new PlayerProductionRecipeActionReadModel(
                RecipeIds.Steel,
                "Steel",
                [
                    new PlayerActionResourceAmount(
                        ResourceIds.FerrousOre,
                        "Ferrous Ore",
                        10.0,
                        100.0)
                ],
                [
                    new PlayerActionResourceAmount(
                        ResourceIds.Steel,
                        "Steel",
                        10.0,
                        20.0)
                ]);
        var production =
            new PlayerProductionFacilityActionReadModel(
                facility,
                queuedRequest,
                RecipeIds.Steel,
                ProductionStatus.Running,
                ProductionBlockReason.None,
                0.25,
                [recipe],
                [
                    new PlayerProductionRequestReadModel(
                        queuedRequest,
                        RecipeIds.Steel,
                        "Steel",
                        ProductionPriority.Normal,
                        ProductionRequestMode.Repeat,
                        false,
                        true)
                ]);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                production: production);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.P);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.P);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            ProductionRequestMode.DesiredStock,
            controller.ProductionMode);

        Press(input, PlatformKey.T);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.T);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            ProductionPriority.High,
            controller.Priority);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest queue));
        Assert.Equal(
            PlayerActionRequestKind.QueueProduction,
            queue.Kind);
        Assert.Equal(
            facility,
            queue.Facility);
        Assert.Equal(
            RecipeIds.Steel,
            queue.RecipeId);
        Assert.Equal(
            ProductionPriority.High,
            queue.Priority);
        Assert.Equal(
            ProductionRequestMode.DesiredStock,
            queue.ProductionMode);
        Assert.Equal(
            ResourceIds.Steel,
            queue.DesiredStockResourceId);
        Assert.True(
            queue.DesiredStockQuantity >
            20.0);

        Press(input, PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            1,
            controller.SelectedIndex);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest pause));
        Assert.Equal(
            PlayerActionRequestKind.SetProductionPaused,
            pause.Kind);
        Assert.Equal(
            queuedRequest,
            pause.RequestEntity);
        Assert.True(pause.Paused);

        Press(input, PlatformKey.C);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.C);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest cancel));
        Assert.Equal(
            PlayerActionRequestKind.CancelProduction,
            cancel.Kind);
        Assert.Equal(
            queuedRequest,
            cancel.RequestEntity);
    }

    [Fact]
    public void PointerActivationIsCapturedAndFocusLossDoesNotRepeatModeToggle()
    {
        PresentationSnapshot snapshot =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ]);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.B);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        input.Apply(
            PlatformInputEvent.FocusLost());
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Construction,
            controller.Mode);

        input.Apply(
            PlatformInputEvent.PointerMoved(
                1_000,
                168));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                1_000,
                168));
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.PointerCaptured);
        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.BeginBuildingPlacement,
            request.Kind);
        Assert.Equal(
            BuildingIds.PowerPlant,
            request.BuildingId);
    }

    [Fact]
    public void LogisticsPaletteEditsThresholdsAndCreatesPolicyRequest()
    {
        EntityId target =
            new(70, 1);
        EntityId policyEntity =
            new(71, 1);
        var logistics =
            new PlayerLogisticsActionReadModel(
                target,
                [
                    new PlayerStockPolicyActionReadModel(
                        policyEntity,
                        ResourceIds.Fuel,
                        "Fuel",
                        25.0,
                        20.0,
                        40.0,
                        80.0,
                        LogisticsStockPriority.Normal,
                        true,
                        PlayerDistributionActionState.Waiting,
                        LogisticsTransportRequestFailureReason.NoTruckAvailable,
                        LogisticsBottleneckReason.None,
                        EntityId.Invalid)
                ],
                null);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                logistics: logistics);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.L);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.L);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.Right);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Right);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.CreateView(
                    1600,
                    900,
                    snapshot.PlayerActions)
                .StockTarget >
            40.0);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SetStockPolicy,
            request.Kind);
        Assert.Equal(target, request.Facility);
        Assert.Equal(ResourceIds.Fuel, request.StockResourceId);
        Assert.True(
            request.StockMinimum <= request.StockTarget);
        Assert.True(
            request.StockTarget <= request.StockMaximum);

        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.C);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest remove));
        Assert.Equal(
            PlayerActionRequestKind.RemoveStockPolicy,
            remove.Kind);
        Assert.Equal(policyEntity, remove.RequestEntity);
    }

    [Fact]
    public void SupplyPaletteAdjustsPolicyAndRequestsExplicitResupply()
    {
        EntityId unit =
            new(80, 1);
        var supply =
            new PlayerSupplyActionReadModel(
                unit,
                BattlefieldSupplyStatus.LowSupply,
                0.2,
                0.3,
                true,
                0.2,
                0.2,
                new EntityId(81, 1),
                PlayerSupplyProviderState.Traveling,
                100.0,
                50.0,
                ResupplyProviderRejection.None);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                supply: supply);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.Y);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Y);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.Right);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Right);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest policyRequest));
        Assert.Equal(
            PlayerActionRequestKind.SetAutomaticResupplyPolicy,
            policyRequest.Kind);
        Assert.True(
            policyRequest.AutomaticFuelThreshold >
            0.2);

        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.Tab);
        controller.Update(input, snapshot, 1600, 900);
        Release(input, PlatformKey.Tab);
        controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.Tab);
        controller.Update(input, snapshot, 1600, 900);
        Release(input, PlatformKey.Tab);
        controller.Update(input, snapshot, 1600, 900);

        Press(input, PlatformKey.Enter);
        controller.Update(input, snapshot, 1600, 900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest resupplyRequest));
        Assert.Equal(
            PlayerActionRequestKind.RequestResupply,
            resupplyRequest.Kind);
        Assert.Equal(unit, resupplyRequest.Facility);
    }

    [Fact]
    public void TacticalPaletteMapsTargetedAndImmediateActions()
    {
        EntityId unit =
            new(90, 1);
        var tactical =
            new PlayerTacticalActionReadModel(
                [unit],
                requestedSelectionCount: 1,
                combatEligibleCount: 1,
                rejectedSelectionCount: 0,
                criticalSupplyCount: 0,
                resupplyingCount: 0,
                hasCommonOrder: false,
                mixedOrderState: false,
                currentOrder: default,
                currentStatus: default,
                targets: [],
                artillery: []);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                tactical: tactical);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.K);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.K);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Tactical,
            controller.Mode);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest attack));
        Assert.Equal(
            PlayerActionRequestKind.BeginAttackTargeting,
            attack.Kind);
        Assert.Equal(
            new[] { unit },
            attack.TacticalEntities);
        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);

        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.K);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.K);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        for (int index = 0;
             index < 3;
             index++)
        {
            Press(input, PlatformKey.Tab);
            controller.Update(
                input,
                snapshot,
                1600,
                900);
            Release(input, PlatformKey.Tab);
            controller.Update(
                input,
                snapshot,
                1600,
                900);
        }

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest hold));
        Assert.Equal(
            PlayerActionRequestKind.SubmitHoldPosition,
            hold.Kind);
        Assert.Equal(
            new[] { unit },
            hold.TacticalEntities);
    }

    private static PlayerConstructionActionReadModel Construction(
        BuildingId buildingId,
        string name) =>
        new(
            buildingId,
            name,
            [
                new PlayerActionResourceAmount(
                    ResourceIds.FerrousOre,
                    "Ferrous Ore",
                    10.0,
                    100.0)
            ],
            false);

    private static PresentationSnapshot CreateSnapshot(
        IReadOnlyList<PlayerConstructionActionReadModel>? construction = null,
        PlayerProductionFacilityActionReadModel? production = null,
        PlayerUnitProductionFacilityActionReadModel? unitProduction = null,
        PlayerLogisticsActionReadModel? logistics = null,
        PlayerSupplyActionReadModel? supply = null,
        PlayerTacticalActionReadModel? tactical = null)
    {
        var session =
            new SimulationSessionId(101);
        var actions =
            new PlayerActionSnapshot(
                session,
                new SimulationTick(4),
                construction ?? [],
                0,
                production,
                unitProduction,
                logistics,
                supply,
                tactical);

        return new PresentationSnapshot(
            new SimulationTick(4),
            TimeSpan.FromMilliseconds(50),
            0,
            ReadOnlySpan<RenderInstance>.Empty,
            sessionId: session,
            playerActions: actions);
    }

    private static void Press(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                key));

    private static void Release(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyUp,
                key));
}
