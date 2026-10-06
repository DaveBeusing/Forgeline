using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DebugOverlayStateTests
{
    [Fact]
    public void ControllerDefaultsToDisabledWithAllCategoriesPrepared()
    {
        var controller =
            new DebugOverlayController();

        Assert.False(
            controller.Enabled);
        Assert.Equal(
            DebugOverlayCategory.All,
            controller.Categories);
        Assert.Equal(
            DebugOverlayCategory.None,
            controller.View.EffectiveCategories);
    }

    [Fact]
    public void CategoriesToggleIndependentlyWithoutChangingMasterState()
    {
        var controller =
            new DebugOverlayController();

        controller.ToggleCategory(
            DebugOverlayCategory.Navigation);
        controller.ToggleCategory(
            DebugOverlayCategory.Combat);

        Assert.False(
            controller.Enabled);
        Assert.False(
            controller.View.IsEnabled(
                DebugOverlayCategory.Navigation));

        controller.ToggleMaster();

        Assert.True(
            controller.Enabled);
        Assert.False(
            controller.View.IsEnabled(
                DebugOverlayCategory.Navigation));
        Assert.False(
            controller.View.IsEnabled(
                DebugOverlayCategory.Combat));
        Assert.True(
            controller.View.IsEnabled(
                DebugOverlayCategory.Logistics));
        Assert.True(
            controller.View.IsEnabled(
                DebugOverlayCategory.Rendering));
    }

    [Fact]
    public void RenderingOnlyDoesNotRequestSimulationDebugSnapshot()
    {
        Assert.False(
            DebugOverlayPolicy.RequiresPresentationDebugSnapshot(
                DebugOverlayCategory.Rendering,
                StrategicOverlayMode.None));

        Assert.Equal(
            DebugOverlayCategory.None,
            DebugOverlayPolicy.ResolveRequiredData(
                DebugOverlayCategory.Rendering,
                StrategicOverlayMode.None));
    }

    [Theory]
    [InlineData(
        StrategicOverlayMode.Logistics,
        DebugOverlayCategory.Logistics)]
    [InlineData(
        StrategicOverlayMode.Supply,
        DebugOverlayCategory.Logistics)]
    [InlineData(
        StrategicOverlayMode.Sensors,
        DebugOverlayCategory.Sensors)]
    [InlineData(
        StrategicOverlayMode.Navigation,
        DebugOverlayCategory.Navigation)]
    public void StrategicOverlayRequestsOnlyItsRequiredDebugData(
        StrategicOverlayMode mode,
        DebugOverlayCategory expected)
    {
        Assert.Equal(
            expected,
            DebugOverlayPolicy.ResolveRequiredData(
                DebugOverlayCategory.None,
                mode));
    }

    [Fact]
    public void AllStrategicOverlayDoesNotEnableUnrelatedDevelopmentCategories()
    {
        DebugOverlayCategory required =
            DebugOverlayPolicy.ResolveRequiredData(
                DebugOverlayCategory.None,
                StrategicOverlayMode.All);

        Assert.Equal(
            DebugOverlayCategory.Navigation |
            DebugOverlayCategory.Logistics |
            DebugOverlayCategory.Sensors,
            required);
        Assert.False(
            (required &
             DebugOverlayCategory.Combat) !=
            0);
        Assert.False(
            (required &
             DebugOverlayCategory.Entities) !=
            0);
    }

    [Fact]
    public void CategoryToggleRejectsCombinedMasks()
    {
        var controller =
            new DebugOverlayController();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                controller.ToggleCategory(
                    DebugOverlayCategory.Navigation |
                    DebugOverlayCategory.World));
    }
}
