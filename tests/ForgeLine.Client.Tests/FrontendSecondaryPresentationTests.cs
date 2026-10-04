using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendSecondaryPresentationTests
{
    [Fact]
    public void NewGameShowsPlayableVerticalSliceConfiguration()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.NewGame(
                new NewGameModel());

        Assert.Equal(FrontendSurfaceKind.Detail, view.Kind);
        Assert.Equal("NEW GAME", view.Title);
        Assert.Contains(
            view.DetailLines,
            line =>
                line.Label == "BATTLEFIELD" &&
                line.Value == NewGameModel.PrototypeMapName);
    }

    [Fact]
    public void EmptyLoadGameShowsExplicitEmptyState()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.LoadGame(
                new LoadGameModel([]));

        Assert.Equal("LOAD GAME", view.Title);
        Assert.Contains(
            view.DetailLines,
            line =>
                line.Value == "NO SAVED MATCHES");
    }

    [Fact]
    public void InvalidSaveIsPresentedAsWarning()
    {
        var model =
            new LoadGameModel(
            [
                new LoadGameEntry(
                    "broken",
                    "BROKEN SLOT",
                    "broken.save.json",
                    0,
                    LoadGameEntryState.Corrupt,
                    "invalid")
            ]);

        FrontendSurfaceView view =
            FrontendPresentationAdapter.LoadGame(model);

        Assert.Contains(
            view.DetailLines,
            line =>
                line.Label == "BROKEN SLOT" &&
                line.IsWarning);
    }

    [Fact]
    public void SettingsReflectCurrentClientValues()
    {
        var model =
            new SettingsModel(
                new FrontendSettingsSnapshot(
                    1920,
                    1080,
                    false,
                    1.25f,
                    true,
                    true,
                    1.0f,
                    new ForgeLine.Input.RtsCameraBindings()));

        FrontendSurfaceView view =
            FrontendPresentationAdapter.Settings(model);

        Assert.Equal("SETTINGS", view.Title);
        Assert.Contains(
            view.DetailLines,
            line =>
                line.Label == "UI SCALE" &&
                line.Value == "1.25");
    }

    [Fact]
    public void CreditsUseExistingCreditsModel()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.Credits();

        Assert.Equal("CREDITS", view.Title);
        Assert.Contains(
            view.DetailLines,
            line =>
                line.Value == "ForgeLine Engine");
    }
}
