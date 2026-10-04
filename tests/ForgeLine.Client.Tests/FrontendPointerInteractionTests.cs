using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendPointerInteractionTests
{
    [Fact]
    public void DetailRowsResolveFromRenderedGeometry()
    {
        Assert.Equal(
            0,
            FrontendHitTesting.DetailRow(
                120,
                400,
                FrontendDesign.ResolveLayout(1920, 1080),
                4));
        Assert.Equal(
            2,
            FrontendHitTesting.DetailRow(
                120,
                510,
                FrontendDesign.ResolveLayout(1920, 1080),
                4));
        Assert.Null(
            FrontendHitTesting.DetailRow(
                120,
                700,
                FrontendDesign.ResolveLayout(1920, 1080),
                4));
    }

    [Fact]
    public void DetailRowsHonorUiScale()
    {
        Assert.Equal(
            1,
            FrontendHitTesting.DetailRow(
                180,
                672,
                FrontendDesign.ResolveLayout(2880, 1620),
                3));
    }

    [Fact]
    public void FooterUsesDedicatedPointerRegion()
    {
        Assert.True(
            FrontendHitTesting.Footer(
                200,
                930,
                1.0f));
        Assert.False(
            FrontendHitTesting.Footer(
                200,
                700,
                1.0f));
    }

    [Fact]
    public void SaveCanBeFocusedDirectlyFromPointerRow()
    {
        var model =
            new LoadGameModel(
            [
                new LoadGameEntry(
                    "one",
                    "ONE",
                    "one.save.json",
                    10,
                    LoadGameEntryState.Available),
                new LoadGameEntry(
                    "two",
                    "TWO",
                    "two.save.json",
                    20,
                    LoadGameEntryState.Available)
            ]);

        model.Focus(1);

        Assert.Equal(
            "two",
            model.FocusedEntry?.Id);
    }

    [Fact]
    public void SettingsCanBeFocusedDirectlyFromPointerRow()
    {
        var interaction =
            new SettingsInteractionModel();

        interaction.Focus(
            FrontendSettingsField.CameraSpeed);

        Assert.Equal(
            FrontendSettingsField.CameraSpeed,
            interaction.FocusedField);
    }
}
