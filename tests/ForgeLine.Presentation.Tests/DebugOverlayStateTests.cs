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
    [InlineData(StrategicOverlayMode.Logistics)]
    [InlineData(StrategicOverlayMode.Supply)]
    [InlineData(StrategicOverlayMode.Sensors)]
    [InlineData(StrategicOverlayMode.Navigation)]
    [InlineData(StrategicOverlayMode.Power)]
    [InlineData(StrategicOverlayMode.All)]
    public void StrategicOverlaysUsePlayerSnapshotsWithoutDeveloperDebugCapture(StrategicOverlayMode mode)
    {
        Assert.Equal(DebugOverlayCategory.None,
            DebugOverlayPolicy.ResolveRequiredData(DebugOverlayCategory.None, mode));
        Assert.False(DebugOverlayPolicy.RequiresPresentationDebugSnapshot(DebugOverlayCategory.None, mode));
    }

    [Fact]
    public void AllStrategicOverlayPreservesOnlyExplicitDeveloperDebugCategories()
    {
        const DebugOverlayCategory requested = DebugOverlayCategory.Navigation | DebugOverlayCategory.Logistics;
        DebugOverlayCategory required = DebugOverlayPolicy.ResolveRequiredData(requested, StrategicOverlayMode.All);
        Assert.Equal(requested, required);
        Assert.False((required & DebugOverlayCategory.Combat) != 0);
        Assert.False((required & DebugOverlayCategory.Entities) != 0);
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
