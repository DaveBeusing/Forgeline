using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishRaidConfigurationTests
{
    [Fact]
    public void DefaultRaidBudgetIsBoundedWithoutChangingMainOffensiveAdmission()
    {
        var configuration = new SkirmishOpponentConfiguration();
        configuration.Validate();
        Assert.Equal(2, configuration.Raids.MinimumUnits);
        Assert.Equal(3, configuration.Raids.MaximumUnits);
        Assert.Equal(32, configuration.Raids.MaximumContacts);
        Assert.Equal(1_200U, configuration.Raids.ActiveTicks);
        Assert.Equal(5, configuration.MinimumAttackUnits);
        Assert.Equal(1, configuration.MinimumObjectivePressureUnits);
    }

    [Theory]
    [InlineData(1, 3, 32, 1200U)]
    [InlineData(2, 4, 32, 1200U)]
    [InlineData(2, 3, 33, 1200U)]
    [InlineData(2, 3, 32, 1201U)]
    public void ExpandedRaidBudgetsAreRejected(int minimum, int maximum, int contacts, uint ticks)
    {
        var configuration = new SkirmishOpponentConfiguration
        {
            Raids = new() { MinimumUnits = minimum, MaximumUnits = maximum, MaximumContacts = contacts, ActiveTicks = ticks }
        };
        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }
}
