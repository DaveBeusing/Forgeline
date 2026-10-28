using System.Reflection;
using ForgeLine.Game;
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
        Assert.Contains(view.DetailLines, line => line.Label == "PAN LEFT" && line.Value == "H / LEFT");
        Assert.Contains(view.DetailLines, line => line.Label == "EDGE SCROLL" && line.Value == "DISABLED");
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
            ClientBuildIdentity.DisplayVersion,
            view.ProductVersion);
        Assert.Matches("^[0-9a-f]{40}$", ClientBuildIdentity.SourceRevision);
        Assert.EndsWith(
            $"{ClientBuildIdentity.SourceRevision[..12]} [{ClientBuildIdentity.BuildVariant}]",
            view.ProductVersion,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ControlsPresentationListsGameplayBindings()
    {
        FrontendSurfaceView view =
            FrontendPresentationAdapter.Controls(
                new RtsCameraBindings());

        Assert.Equal("CONTROLS", view.Title);
        Assert.Equal((DeveloperToolsCapability.Current.IsCompiled ? 30 : 27) + GameplayBindingRegistry.Definitions.Count, view.DetailLines.Count);
        foreach (string label in new[] { "WORLD DEBUG", "DIAGNOSTIC CHECKBOXES", "DEVELOPER TOOLS" })
            Assert.Equal(DeveloperToolsCapability.Current.IsCompiled, view.DetailLines.Any(line => line.Label == label));
        Assert.Contains(view.DetailLines, line => line.Label == "PLACE TURN LEFT" && line.Value == "Q (WHILE PLACING)");
        Assert.Contains(view.DetailLines, line => line.Label == "PLACE TURN RIGHT" &&
            line.Value == (GameplayBindingRegistry.Default.Key(GameplayAction.RotatePlacement) == ForgeLine.Platform.PlatformKey.Unknown
                ? "E (WHILE PLACING)"
                : "E / " + GameplayBindingRegistry.Default.Prompt(GameplayAction.RotatePlacement) + " (WHILE PLACING)" ));
        Assert.Contains(view.DetailLines, line => line.Label == "RING PAGE PREV" && line.Value == "Q (PAGED SUBMENU)");
        Assert.Contains(view.DetailLines, line => line.Label == "RING PAGE NEXT" && line.Value == "E (PAGED SUBMENU)");
        Assert.Equal(view.DetailLines.Count, view.DetailLines.Select(line => line.Label).Distinct().Count());
        foreach (var definition in GameplayBindingRegistry.Definitions)
            Assert.Contains(view.DetailLines, line => line.Label == definition.Label &&
                line.Value == GameplayBindingRegistry.Default.Prompt(definition.Action));
        Assert.Contains(view.DetailLines, line => line.Label == "DETAILED STATS" && line.Value == "SHIFT + F1");
        Assert.Contains(view.DetailLines, line => line.Label == "OPTIONAL GUIDE" && line.Value == "SHIFT + F12");
        Assert.Contains(view.DetailLines, line => line.Label == "FOCUS CONTROL GROUP" && line.Value == "DOUBLE DIGIT");
        Assert.All(view.DetailLines, line => Assert.DoesNotContain(';', line.Value));
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
