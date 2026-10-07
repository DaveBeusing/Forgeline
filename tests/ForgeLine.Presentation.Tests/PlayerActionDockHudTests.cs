using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PlayerActionDockHudTests
{
    [Fact]
    public void InteractionLayoutProvidesStableModeCardAndFooterHits()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);

        HudRect tacticalButton =
            PlayerActionDockInteractionLayout.GetModeButtonRect(
                layout,
                5);
        Assert.True(
            PlayerActionDockInteractionLayout.TryHit(
                Center(tacticalButton),
                layout,
                isOpen: false,
                itemCount: 0,
                out PlayerActionDockHitTarget modeHit));
        Assert.Equal(
            PlayerActionDockControlKind.Mode,
            modeHit.Kind);
        Assert.Equal(
            PlayerActionPanelMode.Tactical,
            modeHit.Mode);

        HudRect firstCard =
            PlayerActionDockInteractionLayout.GetCardRect(
                layout,
                0);
        Assert.True(
            PlayerActionDockInteractionLayout.TryHit(
                Center(firstCard),
                layout,
                isOpen: true,
                itemCount: 3,
                out PlayerActionDockHitTarget cardHit));
        Assert.Equal(
            PlayerActionDockControlKind.Item,
            cardHit.Kind);
        Assert.Equal(
            0,
            cardHit.ItemIndex);

        HudRect activate =
            PlayerActionDockInteractionLayout.GetFooterButtonRect(
                layout,
                0);
        Assert.True(
            PlayerActionDockInteractionLayout.TryHit(
                Center(activate),
                layout,
                isOpen: true,
                itemCount: 3,
                out PlayerActionDockHitTarget footerHit));
        Assert.Equal(
            PlayerActionDockControlKind.Activate,
            footerHit.Kind);
    }

    [Fact]
    public void MissingKnownInputsDisableProductionAndUnitCards()
    {
        var production =
            new PlayerProductionFacilityActionReadModel(
                new EntityId(10, 1),
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
                        [])
                ],
                []);
        var units =
            new PlayerUnitProductionFacilityActionReadModel(
                new EntityId(11, 1),
                EntityId.Invalid,
                UnitId.None,
                UnitProductionStatus.Idle,
                UnitProductionBlockReason.None,
                0.0,
                [
                    new PlayerUnitProductionActionReadModel(
                        UnitIds.MainBattleTank,
                        "Main Battle Tank",
                        [
                            new PlayerActionResourceAmount(
                                ResourceIds.Steel,
                                "Steel",
                                20.0,
                                0.0)
                        ],
                        80)
                ],
                []);
        PlayerActionSnapshot actions =
            Snapshot(
                production: production,
                units: units);

        PlayerActionDockItemState productionState =
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Production,
                0,
                actions);
        PlayerActionDockItemState unitState =
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.UnitProduction,
                0,
                actions);

        Assert.False(
            productionState.CanActivate);
        Assert.Equal(
            "MISSING INPUT",
            productionState.DisabledReason);
        Assert.False(
            unitState.CanActivate);
        Assert.Equal(
            "MISSING INPUT",
            unitState.DisabledReason);
    }

    [Fact]
    public void TacticalDockExposesOnlyImplementedActions()
    {
        EntityId unit =
            new(20, 1);
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
        PlayerActionSnapshot actions =
            Snapshot(
                tactical: tactical);

        Assert.Equal(
            8,
            PlayerActionDockInteractionLayout.GetItemCount(
                PlayerActionPanelMode.Tactical,
                actions));

        string[] titles =
            Enumerable.Range(
                    0,
                    8)
                .Select(
                    index =>
                        PlayerActionDockHudModel.ResolveItemTitle(
                            PlayerActionPanelMode.Tactical,
                            index,
                            actions))
                .ToArray();

        Assert.Contains(
            "ATTACK",
            titles);
        Assert.Contains(
            "ATTACK MOVE",
            titles);
        Assert.Contains(
            "STOP",
            titles);
        Assert.Contains(
            "HOLD",
            titles);
        Assert.Contains(
            "RETREAT",
            titles);
        Assert.Contains(
            "FIRE MISSION",
            titles);
        Assert.Contains(
            "CANCEL FIRE",
            titles);
        Assert.Contains(
            "RECOVERY",
            titles);
        Assert.DoesNotContain(
            "PATROL",
            titles);
        Assert.DoesNotContain(
            "REPAIR",
            titles);

        Assert.Equal(
            "NO IDENTIFIED TARGET",
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Tactical,
                0,
                actions).DisabledReason);
        Assert.Equal(
            "NO ARTILLERY",
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Tactical,
                5,
                actions).DisabledReason);
        Assert.Equal(
            "NO ACTIVE MISSION",
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Tactical,
                6,
                actions).DisabledReason);
    }

    [Fact]
    public void FooterAvailabilityUsesSameModelAsControllerAndRenderer()
    {
        var logistics =
            new PlayerLogisticsActionReadModel(
                new EntityId(30, 1),
                [
                    new PlayerStockPolicyActionReadModel(
                        EntityId.Invalid,
                        ResourceIds.Fuel,
                        "Fuel",
                        10.0,
                        0.0,
                        0.0,
                        0.0,
                        LogisticsStockPriority.Normal,
                        false,
                        PlayerDistributionActionState.None,
                        LogisticsTransportRequestFailureReason.None,
                        LogisticsBottleneckReason.None,
                        EntityId.Invalid)
                ],
                null);
        PlayerActionSnapshot actions =
            Snapshot(
                logistics: logistics);
        var panel =
            new PlayerActionPanelView(
                PlayerActionPanelMode.Logistics,
                0,
                ProductionPriority.Normal,
                ProductionRequestMode.OneShot,
                0.0,
                LogisticsStockPriority.Normal,
                PlayerStockThresholdField.Target,
                0.0,
                0.0,
                0.0,
                false,
                0.0,
                0.0,
                false,
                0.0f,
                0.0f);

        Assert.True(
            PlayerActionDockHudModel.CanUseControl(
                PlayerActionDockControlKind.Activate,
                panel.Mode,
                panel.SelectedIndex,
                panel,
                actions));
        Assert.False(
            PlayerActionDockHudModel.CanUseControl(
                PlayerActionDockControlKind.Cancel,
                panel.Mode,
                panel.SelectedIndex,
                panel,
                actions));
        Assert.True(
            PlayerActionDockHudModel.CanUseControl(
                PlayerActionDockControlKind.CyclePrimary,
                panel.Mode,
                panel.SelectedIndex,
                panel,
                actions));
        Assert.True(
            PlayerActionDockHudModel.CanUseControl(
                PlayerActionDockControlKind.Increase,
                panel.Mode,
                panel.SelectedIndex,
                panel,
                actions));
    }

    [Fact]
    public void LayoutRemainsBoundedAcrossCompactAndScaledHud()
    {
        foreach ((int width, int height, uint dpi, float uiScale) in
                 new[]
                 {
                     (1280, 720, 96u, 1.0f),
                     (1920, 1080, 144u, 1.0f),
                     (2560, 1440, 96u, 1.35f)
                 })
        {
            GameplayHudLayout layout =
                GameplayHudLayout.Create(
                    width,
                    height,
                    dpi,
                    uiScale);

            Assert.False(
                layout.ActionDock.IsEmpty);
            Assert.True(
                PlayerActionDockInteractionLayout.MaximumVisibleItems(
                    layout) >
                0);

            for (int index = 0;
                 index <
                     PlayerActionDockInteractionLayout.ModeButtonCount;
                 index++)
            {
                HudRect rect =
                    PlayerActionDockInteractionLayout.GetModeButtonRect(
                        layout,
                        index);
                Assert.True(
                    rect.X >=
                    layout.ActionDock.X);
                Assert.True(
                    rect.Right <=
                    layout.ActionDock.Right +
                    0.01f);
            }
        }
    }


    [Fact]
    public void TechnologyDockMapsModeAndAuthoritativeBlockedReason()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        HudRect technologyButton =
            PlayerActionDockInteractionLayout.GetModeButtonRect(
                layout,
                6);

        Assert.True(
            PlayerActionDockInteractionLayout.TryHit(
                Center(technologyButton),
                layout,
                isOpen: false,
                itemCount: 0,
                out PlayerActionDockHitTarget modeHit));
        Assert.Equal(
            PlayerActionPanelMode.Technology,
            modeHit.Mode);

        var technology =
            new PlayerTechnologyActionReadModel(
                TechnologyIds.MechanizedSystems,
                "directorate.technology.mechanized_systems",
                "Mechanized Systems",
                TechnologyDomain.Warfare,
                TechnologyPhase.MechanizedWarfare,
                180,
                [
                    new PlayerActionResourceAmount(
                        ResourceIds.Steel,
                        "Steel",
                        140.0,
                        200.0)
                ],
                [
                    new PlayerTechnologyPrerequisiteReadModel(
                        TechnologyIds.IndustrialStandardization,
                        "Industrial Standardization",
                        false)
                ],
                BuildingIds.VehicleFactory,
                "Vehicle Factory",
                EntityId.Invalid,
                new EntityId(44, 1),
                1.0,
                0.0,
                PlayerTechnologyState.Locked,
                TechnologyResearchBlockReason.UnmetPrerequisite,
                0.0,
                EntityId.Invalid,
                [TechnologyCapabilityIds.MechanizedSystems]);
        PlayerActionSnapshot actions =
            Snapshot(
                technology: [technology]);

        PlayerActionDockItemState state =
            PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Technology,
                0,
                actions);

        Assert.False(
            state.CanActivate);
        Assert.Equal(
            "PREREQUISITE",
            state.DisabledReason);
        Assert.Equal(
            "TECH",
            PlayerActionDockHudModel.ResolveModeLabel(
                PlayerActionPanelMode.Technology));
        Assert.Equal(
            "H",
            PlayerActionDockHudModel.ResolveModeShortcut(
                PlayerActionPanelMode.Technology));
    }

    private static PlayerActionSnapshot Snapshot(
        PlayerProductionFacilityActionReadModel? production = null,
        PlayerUnitProductionFacilityActionReadModel? units = null,
        PlayerLogisticsActionReadModel? logistics = null,
        PlayerSupplyActionReadModel? supply = null,
        PlayerTacticalActionReadModel? tactical = null,
        IReadOnlyList<PlayerTechnologyActionReadModel>? technology = null) =>
        new(
            new SimulationSessionId(1),
            new SimulationTick(1),
            [],
            0,
            production,
            units,
            logistics,
            supply,
            tactical,
            technology);

    private static System.Numerics.Vector2 Center(
        in HudRect rect) =>
        new(
            rect.X +
                rect.Width *
                0.5f,
            rect.Y +
                rect.Height *
                0.5f);
}
