namespace ForgeLine.UI;

public enum GameFrontendScreen : byte
{
    Loading = 1,
    MainMenu = 2,
    NewGame = 3,
    LoadGame = 4,
    Settings = 5,
    Credits = 6,
    InGame = 7,
    Exiting = 8
}

public enum GameFrontendAction : byte
{
    LoadingCompleted = 1,
    NewGame = 2,
    LoadGame = 3,
    Settings = 4,
    Credits = 5,
    Back = 6,
    StartMatch = 7,
    Exit = 8
}

public sealed class GameFrontendShell
{
    public GameFrontendScreen Screen { get; private set; } =
        GameFrontendScreen.Loading;

    public bool IsGameplayActive =>
        Screen == GameFrontendScreen.InGame;

    public bool IsExitRequested =>
        Screen == GameFrontendScreen.Exiting;

    public void Dispatch(GameFrontendAction action)
    {
        Screen = (Screen, action) switch
        {
            (GameFrontendScreen.Loading, GameFrontendAction.LoadingCompleted) =>
                GameFrontendScreen.MainMenu,
            (GameFrontendScreen.MainMenu, GameFrontendAction.NewGame) =>
                GameFrontendScreen.NewGame,
            (GameFrontendScreen.MainMenu, GameFrontendAction.LoadGame) =>
                GameFrontendScreen.LoadGame,
            (GameFrontendScreen.MainMenu, GameFrontendAction.Settings) =>
                GameFrontendScreen.Settings,
            (GameFrontendScreen.MainMenu, GameFrontendAction.Credits) =>
                GameFrontendScreen.Credits,
            (GameFrontendScreen.MainMenu, GameFrontendAction.Exit) =>
                GameFrontendScreen.Exiting,
            (GameFrontendScreen.NewGame, GameFrontendAction.StartMatch) =>
                GameFrontendScreen.InGame,
            (GameFrontendScreen.NewGame, GameFrontendAction.Back) =>
                GameFrontendScreen.MainMenu,
            (GameFrontendScreen.LoadGame, GameFrontendAction.Back) =>
                GameFrontendScreen.MainMenu,
            (GameFrontendScreen.Settings, GameFrontendAction.Back) =>
                GameFrontendScreen.MainMenu,
            (GameFrontendScreen.Credits, GameFrontendAction.Back) =>
                GameFrontendScreen.MainMenu,
            (GameFrontendScreen.InGame, GameFrontendAction.Back) =>
                GameFrontendScreen.MainMenu,
            _ => throw new InvalidOperationException(
                $"Frontend action {action} is not valid from {Screen}.")
        };
    }
}
