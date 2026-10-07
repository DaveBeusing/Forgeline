using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourcePowerAlertHudTests
{
    [Fact]
    public void ResourceMappingUsesOnlyAuthoritativePlayerSummaryResources()
    {
        var resources =
            new PlayerResourceSummary(
                FerrousOre: 10.0,
                Volatiles: 20.0,
                Silicates: 30.0,
                Steel: 40.0,
                Fuel: 50.0,
                Electronics: 60.0,
                Ammunition: 70.0);
        RtsUiIcon[] expectedIcons =
        [
            RtsUiIcon.ResourceFerrousOre,
            RtsUiIcon.ResourceVolatiles,
            RtsUiIcon.ResourceSilicates,
            RtsUiIcon.ResourceSteel,
            RtsUiIcon.ResourceFuel,
            RtsUiIcon.ResourceElectronics,
            RtsUiIcon.ResourceAmmunition
        ];
        double[] expectedQuantities =
        [
            10.0,
            20.0,
            30.0,
            40.0,
            50.0,
            60.0,
            70.0
        ];

        Assert.Equal(
            expectedIcons.Length,
            ResourcePowerHudModel.AuthoritativeResourceCount);

        for (int index = 0;
             index < expectedIcons.Length;
             index++)
        {
            Assert.Equal(
                expectedIcons[index],
                ResourcePowerHudModel.GetResourceIcon(
                    index));
            Assert.Equal(
                expectedQuantities[index],
                ResourcePowerHudModel.GetResourceQuantity(
                    resources,
                    index));
        }

        Assert.DoesNotContain(
            RtsUiIcon.ResourceRareElements,
            expectedIcons);
    }

    [Theory]
    [InlineData(940.0f, 94.0f, 7)]
    [InlineData(376.0f, 94.0f, 4)]
    [InlineData(20.0f, 94.0f, 0)]
    [InlineData(940.0f, 0.0f, 0)]
    public void ResourceOverflowIsBoundedByAvailableWidth(
        float availableWidth,
        float cellWidth,
        int expected)
    {
        Assert.Equal(
            expected,
            ResourcePowerHudModel.ResolveVisibleResourceCount(
                availableWidth,
                cellWidth));
    }

    [Fact]
    public void PowerStateUsesAuthoritativeConstraintSummary()
    {
        var stable =
            new PlayerPowerSummary(
                default,
                Generation: 120.0,
                Demand: 80.0,
                AllocatedPower: 80.0,
                Deficit: 0.0,
                BrownoutConsumers: 0,
                OfflineConsumers: 0);
        var constrained =
            new PlayerPowerSummary(
                default,
                Generation: 80.0,
                Demand: 120.0,
                AllocatedPower: 80.0,
                Deficit: 40.0,
                BrownoutConsumers: 2,
                OfflineConsumers: 1);

        Assert.Equal(
            "STABLE",
            ResourcePowerHudModel.ResolvePowerStateLabel(
                stable));
        Assert.Equal(
            "CONSTRAINED",
            ResourcePowerHudModel.ResolvePowerStateLabel(
                constrained));
    }

    [Fact]
    public void AlertSeverityHasExplicitSemanticMapping()
    {
        Assert.Equal(
            HudAlertSeverity.Warning,
            ResourcePowerHudModel.ResolveAlertSeverity(
                PlayerAlertState.LowPower));
        Assert.Equal(
            HudAlertSeverity.Warning,
            ResourcePowerHudModel.ResolveAlertSeverity(
                PlayerAlertState.ProductionBlocked));
        Assert.Equal(
            HudAlertSeverity.Critical,
            ResourcePowerHudModel.ResolveAlertSeverity(
                PlayerAlertState.SupplyCritical));
        Assert.Equal(
            HudAlertSeverity.Critical,
            ResourcePowerHudModel.ResolveAlertSeverity(
                PlayerAlertState.CommandCoreDamaged));
        Assert.Equal(
            HudAlertSeverity.Critical,
            ResourcePowerHudModel.ResolveAlertSeverity(
                PlayerAlertState.CommandCoreDestroyed));
    }

    [Fact]
    public void CommandFeedbackExpiryUsesSimulationTicks()
    {
        PlayerCommandFeedback feedback =
            CreateFeedback(
                resolvedAtTick: 100);

        Assert.False(
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                CreateExperience(
                    tick: 99,
                    feedback)));
        Assert.True(
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                CreateExperience(
                    tick: 100,
                    feedback)));
        Assert.True(
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                CreateExperience(
                    tick: 180,
                    feedback)));
        Assert.False(
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                CreateExperience(
                    tick: 181,
                    feedback)));
    }

    [Fact]
    public void FreshPlayerExperienceDoesNotRetainPreviousFeedback()
    {
        PlayerExperienceSnapshot previous =
            CreateExperience(
                tick: 40,
                CreateFeedback(
                    resolvedAtTick: 40));
        PlayerExperienceSnapshot fresh =
            CreateExperience(
                tick: 1,
                PlayerCommandFeedback.None);

        Assert.Equal(
            1,
            ResourcePowerHudModel.ResolveNotificationCount(
                previous));
        Assert.Equal(
            0,
            ResourcePowerHudModel.ResolveNotificationCount(
                fresh));
    }

    [Fact]
    public void AlertCountsDoNotCreateAdditionalSyntheticEvents()
    {
        PlayerExperienceSnapshot experience =
            CreateExperience(
                tick: 10,
                PlayerCommandFeedback.None,
                PlayerAlertState.LowPower |
                PlayerAlertState.ProductionBlocked |
                PlayerAlertState.SupplyCritical);

        Assert.Equal(
            3,
            ResourcePowerHudModel.ResolveNotificationCount(
                experience));
    }

    private static PlayerCommandFeedback CreateFeedback(
        ulong resolvedAtTick) =>
        new(
            PlayerCommandFeedbackKind.Movement,
            PlayerCommandFeedbackState.Accepted,
            AcceptedTargets: 1,
            RejectedTargets: 0,
            BuildCommandRejectionReason.None,
            BuildingPlacementFailureReason.None,
            new SimulationTick(
                resolvedAtTick));

    private static PlayerExperienceSnapshot CreateExperience(
        ulong tick,
        PlayerCommandFeedback feedback,
        PlayerAlertState alerts = PlayerAlertState.None) =>
        new(
            new PlayerId(1),
            new SimulationTick(
                tick),
            PlayerMatchStatus.Active,
            default,
            default,
            default,
            default,
            default,
            alerts,
            CriticalSupplyUnits: 0,
            BlockedProductionFacilities: 0,
            feedback,
            default);
}
