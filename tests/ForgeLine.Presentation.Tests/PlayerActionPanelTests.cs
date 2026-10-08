using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
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
    public void PointerSelectionAndActivationAreCapturedAndFocusLossDoesNotRepeatModeToggle()
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

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        HudRect card =
            PlayerActionDockInteractionLayout.GetCardRect(
                layout,
                0);
        HudRect activate =
            PlayerActionDockInteractionLayout.GetFooterButtonRect(
                layout,
                0);

        Click(
            controller,
            input,
            snapshot,
            Center(card));

        Assert.True(
            controller.PointerCaptured);
        Assert.Equal(
            0,
            controller.SelectedIndex);
        Assert.False(
            controller.TryTakeRequest(
                out _));

        Click(
            controller,
            input,
            snapshot,
            Center(activate));

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.BeginBuildingPlacement,
            request.Kind);
        Assert.Equal(
            BuildingIds.PowerPlant,
            request.BuildingId);
        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
    }

    [Fact]
    public void PointerCannotActivateDisabledProductionCard()
    {
        EntityId facility =
            new(52, 1);
        var production =
            new PlayerProductionFacilityActionReadModel(
                facility,
                EntityId.Invalid,
                RecipeId.None,
                ProductionStatus.Idle,
                ProductionBlockReason.None,
                0.0,
                [
                    new PlayerProductionRecipeActionReadModel(
                        RecipeIds.Steel,
                        "Steel",
                        [
                            new PlayerActionResourceAmount(
                                ResourceIds.FerrousOre,
                                "Ferrous Ore",
                                10.0,
                                0.0)
                        ],
                        [
                            new PlayerActionResourceAmount(
                                ResourceIds.Steel,
                                "Steel",
                                10.0,
                                0.0)
                        ])
                ],
                []);
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

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        Click(
            controller,
            input,
            snapshot,
            Center(
                PlayerActionDockInteractionLayout.GetCardRect(
                    layout,
                    0)));
        Click(
            controller,
            input,
            snapshot,
            Center(
                PlayerActionDockInteractionLayout.GetFooterButtonRect(
                    layout,
                    0)));

        Assert.True(
            controller.PointerCaptured);
        Assert.False(
            controller.TryTakeRequest(
                out _));
        Assert.Equal(
            "MISSING INPUT",
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Production,
                0,
                snapshot.PlayerActions).DisabledReason);
    }

    [Fact]
    public void SessionReplacementClearsOpenDockAndPendingRequest()
    {
        PresentationSnapshot first =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ],
                sessionValue: 101);
        PresentationSnapshot replacement =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ],
                sessionValue: 202);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            first,
            1600,
            900);
        Press(input, PlatformKey.B);
        controller.Update(
            input,
            first,
            1600,
            900);
        Release(input, PlatformKey.B);
        controller.Update(
            input,
            first,
            1600,
            900);
        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            first,
            1600,
            900);

        controller.Update(
            input,
            replacement,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
        Assert.False(
            controller.PointerCaptured);
        Assert.False(
            controller.TryTakeRequest(
                out _));
    }

    [Fact]
    public void StaleContextCannotSubmitAction()
    {
        EntityId facility =
            new(53, 1);
        var production =
            new PlayerProductionFacilityActionReadModel(
                facility,
                EntityId.Invalid,
                RecipeId.None,
                ProductionStatus.Idle,
                ProductionBlockReason.None,
                0.0,
                [
                    new PlayerProductionRecipeActionReadModel(
                        RecipeIds.Steel,
                        "Steel",
                        [],
                        [])
                ],
                []);
        PresentationSnapshot active =
            CreateSnapshot(
                production: production);
        PresentationSnapshot stale =
            CreateSnapshot();
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            active,
            1600,
            900);
        Press(input, PlatformKey.P);
        controller.Update(
            input,
            active,
            1600,
            900);
        Release(input, PlatformKey.P);
        controller.Update(
            input,
            stale,
            1600,
            900);
        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            stale,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Production,
            controller.Mode);
        Assert.False(
            controller.TryTakeRequest(
                out _));
    }

    [Fact]
    public void TerminalMatchClosesDockAndRejectsKeyboardAndPointerActions()
    {
        PresentationSnapshot active =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ]);
        PresentationSnapshot terminal =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ],
                terminal: true);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            active,
            1600,
            900);
        Press(input, PlatformKey.B);
        controller.Update(
            input,
            active,
            1600,
            900);
        Assert.Equal(
            PlayerActionPanelMode.Construction,
            controller.Mode);

        Release(input, PlatformKey.B);
        controller.Update(
            input,
            terminal,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
        Assert.False(
            controller.PointerCaptured);

        Press(input, PlatformKey.B);
        input.Apply(
            PlatformInputEvent.PointerMoved(
                800,
                760));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                800,
                760));
        controller.Update(
            input,
            terminal,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
        Assert.False(
            controller.PointerCaptured);
        Assert.False(
            controller.TryTakeRequest(
                out _));
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

        Press(input, PlatformKey.T);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest priorityRequest));
        Assert.Equal(
            PlayerActionRequestKind.SetSupplyPriority,
            priorityRequest.Kind);
        Assert.Equal(unit, priorityRequest.Facility);
        Assert.Equal(
            BattlefieldSupplyPriority.High,
            priorityRequest.SupplyPriority);

        Release(input, PlatformKey.T);
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
    public void TacticalPaletteSubmitsRecoveryAwareRetreat()
    {
        EntityId unit =
            new(89, 1);
        var tactical =
            new PlayerTacticalActionReadModel(
                [unit],
                requestedSelectionCount: 1,
                combatEligibleCount: 1,
                rejectedSelectionCount: 0,
                criticalSupplyCount: 1,
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
        var input =
            new InputState();
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

        for (int index = 0;
             index < 7;
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
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SubmitRetreatToRecovery,
            request.Kind);
        Assert.Equal(
            new[] { unit },
            request.TacticalEntities);
        Assert.Equal(
            FormationTemplate.Column,
            request.TacticalFormation);
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
                targets: [new PlayerTacticalTargetReadModel(new(91, 1), default,
                    Vector3.Zero, IntelligenceState.Identified, SimulationTick.Zero, 1)],
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


    [Fact]
    public void TechnologyPaletteCreatesStartAndCancelRequests()
    {
        EntityId facility =
            new(120, 1);
        EntityId sourceInventory =
            new(121, 1);
        EntityId activeRequest =
            new(122, 1);
        var input =
            new InputState();
        var controller =
            new PlayerActionPanelController();
        PresentationSnapshot availableSnapshot =
            CreateSnapshot(
                technology:
                [
                    TechnologyAction(
                        facility,
                        sourceInventory,
                        PlayerTechnologyState.Available,
                        TechnologyResearchBlockReason.None,
                        EntityId.Invalid)
                ]);

        controller.Update(
            input,
            availableSnapshot,
            1600,
            900);
        Press(
            input,
            PlatformKey.H);
        controller.Update(
            input,
            availableSnapshot,
            1600,
            900);
        Release(
            input,
            PlatformKey.H);
        controller.Update(
            input,
            availableSnapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Technology,
            controller.Mode);

        Press(
            input,
            PlatformKey.Enter);
        controller.Update(
            input,
            availableSnapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest start));
        Assert.Equal(
            PlayerActionRequestKind.StartTechnologyResearch,
            start.Kind);
        Assert.Equal(
            TechnologyIds.IndustrialStandardization,
            start.TechnologyId);
        Assert.Equal(
            facility,
            start.Facility);
        Assert.Equal(
            sourceInventory,
            start.TechnologySourceInventory);

        var cancelInput =
            new InputState();
        var cancelController =
            new PlayerActionPanelController();
        PresentationSnapshot researchingSnapshot =
            CreateSnapshot(
                technology:
                [
                    TechnologyAction(
                        facility,
                        sourceInventory,
                        PlayerTechnologyState.Researching,
                        TechnologyResearchBlockReason.None,
                        activeRequest)
                ]);

        cancelController.Update(
            cancelInput,
            researchingSnapshot,
            1600,
            900);
        Press(
            cancelInput,
            PlatformKey.H);
        cancelController.Update(
            cancelInput,
            researchingSnapshot,
            1600,
            900);
        Release(
            cancelInput,
            PlatformKey.H);
        cancelController.Update(
            cancelInput,
            researchingSnapshot,
            1600,
            900);
        Press(
            cancelInput,
            PlatformKey.C);
        cancelController.Update(
            cancelInput,
            researchingSnapshot,
            1600,
            900);

        Assert.True(
            cancelController.TryTakeRequest(
                out PlayerActionRequest cancel));
        Assert.Equal(
            PlayerActionRequestKind.CancelTechnologyResearch,
            cancel.Kind);
        Assert.Equal(
            activeRequest,
            cancel.RequestEntity);
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


    [Fact]
    public void TechnologyPaletteExposesHoverAndPressedVisualState()
    {
        EntityId facility =
            new(130, 1);
        EntityId sourceInventory =
            new(131, 1);
        var input =
            new InputState();
        var controller =
            new PlayerActionPanelController();
        PresentationSnapshot snapshot =
            CreateSnapshot(
                technology:
                [
                    TechnologyAction(
                        facility,
                        sourceInventory,
                        PlayerTechnologyState.Available,
                        TechnologyResearchBlockReason.None,
                        EntityId.Invalid)
                ]);

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(
            input,
            PlatformKey.H);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(
            input,
            PlatformKey.H);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        HudRect card =
            PlayerActionDockInteractionLayout.GetCardRect(
                layout,
                0);
        int x =
            checked((int)MathF.Round(
                card.X +
                card.Width * 0.5f));
        int y =
            checked((int)MathF.Round(
                card.Y +
                card.Height * 0.5f));

        input.Apply(
            PlatformInputEvent.PointerMoved(
                x,
                y));
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        PlayerActionPanelView hovered =
            controller.CreateView(
                1600,
                900,
                snapshot.PlayerActions);

        Assert.Equal(
            0,
            hovered.HoveredIndex);
        Assert.False(
            hovered.PointerPressed);

        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                x,
                y));
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        PlayerActionPanelView pressed =
            controller.CreateView(
                1600,
                900,
                snapshot.PlayerActions);

        Assert.Equal(
            0,
            pressed.HoveredIndex);
        Assert.True(
            pressed.PointerPressed);
    }


    private static PlayerTechnologyActionReadModel TechnologyAction(
        EntityId facility,
        EntityId sourceInventory,
        PlayerTechnologyState state,
        TechnologyResearchBlockReason blockReason,
        EntityId activeRequest) =>
        new(
            TechnologyIds.IndustrialStandardization,
            "directorate.technology.industrial_standardization",
            "Industrial Standardization",
            TechnologyDomain.Industry,
            TechnologyPhase.IndustrialFoundation,
            120,
            [
                new PlayerActionResourceAmount(
                    ResourceIds.Steel,
                    "Steel",
                    80.0,
                    200.0)
            ],
            [],
            BuildingIds.CommandCore,
            "Command Core",
            facility,
            sourceInventory,
            1.0,
            1.0,
            state,
            blockReason,
            state == PlayerTechnologyState.Researching
                ? 0.25
                : 0.0,
            activeRequest,
            [TechnologyCapabilityIds.FieldEngineering]);

    private static PresentationSnapshot CreateSnapshot(
        IReadOnlyList<PlayerConstructionActionReadModel>? construction = null,
        PlayerProductionFacilityActionReadModel? production = null,
        PlayerUnitProductionFacilityActionReadModel? unitProduction = null,
        PlayerLogisticsActionReadModel? logistics = null,
        PlayerSupplyActionReadModel? supply = null,
        PlayerTacticalActionReadModel? tactical = null,
        IReadOnlyList<PlayerTechnologyActionReadModel>? technology = null,
        ulong sessionValue = 101,
        bool terminal = false)
    {
        var session =
            new SimulationSessionId(
                sessionValue);
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
                tactical,
                technology);

        PlayerExperienceSnapshot? experience =
            terminal
                ? new PlayerExperienceSnapshot(
                    new PlayerId(1),
                    new SimulationTick(4),
                    PlayerMatchStatus.Victory,
                    new PlayerId(1),
                    default,
                    default,
                    default,
                    PlayerSelectionSummary.Empty,
                    PlayerAlertState.None,
                    0,
                    0,
                    PlayerCommandFeedback.None,
                    default)
                : null;

        return new PresentationSnapshot(
            new SimulationTick(4),
            TimeSpan.FromMilliseconds(50),
            0,
            ReadOnlySpan<RenderInstance>.Empty,
            sessionId: session,
            playerExperience:
                experience,
            playerActions: actions);
    }

    private static Vector2 Center(
        in HudRect rect) =>
        new(
            rect.X +
                rect.Width *
                0.5f,
            rect.Y +
                rect.Height *
                0.5f);

    private static void Click(
        PlayerActionPanelController controller,
        InputState input,
        PresentationSnapshot snapshot,
        Vector2 position)
    {
        input.Apply(
            PlatformInputEvent.PointerMoved(
                checked((int)MathF.Round(position.X)),
                checked((int)MathF.Round(position.Y))));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                checked((int)MathF.Round(position.X)),
                checked((int)MathF.Round(position.Y))));
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonUp,
                PlatformMouseButton.Left,
                checked((int)MathF.Round(position.X)),
                checked((int)MathF.Round(position.Y))));
        controller.Update(
            input,
            snapshot,
            1600,
            900);
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
