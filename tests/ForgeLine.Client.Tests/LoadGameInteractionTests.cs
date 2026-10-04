using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class LoadGameInteractionTests
{
    [Fact]
    public void LoadGameFocusMovesAcrossAllVisibleEntries()
    {
        LoadGameModel model = CreateModel();

        Assert.Equal("newest", model.FocusedEntry?.Id);

        model.MoveNext();
        Assert.Equal("broken", model.FocusedEntry?.Id);

        model.MoveNext();
        Assert.Equal("older", model.FocusedEntry?.Id);

        model.MoveNext();
        Assert.Equal("newest", model.FocusedEntry?.Id);
    }

    [Fact]
    public void CorruptFocusedSaveCannotBecomeLoadTarget()
    {
        LoadGameModel model = CreateModel();

        model.MoveNext();

        Assert.False(
            model.TryGetFocusedLoadTarget(out _));
    }

    [Fact]
    public void AvailableFocusedSaveReturnsExactSelectedEntry()
    {
        LoadGameModel model = CreateModel();

        model.MoveNext();
        model.MoveNext();

        Assert.True(
            model.TryGetFocusedLoadTarget(
                out LoadGameEntry selected));
        Assert.Equal("older", selected.Id);
    }

    [Fact]
    public void ContinueRemainsNewestValidSaveRegardlessOfFocus()
    {
        LoadGameModel model = CreateModel();

        model.MoveNext();
        model.MoveNext();

        Assert.Equal("newest", model.ContinueTarget?.Id);
        Assert.Equal("older", model.FocusedEntry?.Id);
    }

    private static LoadGameModel CreateModel() =>
        new(
        [
            new LoadGameEntry(
                "newest",
                "NEWEST",
                "newest.save.json",
                100,
                LoadGameEntryState.Available),
            new LoadGameEntry(
                "broken",
                "BROKEN",
                "broken.save.json",
                90,
                LoadGameEntryState.Corrupt,
                "invalid"),
            new LoadGameEntry(
                "older",
                "OLDER",
                "older.save.json",
                80,
                LoadGameEntryState.Available)
        ]);
}
