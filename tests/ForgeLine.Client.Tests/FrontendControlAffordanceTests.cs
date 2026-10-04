using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendControlAffordanceTests
{
    [Fact]
    public void NewGameExposesSeedAdjustAndStartAction()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.NewGame(
                new NewGameModel());

        FrontendDetailLineView seed =
            Assert.Single(
                view.DetailLines,
                line => line.Label == "SEED");

        Assert.True(seed.IsFocused);
        Assert.True(seed.CanDecrease);
        Assert.True(seed.CanIncrease);
        Assert.Equal("START", view.PrimaryAction);
        Assert.Equal("BACK", view.SecondaryAction);
    }

    [Fact]
    public void LoadActionOnlyAppearsForLoadableFocus()
    {
        var model =
            new LoadGameModel(
            [
                new LoadGameEntry(
                    "broken",
                    "BROKEN",
                    "broken.save.json",
                    1,
                    LoadGameEntryState.Corrupt),
                new LoadGameEntry(
                    "valid",
                    "VALID",
                    "valid.save.json",
                    2,
                    LoadGameEntryState.Available)
            ]);

        Assert.Equal(
            string.Empty,
            FrontendPresentationAdapter.LoadGame(model).PrimaryAction);

        model.Focus(1);

        Assert.Equal(
            "LOAD",
            FrontendPresentationAdapter.LoadGame(model).PrimaryAction);
    }

    [Fact]
    public void AdjustButtonsMatchRenderedControlRegions()
    {
        Assert.Equal(
            -1,
            FrontendHitTesting.DetailAdjust(
                850,
                405,
                1.0f,
                4));
        Assert.Equal(
            1,
            FrontendHitTesting.DetailAdjust(
                940,
                405,
                1.0f,
                4));
    }

    [Fact]
    public void ActionButtonsMatchRenderedRegions()
    {
        Assert.True(
            FrontendHitTesting.PrimaryAction(
                900,
                860,
                1.0f));
        Assert.True(
            FrontendHitTesting.SecondaryAction(
                150,
                860,
                1.0f));
    }
}
