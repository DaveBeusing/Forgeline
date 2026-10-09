using System.Reflection;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class MainMenuModelTests
{
    [Fact]
    public void ControlsReflectCustomCameraKeysAndDisabledEdgeScroll()
    {
        var bindings = new RtsCameraBindings { PanLeft = ForgeLine.Platform.PlatformKey.H };
        var view = FrontendPresentationAdapter.Controls(bindings, edgeScrollEnabled: false);
        Assert.Equal(FrontendSurfaceKind.Controls, view.Kind);
        Assert.Contains("W/H/S/D", view.DetailLines[0].Value);
        Assert.Contains("EDGE PAN DISABLED", view.DetailLines[1].Value);
    }

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
            12,
            view.DetailLines.Count);
        Assert.Equal("OPTIONAL GUIDE", view.DetailLines[^2].Label);
        Assert.Contains("SHIFT+F12 SHOW / HIDE", view.DetailLines[^2].Value);
        Assert.Contains("ONBOARDING SETTING", view.DetailLines[^2].Value);
        Assert.Contains("HOME BASE FOCUS", view.DetailLines[1].Value);
        Assert.Contains("DOUBLE LEFT SAME-TYPE VISIBLE UNITS", view.DetailLines[2].Value);
        Assert.Equal("CONTROL GROUPS", view.DetailLines[8].Label);
        Assert.Contains("DOUBLE DIGIT FOCUS", view.DetailLines[8].Value);
        Assert.Contains("SHIFT+CLICK REPEAT AFTER ACCEPTANCE", view.DetailLines[6].Value);
        Assert.Contains(
            "W/A/S/D",
            view.DetailLines[0].Value);
        Assert.Contains(
            "UP/LEFT/DOWN/RIGHT",
            view.DetailLines[0].Value);
        Assert.Equal(
            "F1 / F2 / F3",
            view.DetailLines[4].Label);
        Assert.Contains(
            "HELP",
            view.DetailLines[4].Value);
        Assert.Contains(
            "WORLD DEBUG",
            view.DetailLines[4].Value);
        Assert.Contains(
            "FORMATION",
            view.DetailLines[4].Value);
        Assert.Contains(
            "COMMAND CORE",
            view.DetailLines[5].Value);
        Assert.Contains(
            "POWER PLANT",
            view.DetailLines[5].Value);
        Assert.Contains(
            "EXTRACTOR",
            view.DetailLines[5].Value);
        Assert.Contains(
            "STORAGE DEPOT",
            view.DetailLines[6].Value);
        Assert.Contains(
            "SMELTER",
            view.DetailLines[6].Value);
        Assert.Contains(
            "ROTATE BUILDING",
            view.DetailLines[6].Value);
        Assert.Contains(
            "STRATEGIC OVERLAY",
            view.DetailLines[^1].Value);
        Assert.Contains(
            "MINIMAP",
            view.DetailLines[^1].Value);
        Assert.Contains(
            "HELP",
            view.DetailLines[^1].Value);
        Assert.Contains(
            "ESC/SPACE PAUSE",
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
