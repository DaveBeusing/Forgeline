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
