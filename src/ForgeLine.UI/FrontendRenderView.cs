namespace ForgeLine.UI;

public readonly record struct FrontendRenderView(
    GameFrontendScreen Screen,
    FrontendLoadingState Loading,
    MainMenuModel? MainMenu)
{
    public static FrontendRenderView LoadingView(
        FrontendLoadingState loading) =>
        new(
            GameFrontendScreen.Loading,
            loading,
            null);

    public static FrontendRenderView MainMenuView(
        MainMenuModel menu) =>
        new(
            GameFrontendScreen.MainMenu,
            default,
            menu);
}
