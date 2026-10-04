using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class NewGameModelTests
{
    [Fact]
    public void DefaultsExposeCurrentPlayableVerticalSlice()
    {
        var model = new NewGameModel();

        Assert.Equal("Central Divide", model.Configuration.MapName);
        Assert.Equal("Directorate", model.Configuration.FactionName);
        Assert.Equal(17UL, model.Configuration.Seed);
        Assert.True(model.CanStart);
    }

    [Fact]
    public void SeedCanBeChangedWithoutCreatingParallelMatchRules()
    {
        var model = new NewGameModel();

        model.SetSeed(2026);

        Assert.Equal(2026UL, model.Configuration.Seed);
        Assert.Equal("Central Divide", model.Configuration.MapName);
        Assert.Equal("Directorate", model.Configuration.FactionName);
    }

    [Fact]
    public void StartAndBackUseExistingFrontendActions()
    {
        var model = new NewGameModel();

        Assert.Equal(GameFrontendAction.StartMatch, model.Start());
        Assert.Equal(GameFrontendAction.Back, model.Back());
    }

    [Fact]
    public void StartActionTransitionsExistingShellIntoGameplay()
    {
        var shell = new GameFrontendShell();
        var model = new NewGameModel();

        shell.Dispatch(GameFrontendAction.LoadingCompleted);
        shell.Dispatch(GameFrontendAction.NewGame);
        shell.Dispatch(model.Start());

        Assert.Equal(GameFrontendScreen.InGame, shell.Screen);
        Assert.True(shell.IsGameplayActive);
    }
}
