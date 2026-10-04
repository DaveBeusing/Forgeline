using ForgeLine.UI;

namespace ForgeLine.Client.Tests;

public sealed class GameFrontendShellTests
{
    [Fact]
    public void StartupFlowsFromLoadingToNewGameAndGameplay()
    {
        var shell = new GameFrontendShell();

        Assert.Equal(GameFrontendScreen.Loading, shell.Screen);

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        Assert.Equal(GameFrontendScreen.MainMenu, shell.Screen);

        shell.Dispatch(GameFrontendAction.NewGame);
        Assert.Equal(GameFrontendScreen.NewGame, shell.Screen);

        shell.Dispatch(GameFrontendAction.StartMatch);
        Assert.True(shell.IsGameplayActive);
    }

    [Theory]
    [InlineData(GameFrontendAction.LoadGame, GameFrontendScreen.LoadGame)]
    [InlineData(GameFrontendAction.Settings, GameFrontendScreen.Settings)]
    [InlineData(GameFrontendAction.Credits, GameFrontendScreen.Credits)]
    public void MainMenuRoutesToSecondaryScreens(
        GameFrontendAction action,
        GameFrontendScreen expected)
    {
        var shell = new GameFrontendShell();
        shell.Dispatch(GameFrontendAction.LoadingCompleted);

        shell.Dispatch(action);

        Assert.Equal(expected, shell.Screen);
        shell.Dispatch(GameFrontendAction.Back);
        Assert.Equal(GameFrontendScreen.MainMenu, shell.Screen);
    }

    [Fact]
    public void MainMenuExitRequestsShutdown()
    {
        var shell = new GameFrontendShell();
        shell.Dispatch(GameFrontendAction.LoadingCompleted);

        shell.Dispatch(GameFrontendAction.Exit);

        Assert.True(shell.IsExitRequested);
    }

    [Fact]
    public void InvalidTransitionIsRejected()
    {
        var shell = new GameFrontendShell();

        Assert.Throws<InvalidOperationException>(
            () => shell.Dispatch(GameFrontendAction.StartMatch));
    }
}
