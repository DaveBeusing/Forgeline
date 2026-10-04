using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendLifecycleTests
{
    [Fact]
    public void CreditsExposeProductIdentityAndTechnology()
    {
        Assert.Equal("FORGELINE", CreditsModel.ProductName);
        Assert.Equal("Build. Supply. Conquer.", CreditsModel.Tagline);
        Assert.Contains(
            CreditsModel.Sections,
            section =>
                section.Heading == "Technology" &&
                section.Lines.Contains("Direct3D 12"));
    }

    [Fact]
    public void CreditsBackReturnsToMainMenu()
    {
        var shell = new GameFrontendShell();

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        shell.Dispatch(GameFrontendAction.Credits);
        shell.Dispatch(CreditsModel.Back());

        Assert.Equal(GameFrontendScreen.MainMenu, shell.Screen);
    }

    [Fact]
    public void ExitActionProducesControlledLifecycleRequest()
    {
        var shell = new GameFrontendShell();
        var lifecycle = new FrontendLifecycle();

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        shell.Dispatch(GameFrontendAction.Exit);
        lifecycle.Synchronize(shell);

        Assert.Equal(GameFrontendScreen.Exiting, shell.Screen);
        Assert.True(lifecycle.ExitRequest.Requested);
        Assert.Equal(0, lifecycle.ExitRequest.ExitCode);
    }

    [Fact]
    public void LifecycleDoesNotRequestExitForActiveFrontend()
    {
        var shell = new GameFrontendShell();
        var lifecycle = new FrontendLifecycle();

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        lifecycle.Synchronize(shell);

        Assert.False(lifecycle.ExitRequest.Requested);
    }

    [Fact]
    public void FunctionalFrontendDestinationsRemainReachable()
    {
        AssertRoute(GameFrontendAction.NewGame, GameFrontendScreen.NewGame);
        AssertRoute(GameFrontendAction.LoadGame, GameFrontendScreen.LoadGame);
        AssertRoute(GameFrontendAction.Settings, GameFrontendScreen.Settings);
        AssertRoute(GameFrontendAction.Credits, GameFrontendScreen.Credits);
        AssertRoute(GameFrontendAction.Exit, GameFrontendScreen.Exiting);
    }

    [Fact]
    public void BootToNewGameProducesSelectedSessionRequest()
    {
        var shell = new GameFrontendShell();
        var menu = new MainMenuModel(false);
        var newGame = new NewGameModel();

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        Assert.True(menu.TryFocus("new-game"));
        shell.Dispatch(menu.ActivateFocused());
        newGame.SetSeed(731);

        ClientSessionRequest request =
            ClientSessionRequest.NewGame(
                newGame.Configuration.Seed);

        Assert.Equal(GameFrontendScreen.NewGame, shell.Screen);
        Assert.Equal(ClientSessionRequestKind.NewGame, request.Kind);
        Assert.Equal(731UL, request.Seed);
    }

    [Fact]
    public void BootToContinueProducesNewestValidRestoreRequest()
    {
        var loadGame =
            new LoadGameModel(
            [
                new LoadGameEntry(
                    "older",
                    "OLDER",
                    "older.save.json",
                    20,
                    LoadGameEntryState.Available),
                new LoadGameEntry(
                    "newest",
                    "NEWEST",
                    "newest.save.json",
                    80,
                    LoadGameEntryState.Available),
                new LoadGameEntry(
                    "broken",
                    "BROKEN",
                    "broken.save.json",
                    100,
                    LoadGameEntryState.Corrupt)
            ]);
        var shell = new GameFrontendShell();
        var menu =
            new MainMenuModel(
                loadGame.CanContinue);

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        Assert.True(menu.TryFocus("continue"));
        Assert.Equal(
            GameFrontendAction.LoadGame,
            menu.ActivateFocused());
        Assert.True(
            loadGame.TryGetLoadTarget(
                loadGame.ContinueTarget!.Value.Id,
                out LoadGameEntry selected));

        ClientSessionRequest request =
            ClientSessionRequest.Load(selected);

        Assert.Equal(ClientSessionRequestKind.LoadGame, request.Kind);
        Assert.Equal("newest", request.Save?.Id);
    }

    private static void AssertRoute(
        GameFrontendAction action,
        GameFrontendScreen expected)
    {
        var shell = new GameFrontendShell();
        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        shell.Dispatch(action);

        Assert.Equal(expected, shell.Screen);
    }
}
