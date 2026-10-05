using System.Reflection;
using ForgeLine.Input;
using ForgeLine.Presentation;
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
            item => Assert.Equal(MainMenuCommand.Controls, item.Command),
            item => Assert.Equal(MainMenuCommand.Settings, item.Command),
            item => Assert.Equal(MainMenuCommand.Credits, item.Command),
            item => Assert.Equal(MainMenuCommand.Exit, item.Command));
    }

    [Fact]
    public void MainMenuPresentationUsesBuiltProductVersion()
    {
        var menu =
            new MainMenuModel(
                hasValidContinueTarget: false);

        var view =
            FrontendPresentationAdapter.MainMenu(
                menu);
        string? informationalVersion =
            typeof(FrontendPresentationAdapter)
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        Assert.False(
            string.IsNullOrWhiteSpace(
                informationalVersion));
        Assert.Equal(
            informationalVersion!.Split('+')[0],
            view.ProductVersion);
    }

    [Fact]
    public void ControlsPresentationListsGameplayBindings()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.Controls(
                new RtsCameraBindings());

        Assert.Equal(
            "CONTROLS",
            view.Title);
        Assert.Equal(
            8,
            view.DetailLines.Count);
        Assert.Equal(
            "W/A/S/D",
            view.DetailLines[0].Value);
        Assert.Contains(
            "F12 HELP",
            view.DetailLines[^1].Value);
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
    [InlineData("controls", GameFrontendAction.Controls)]
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
