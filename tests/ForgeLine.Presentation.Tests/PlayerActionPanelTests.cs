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
    [Theory]
    [InlineData("threshold")]
    [InlineData("enabled")]
    [InlineData("priority")]
    [InlineData("removed")]
    [InlineData("recreated")]
    public void DirtyStockDraftReloadsChangedPolicyAndRequiresReviewBeforeApply(string change)
    {
        var policy = new PlayerStockPolicyActionReadModel(new(80, 1), ResourceIds.Steel, "Steel",
            0, 50, 100, 200, LogisticsStockPriority.Normal, true, default, default, default, default);
        var snapshot = Snapshot(policy);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        controller.Update(input, snapshot, 1600, 900);
        Key(PlatformKey.L); Key(PlatformKey.Right);
        Assert.Equal(100, View().StockMinimum);
        var current = change switch
        {
            "threshold" => policy with { DesiredMinimum = 25, DesiredTarget = 150, DesiredMaximum = 250 },
            "enabled" => policy with { Enabled = false },
            "priority" => policy with { Priority = LogisticsStockPriority.High },
            "removed" => policy with { PolicyEntity = EntityId.Invalid },
            _ => policy with { PolicyEntity = new(80, 2) }
        };
        snapshot = Snapshot(current);
        controller.Update(input, snapshot, 1600, 900);
        Assert.True(View().StockConflict);
        Assert.Equal(current.DesiredMinimum, View().StockMinimum);
        Assert.Equal(current.DesiredMaximum, View().StockMaximum);
        Assert.Contains("POLICY CHANGED", View().StockPreview);
        Assert.False(controller.TryTakeRequest(out _));
        Key(PlatformKey.Enter);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.False(View().StockConflict);
        Key(PlatformKey.Enter);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(current.DesiredMinimum, request.StockMinimum);
        Assert.Equal(current.DesiredMaximum, request.StockMaximum);
        Assert.Equal(current.Priority, request.StockPriority);
        Assert.True(request.Enabled);

        PresentationSnapshot Snapshot(PlayerStockPolicyActionReadModel row) =>
            CreateSnapshot(logistics: new PlayerLogisticsActionReadModel(new(73, 1), [row], null, 500) { IsCommandCore = true });
        PlayerActionPanelView View() => controller.CreateView(1600, 900, snapshot.PlayerActions);
        void Key(PlatformKey key)
        {
            Press(input, key); controller.Update(input, snapshot, 1600, 900);
            Release(input, key); controller.Update(input, snapshot, 1600, 900);
        }
    }

    [Fact]
    public void CleanStockDraftRefreshesAndOwnAppliedValuesDoNotBecomeAConflict()
    {
        var policy = new PlayerStockPolicyActionReadModel(new(80, 1), ResourceIds.Steel, "Steel",
            0, 50, 100, 200, LogisticsStockPriority.Normal, true, default, default, default, default);
        var snapshot = Snapshot(policy);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        controller.Update(input, snapshot, 1600, 900);
        Key(PlatformKey.L);
        policy = policy with { DesiredMinimum = 100, DesiredTarget = 200, DesiredMaximum = 250 };
        snapshot = Snapshot(policy);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(100, View().StockMinimum);
        Assert.Equal(250, View().StockMaximum);
        Assert.False(View().StockConflict);
        Key(PlatformKey.Right); Key(PlatformKey.Enter);
        Assert.True(controller.TryTakeRequest(out var request));
        snapshot = Snapshot(policy with { DesiredMinimum = request.StockMinimum,
            DesiredTarget = request.StockTarget, DesiredMaximum = request.StockMaximum });
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(View().StockConflict);
        Assert.Equal(request.StockMinimum, View().StockMinimum);

        PresentationSnapshot Snapshot(PlayerStockPolicyActionReadModel row) =>
            CreateSnapshot(logistics: new PlayerLogisticsActionReadModel(new(73, 1), [row], null, 500) { IsCommandCore = true });
        PlayerActionPanelView View() => controller.CreateView(1600, 900, snapshot.PlayerActions);
        void Key(PlatformKey key)
        {
            Press(input, key); controller.Update(input, snapshot, 1600, 900);
            Release(input, key); controller.Update(input, snapshot, 1600, 900);
        }
    }

    [Fact]
    public void LockedUnitActivationOpensItsTechnologyWithoutSubmittingProduction()
    {
        var facility = new PlayerUnitProductionFacilityActionReadModel(new(10, 1), default, default,
            default, default, 0, [new PlayerUnitProductionActionReadModel(UnitIds.CombatEngineer,
                "Combat Engineer", [], 100, false, TechnologyCapabilityIds.FieldEngineering)], []);
        var technology = TechnologyAction(new(11, 1), new(11, 1), PlayerTechnologyState.Available,
            TechnologyResearchBlockReason.None, default);
        var snapshot = CreateSnapshot(unitProduction: facility, technology: [technology]);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.U);
        controller.Update(input, snapshot, 1600, 900);
        Release(input, PlatformKey.U);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.UnitProduction, controller.Mode);
        Press(input, PlatformKey.Enter);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Technology, controller.Mode);
        Assert.Equal(0, controller.SelectedIndex);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Fact]
    public void ManualLogisticsKeyboardEditsUseDirectionalBoundsWithoutChangingSnapshot()
    {
        var policy = new PlayerStockPolicyActionReadModel(default, ResourceIds.Steel, "Steel",
            0, 50, 100, 150, LogisticsStockPriority.Normal, true, default, default, default, default);
        var logistics = new PlayerLogisticsActionReadModel(new(73, 1), [policy], null, 150);
        var snapshot = CreateSnapshot(logistics: logistics);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);

        Key(PlatformKey.L);
        Assert.Equal(100, View().StockTarget);
        Key(PlatformKey.Right);
        Assert.Equal(150, View().StockTarget);
        Key(PlatformKey.Right);
        Assert.Equal(150, View().StockTarget);
        Key(PlatformKey.Left);
        Assert.Equal(100, View().StockTarget);
        Key(PlatformKey.Left);
        Assert.Equal(50, View().StockTarget);
        Key(PlatformKey.Left);
        Assert.Equal(50, View().StockTarget);
        Assert.Equal(100, logistics.Policies[0].DesiredTarget);
        Assert.False(controller.TryTakeRequest(out _));
        Key(PlatformKey.Enter);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.SetStockPolicy, request.Kind);
        Assert.Equal(50, request.StockMinimum);
        Assert.Equal(50, request.StockTarget);
        Assert.Equal(150, request.StockMaximum);

        PlayerActionPanelView View() => controller.CreateView(1600, 900, snapshot.PlayerActions);
        void Key(PlatformKey key)
        {
            Press(input, key); controller.Update(input, snapshot, 1600, 900);
            Release(input, key); controller.Update(input, snapshot, 1600, 900);
        }
    }

    [Fact]
    public void LogisticsRadialStepAvailabilityMatchesChosenFieldAndDirection()
    {
        var policy = new PlayerStockPolicyActionReadModel(default, ResourceIds.Steel, "Steel",
            0, 50, 100, 100, LogisticsStockPriority.Normal, true, default, default, default, default);
        var logistics = new PlayerLogisticsActionReadModel(new(73, 1), [policy], null, 125);
        var snapshot = CreateSnapshot(logistics: logistics);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.L);
        controller.Update(input, snapshot, 1600, 900);
        var panel = controller.CreateView(1600, 900, snapshot.PlayerActions);
        Assert.False(PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Increase,
            PlayerActionPanelMode.Logistics, 0, panel, snapshot.PlayerActions));
        Assert.True(PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Decrease,
            PlayerActionPanelMode.Logistics, 0, panel, snapshot.PlayerActions));
        Assert.Equal("INCREASE MAX FIRST",
            PlayerActionDockHudModel.DecideStockAdjustment(panel, logistics, 1).Reason);
    }

    [Fact]
    public void UnconfiguredLogisticsBelowMinimumCapacityBlocksApplyAndEveryQuantityField()
    {
        var policy = new PlayerStockPolicyActionReadModel(EntityId.Invalid, ResourceIds.Steel, "Steel",
            0, 0, 0, 0, LogisticsStockPriority.Normal, true, default, default, default, default);
        var logistics = new PlayerLogisticsActionReadModel(new(73, 1), [policy], null, 49);
        var snapshot = CreateSnapshot(logistics: logistics);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);

        Key(PlatformKey.L);
        Assert.False(policy.HasPolicy);
        Assert.Equal("CAPACITY BELOW 50", PlayerActionDockHudModel.ResolveItemState(
            PlayerActionPanelMode.Logistics, 0, snapshot.PlayerActions).DisabledReason);

        foreach (var field in new[]
                 { PlayerStockThresholdField.Target, PlayerStockThresholdField.Maximum, PlayerStockThresholdField.Minimum })
        {
            var panel = View();
            Assert.Equal(field, panel.StockThresholdField);
            foreach (var control in new[]
                     { PlayerActionDockControlKind.Activate, PlayerActionDockControlKind.Increase, PlayerActionDockControlKind.Decrease })
                Assert.False(PlayerActionDockHudModel.CanUseControl(
                    control, panel.Mode, panel.SelectedIndex, panel, snapshot.PlayerActions));

            Key(PlatformKey.Right);
            Assert.Equal((0.0, 0.0, 0.0), (View().StockMinimum, View().StockTarget, View().StockMaximum));
            Key(PlatformKey.Left);
            Assert.Equal((0.0, 0.0, 0.0), (View().StockMinimum, View().StockTarget, View().StockMaximum));
            Key(PlatformKey.Enter);
            Assert.False(controller.TryTakeRequest(out _));
            Key(PlatformKey.M);
        }

        Assert.Equal((0.0, 0.0, 0.0),
            (logistics.Policies[0].DesiredMinimum, logistics.Policies[0].DesiredTarget, logistics.Policies[0].DesiredMaximum));
        Assert.False(logistics.Policies[0].HasPolicy);

        PlayerActionPanelView View() => controller.CreateView(1600, 900, snapshot.PlayerActions);
        void Key(PlatformKey key)
        {
            Press(input, key); controller.Update(input, snapshot, 1600, 900);
            Release(input, key); controller.Update(input, snapshot, 1600, 900);
        }
    }

    [Fact]
    public void StaticProviderStockDoesNotOfferRecipientCommandsByMouseOrKeyboard()
    {
        var supply = new PlayerSupplyActionReadModel(new(50,1),default,1,1,true,.5,.5,default,default,0,0,default)
            { IsStaticProvider = true, HasLocalProviderInventory = true, LocalProviderFuel = 75, LocalProviderAmmunition = 40 };
        var snapshot = CreateSnapshot(supply:supply);
        var controller = new PlayerActionPanelController(); var input = new InputState();
        controller.Update(input,snapshot,1600,900);
        foreach (var key in new[]{ PlatformKey.Y,PlatformKey.T,PlatformKey.M,PlatformKey.Left,PlatformKey.Right,PlatformKey.Enter })
        { Press(input,key); controller.Update(input,snapshot,1600,900); Release(input,key); controller.Update(input,snapshot,1600,900); }
        Assert.False(controller.TryTakeRequest(out _));
        Assert.False(PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Supply,0,snapshot.PlayerActions).CanActivate);
        Assert.False(PlayerActionDockHudModel.CanCyclePrimary(PlayerActionPanelMode.Supply,snapshot.PlayerActions));
        Assert.False(PlayerActionDockHudModel.CanCycleSecondary(PlayerActionPanelMode.Supply,snapshot.PlayerActions));
    }

    [Fact]
    public void ProductionInputsPreviewPreparesEachResourceWithoutDispatchingAndResetsAcrossEntities()
    {
        var steel = new PlayerStockPolicyActionReadModel(default, ResourceIds.Steel, "Steel", 0, 5, 10, 20,
            LogisticsStockPriority.Normal, true, default, default, default, default);
        var fuel = steel with { ResourceId = ResourceIds.Fuel, DisplayName = "Fuel" };
        var requirement = new PlayerProductionRequirement(2, 7, "Vehicle", [new(ResourceIds.Steel, "Steel", 70, 0), new(ResourceIds.Fuel, "Fuel", 60, 0)]);
        var logistics = new PlayerLogisticsActionReadModel(new(73, 1), [steel, fuel], null, 500, requirements: [requirement]);
        var snapshot = CreateSnapshot(logistics: logistics);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Key(PlatformKey.L); Key(PlatformKey.Tab); Key(PlatformKey.Tab);
        Assert.Equal(2, controller.SelectedIndex);
        Key(PlatformKey.Right);
        Assert.Equal(2, controller.CreateView(1600,900,snapshot.PlayerActions).LogisticsSteps);
        Key(PlatformKey.Enter);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.Equal(0, controller.SelectedIndex);
        Assert.Equal(140, controller.CreateView(1600,900,snapshot.PlayerActions).StockTarget);
        Key(PlatformKey.Enter);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.SetStockPolicy, request.Kind);
        Assert.Equal(140, request.StockTarget);
        Assert.Equal(10, logistics.Policies[0].DesiredTarget);
        Key(PlatformKey.Tab);
        Assert.Equal(120, controller.CreateView(1600,900,snapshot.PlayerActions).StockTarget);
        snapshot = CreateSnapshot();
        controller.Update(input,snapshot,1600,900);
        snapshot = CreateSnapshot(logistics: logistics);
        controller.Update(input,snapshot,1600,900);
        Assert.Equal(10, controller.CreateView(1600,900,snapshot.PlayerActions).StockTarget);
        snapshot = CreateSnapshot(logistics: new PlayerLogisticsActionReadModel(new(74,2), [steel,fuel], null,500,requirements:[requirement]));
        controller.Update(input,snapshot,1600,900);
        Assert.Equal(10, controller.CreateView(1600,900,snapshot.PlayerActions).StockTarget);
        void Key(PlatformKey key)
        { Press(input,key); controller.Update(input,snapshot,1600,900); Release(input,key); controller.Update(input,snapshot,1600,900); }
    }

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
    public void LogisticsFiftyUnitControlsPreserveLegacyPoliciesAndNeverDispatchFromOrderRows()
    {
        var policy = new PlayerStockPolicyActionReadModel(new(71, 1), ResourceIds.Fuel, "Fuel",
            10, 0, 75, 175, LogisticsStockPriority.Normal, true,
            PlayerDistributionActionState.Pending, default, default, default);
        var order = new PlayerLogisticsOrderReadModel(new(99), ResourceIds.Fuel, "Fuel",
            LogisticsTransportRequestState.RetryPending, LogisticsTransportRequestFailureReason.NoTruckAvailable,
            LogisticsBottleneckReason.InsufficientTruckCapacity, 75, 0, default, default, default, "", "");
        var logistics = new PlayerLogisticsActionReadModel(new(70, 1), [policy], null, 300, orders: [order])
            { TotalOrderCount = 1 };
        var snapshot = CreateSnapshot(logistics: logistics);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        controller.Update(input, snapshot, 1600, 900);

        void Key(PlatformKey key)
        {
            Press(input, key); controller.Update(input, snapshot, 1600, 900);
            Release(input, key); controller.Update(input, snapshot, 1600, 900);
        }

        Key(PlatformKey.L);
        var unchanged = controller.CreateView(1600, 900, snapshot.PlayerActions);
        Assert.Equal(0, unchanged.StockMinimum);
        Assert.Equal(75, unchanged.StockTarget);
        Assert.Equal(175, unchanged.StockMaximum);
        Key(PlatformKey.Right);
        var raised = controller.CreateView(1600, 900, snapshot.PlayerActions);
        Assert.Equal(100, raised.StockTarget);
        Assert.Equal(0, raised.StockMinimum);
        Assert.Equal(175, raised.StockMaximum);
        Key(PlatformKey.Left);
        Assert.Equal(50, controller.CreateView(1600, 900, snapshot.PlayerActions).StockTarget);
        Key(PlatformKey.Tab);
        Assert.Equal(1, controller.SelectedIndex);
        Key(PlatformKey.Right);
        Key(PlatformKey.Enter);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.Equal(75, logistics.Policies[0].DesiredTarget);
        Assert.Equal(0, logistics.Policies[0].DesiredMinimum);
        Assert.Equal(175, logistics.Policies[0].DesiredMaximum);
    }

    [Theory]
    [InlineData("missing-actions")]
    [InlineData("missing-supply")]
    [InlineData("stale-actions")]
    [InlineData("selection-changed")]
    public void SupplyEditorIgnoresUnavailableActionsAndResynchronizesSameEntity(string unavailableReason)
    {
        var unit = new EntityId(80, 1);
        var supply = new PlayerSupplyActionReadModel(unit, BattlefieldSupplyStatus.LowSupply,
            0.2, 0.3, true, 0.2, 0.2, new EntityId(81, 1),
            PlayerSupplyProviderState.Traveling, 100, 50, ResupplyProviderRejection.None);
        var available = CreateSnapshot(supply: supply);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        controller.Update(input, available, 1600, 900);
        Press(input, PlatformKey.Y);
        controller.Update(input, available, 1600, 900);
        Release(input, PlatformKey.Y);
        controller.Update(input, available, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Supply, controller.Mode);

        var unavailable = unavailableReason switch
        {
            "missing-actions" => new PresentationSnapshot(new SimulationTick(4),
                TimeSpan.FromMilliseconds(50), 0, ReadOnlySpan<RenderInstance>.Empty,
                sessionId: available.SessionId),
            "missing-supply" => CreateSnapshot(),
            "stale-actions" => new PresentationSnapshot(new SimulationTick(5),
                TimeSpan.FromMilliseconds(50), 0, ReadOnlySpan<RenderInstance>.Empty,
                sessionId: available.SessionId, playerActions: available.PlayerActions),
            _ => available
        };
        SelectionSet? selection = null;
        if (unavailableReason == "selection-changed")
        {
            selection = new SelectionSet();
            selection.SetSingle(new EntityId(99, 1));
        }

        controller.Update(input, unavailable, 1600, 900, currentSelection: selection);
        foreach (var key in new[] { PlatformKey.T, PlatformKey.M, PlatformKey.Right,
                     PlatformKey.Left, PlatformKey.Enter, PlatformKey.C })
        {
            Press(input, key);
            controller.Update(input, unavailable, 1600, 900, currentSelection: selection);
            Assert.False(controller.TryTakeRequest(out _));
            Release(input, key);
            controller.Update(input, unavailable, 1600, 900, currentSelection: selection);
        }

        var refreshed = CreateSnapshot(supply: supply with
        {
            AutomaticEnabled = false, AutomaticFuelThreshold = 0.65,
            AutomaticAmmunitionThreshold = 0.75, Priority = BattlefieldSupplyPriority.Critical
        });
        controller.Update(input, refreshed, 1600, 900);
        var view = controller.CreateView(1600, 900, refreshed.PlayerActions);
        Assert.Equal(PlayerActionPanelMode.Supply, controller.Mode);
        Assert.False(view.AutomaticResupplyEnabled);
        Assert.Equal(0.65, view.AutomaticFuelThreshold);
        Assert.Equal(0.75, view.AutomaticAmmunitionThreshold);
        Assert.Equal(BattlefieldSupplyPriority.Critical, controller.SupplyPriority);
        Assert.False(controller.TryTakeRequest(out _));
        Press(input, PlatformKey.Enter);
        controller.Update(input, refreshed, 1600, 900);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.SetAutomaticResupplyPolicy, request.Kind);
        Assert.Equal(unit, request.Facility);
        Assert.Equal(0.65, request.AutomaticFuelThreshold);
        Assert.Equal(0.75, request.AutomaticAmmunitionThreshold);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutomaticProcessorsHideProcessAndIgnoreItsShortcut(bool automatic)
    {
        var facility = new PlayerProductionFacilityActionReadModel(new(10, 1), default, default,
            ProductionStatus.NoInput, ProductionBlockReason.NoInput, 0,
            [new PlayerProductionRecipeActionReadModel(RecipeIds.Steel, "Steel", [], [])], [], isAutomatic: automatic);
        var snapshot = CreateSnapshot(production: facility);
        Assert.Equal(automatic ? 0 : 1,
            PlayerActionDockInteractionLayout.GetItemCount(PlayerActionPanelMode.Production, snapshot.PlayerActions));
        Assert.Equal(!automatic, PlayerActionDockHudModel.CanCyclePrimary(PlayerActionPanelMode.Production, snapshot.PlayerActions));
        Assert.Equal(!automatic, PlayerActionDockHudModel.CanCycleSecondary(PlayerActionPanelMode.Production, snapshot.PlayerActions));
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, GameplayBindingRegistry.Default.Key(GameplayAction.Process));
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(automatic ? PlayerActionPanelMode.Closed : PlayerActionPanelMode.Production, controller.Mode);
        Press(input, PlatformKey.Enter);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(!automatic, controller.TryTakeRequest(out _));
    }

    [Fact]
    public void OperationsCanResumePausedAutomaticWorkWithoutOfferingRecipeOrders()
    {
        var request = new PlayerProductionRequestReadModel(new(11, 1), RecipeIds.Steel, "Steel",
            ProductionPriority.Normal, ProductionRequestMode.Repeat, true, true);
        var facility = new PlayerProductionFacilityActionReadModel(new(10, 1), request.RequestEntity, RecipeIds.Steel,
            ProductionStatus.Paused, ProductionBlockReason.Paused, 0,
            [new PlayerProductionRecipeActionReadModel(RecipeIds.Steel, "Steel", [], [])], [request], isAutomatic: true);
        var snapshot = CreateSnapshot(production: facility);
        Assert.Empty(facility.QueueRecipes);
        Assert.False(PlayerActionDockHudModel.CanCancel(PlayerActionPanelMode.Production, 0, snapshot.PlayerActions));
        Assert.Single(facility.Recipes); // Recipe inputs remain available to stock-policy inspection.
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        controller.OpenOperationsControls(PlayerActionPanelMode.Production);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal("RESUME", PlayerActionDockHudModel.ResolvePrimaryActionLabel(PlayerActionPanelMode.Production, 0, snapshot.PlayerActions));
        Press(input, PlatformKey.Enter);
        controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.TryTakeRequest(out var action));
        Assert.Equal(PlayerActionRequestKind.SetProductionPaused, action.Kind);
        Assert.Equal(request.RequestEntity, action.RequestEntity);
        Assert.False(action.Paused);
    }

    [Theory]
    [InlineData(1, GameplayAction.AttackMove)]
    [InlineData(2, GameplayAction.Stop)]
    [InlineData(3, GameplayAction.Hold)]
    [InlineData(4, GameplayAction.Retreat)]
    public void DirectTacticalBindingMatchesMenuAndConsumesShortTapOnce(int index, GameplayAction action)
    {
        var selected = new EntityId(101, 1);
        var tactical = new PlayerTacticalActionReadModel([selected], 1, 1, 0, 0, 0, false, false, default, default, [], []);
        var snapshot = CreateSnapshot(tactical: tactical);
        var menu = new PlayerActionPanelController();
        var menuInput = new InputState();
        menu.Update(menuInput, snapshot, 1600, 900);
        Tap(PlatformKey.K);
        for (int i = 0; i < index; i++) Tap(PlatformKey.Tab);
        Tap(PlatformKey.Enter);
        Assert.True(menu.TryTakeRequest(out var expected));

        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(action, PlatformKey.J));
        var controller = new PlayerActionPanelController(bindings);
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.J); Release(input, PlatformKey.J);
        controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.TryTakeRequest(out var actual));
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.TacticalEntities, actual.TacticalEntities);
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.Equal("J", bindings.TacticalShortcut(index));

        void Tap(PlatformKey key)
        {
            menuInput.BeginFrame(); Press(menuInput, key); menu.Update(menuInput, snapshot, 1600, 900);
            menuInput.BeginFrame(); Release(menuInput, key); menu.Update(menuInput, snapshot, 1600, 900);
        }
    }

    [Fact]
    public void BlockedTacticalShortcutRequiresReleaseAndFreshPress()
    {
        var selected = new EntityId(101, 1);
        var snapshot = CreateSnapshot(tactical: new PlayerTacticalActionReadModel([selected], 1, 1, 0, 0, 0, false, false, default, default, [], []));
        var controller = new PlayerActionPanelController(new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Stop, PlatformKey.J)));
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.J);
        controller.Update(input, snapshot, 1600, 900, globalShortcutsBlocked: true);
        input.BeginFrame();
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
        Release(input, PlatformKey.J); controller.Update(input, snapshot, 1600, 900);
        Press(input, PlatformKey.J); controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.TryTakeRequest(out _));
    }

    [Theory]
    [InlineData(PlatformKey.LeftShift)]
    [InlineData(PlatformKey.LeftControl)]
    public void ReleasedModifierStillBlocksBriefTacticalChord(PlatformKey modifier)
    {
        var selected = new EntityId(101, 1);
        var snapshot = CreateSnapshot(tactical: new PlayerTacticalActionReadModel([selected], 1, 1, 0, 0, 0, false, false, default, default, [], []));
        var controller = new PlayerActionPanelController(new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.Stop, PlatformKey.J)));
        var input = new InputState();
        controller.Update(input, snapshot, 1600, 900);
        Press(input, modifier); Press(input, PlatformKey.J);
        Release(input, PlatformKey.J); Release(input, modifier);
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
        input.BeginFrame(); Press(input, PlatformKey.J);
        controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.TryTakeRequest(out _));
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


