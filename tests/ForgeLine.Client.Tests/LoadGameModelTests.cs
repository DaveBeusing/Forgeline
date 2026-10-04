using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class LoadGameModelTests
{
    [Fact]
    public void EmptyCatalogHasNoContinueTarget()
    {
        var model = new LoadGameModel([]);

        Assert.False(model.HasSaves);
        Assert.False(model.CanContinue);
        Assert.Null(model.ContinueTarget);
    }

    [Fact]
    public void ContinueSelectsNewestAvailableSave()
    {
        var model = new LoadGameModel(
        [
            new LoadGameEntry(
                "old",
                "Old",
                "old.save.json",
                100,
                LoadGameEntryState.Available),
            new LoadGameEntry(
                "broken",
                "Broken",
                "broken.save.json",
                900,
                LoadGameEntryState.Corrupt,
                "checksum"),
            new LoadGameEntry(
                "new",
                "New",
                "new.save.json",
                400,
                LoadGameEntryState.Available)
        ]);

        Assert.True(model.CanContinue);
        Assert.Equal("new", model.ContinueTarget?.Id);
    }

    [Theory]
    [InlineData(LoadGameEntryState.Corrupt)]
    [InlineData(LoadGameEntryState.Incompatible)]
    public void InvalidEntriesRemainVisibleButCannotLoad(
        LoadGameEntryState state)
    {
        var model = new LoadGameModel(
        [
            new LoadGameEntry(
                "invalid",
                "Invalid",
                "invalid.save.json",
                0,
                state,
                "diagnostic")
        ]);

        Assert.True(model.HasSaves);
        Assert.False(model.CanContinue);
        Assert.False(
            model.TryGetLoadTarget(
                "invalid",
                out _));
        Assert.Equal(
            "diagnostic",
            model.Entries[0].Detail);
    }

    [Fact]
    public void AvailableEntryCanBeSelectedForRestore()
    {
        var expected =
            new LoadGameEntry(
                "slot",
                "Campaign",
                "slot.save.json",
                240,
                LoadGameEntryState.Available);
        var model =
            new LoadGameModel([expected]);

        Assert.True(
            model.TryGetLoadTarget(
                "slot",
                out LoadGameEntry actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BackUsesExistingFrontendAction()
    {
        Assert.Equal(
            GameFrontendAction.Back,
            LoadGameModel.Back());
    }
}
