using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VerticalSliceValidationProfileTests
{
    [Fact]
    public void ValidationProfileKeepsOffensiveFuelAdmissionBelowSustainedResupplyBand()
    {
        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);

        Assert.Equal(0.40, settings.OpponentConfigurations[1].OffensiveFuelThreshold, 2);
        Assert.Equal(1, settings.OpponentConfigurations[1].MinimumObjectivePressureUnits);
        Assert.Equal(0.46, settings.OpponentConfigurations[2].OffensiveFuelThreshold, 2);

        Assert.True(
            settings.OpponentConfigurations[1].OffensiveFuelThreshold >
            settings.OpponentConfigurations[1].ResupplyThreshold);
        Assert.True(
            settings.OpponentConfigurations[2].OffensiveFuelThreshold >
            settings.OpponentConfigurations[2].ResupplyThreshold);

        Assert.True(
            settings.OpponentConfigurations[1].OffensiveFuelThreshold <
            0.55);
        Assert.True(
            settings.OpponentConfigurations[2].OffensiveFuelThreshold <
            0.55);
    }
}
