using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class MainMenuModelTests
{
    [Fact]
    public void MenuPublishesForgelineIdentityAndExpectedActions()
    {
        var menu = new MainMenuModel(hasValidContinueTarget: true);

        Assert.Equal("FORGELINE", MainMenuModel.ProductName);
        Assert.Equal("Build. Supply. Conquer.", MainMenuModel.Tagline);
        Assert.Collection(
            menu.Items,
            item => Assert.Equal(MainMenuCommand.Continue, item.Command),
            item => Assert.Equal(MainMenuCommand.NewGame, item.Command),
            item => Assert.Equal(MainMenuCommand.LoadGame, item.Command),
            item => Assert.Equal(MainMenuCommand.Settings, item.Command),
            item => Assert.Equal(MainMenuCommand.Credits, item.Command),
            item => Assert.Equal(MainMenuCommand.Exit, item.Command));
    }

    [Fact]
    public void ContinueIsDisabledAndSkippedWithoutValidTarget()
    {
        var menu = new MainMenuModel(hasValidContinueTarget: false);

        Assert.False(menu.Items[0].IsEnabled);
        Assert.Equal("new-game", menu.FocusedId);
        Assert.False(menu.TryFocus("continue"));
    }

    [Fact]
    public void KeyboardNavigationWrapsAcrossEnabledActions()
    {
        var menu = new MainMenuModel(hasValidContinueTarget: false);

        Assert.Equal("load-game", menu.MoveNext().Id);
        Assert.Equal("new-game", menu.MovePrevious().Id);
        Assert.Equal("exit", menu.MovePrevious().Id);
    }

    [Theory]
    [InlineData("new-game", GameFrontendAction.NewGame)]
    [InlineData("load-game", GameFrontendAction.LoadGame)]
    [InlineData("settings", GameFrontendAction.Settings)]
    [InlineData("credits", GameFrontendAction.Credits)]
    [InlineData("exit", GameFrontendAction.Exit)]
    public void EnabledActionsMapToExistingFrontendShell(
        string itemId,
        GameFrontendAction expected)
    {
        var menu = new MainMenuModel(hasValidContinueTarget: false);

        Assert.True(menu.TryFocus(itemId));
        Assert.Equal(expected, menu.ActivateFocused());
    }

    [Fact]
    public void ContinueUsesExistingLoadBoundary()
    {
        var menu = new MainMenuModel(hasValidContinueTarget: true);

        Assert.Equal("continue", menu.FocusedId);
        Assert.Equal(GameFrontendAction.LoadGame, menu.ActivateFocused());
    }
}
