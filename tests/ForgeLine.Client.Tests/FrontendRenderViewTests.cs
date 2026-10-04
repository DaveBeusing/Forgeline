using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendRenderViewTests
{
    [Fact]
    public void LoadingViewCarriesTypedLoadingState()
    {
        var state = new FrontendLoadingState(
            FrontendLoadingPhase.LoadingAssets,
            "Loading assets",
            2,
            4);

        FrontendRenderView view =
            FrontendRenderView.LoadingView(state);

        Assert.Equal(GameFrontendScreen.Loading, view.Screen);
        Assert.Equal(state, view.Loading);
        Assert.Null(view.MainMenu);
    }

    [Fact]
    public void MainMenuViewCarriesExistingMenuModel()
    {
        var menu =
            new MainMenuModel(
                hasValidContinueTarget: false);

        FrontendRenderView view =
            FrontendRenderView.MainMenuView(menu);

        Assert.Equal(GameFrontendScreen.MainMenu, view.Screen);
        Assert.Same(menu, view.MainMenu);
        Assert.NotNull(view.MainMenu);
        Assert.Equal("new-game", view.MainMenu!.FocusedId);
    }

    [Fact]
    public void MainMenuKeyboardFocusUsesExistingFocusModel()
    {
        var menu =
            new MainMenuModel(
                hasValidContinueTarget: false);

        menu.MoveNext();
        menu.MoveNext();

        Assert.Equal("settings", menu.FocusedId);
        Assert.Equal(
            GameFrontendAction.Settings,
            menu.ActivateFocused());
    }
}
