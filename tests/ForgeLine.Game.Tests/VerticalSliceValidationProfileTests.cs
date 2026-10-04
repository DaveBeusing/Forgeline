using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VerticalSliceValidationProfileTests
{
    [Fact]
    public void ValidationProfileKeepsOffensiveFuelAdmissionBelowSustainedResupplyBand()
    {
        VerticalSliceScenarioSettings settings =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);

        Assert.Equal(0.40, settings.WestOpponent.OffensiveFuelThreshold, 2);
        Assert.Equal(1, settings.WestOpponent.MinimumObjectivePressureUnits);
        Assert.Equal(0.46, settings.EastOpponent.OffensiveFuelThreshold, 2);

        Assert.True(
            settings.WestOpponent.OffensiveFuelThreshold >
            settings.WestOpponent.ResupplyThreshold);
        Assert.True(
            settings.EastOpponent.OffensiveFuelThreshold >
            settings.EastOpponent.ResupplyThreshold);

        Assert.True(
            settings.WestOpponent.OffensiveFuelThreshold <
            0.55);
        Assert.True(
            settings.EastOpponent.OffensiveFuelThreshold <
            0.55);
    }
}
