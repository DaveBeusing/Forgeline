namespace ForgeLine.UI;

public enum MainMenuCommand : byte
{
    Continue = 1,
    NewGame = 2,
    LoadGame = 3,
    Settings = 4,
    Credits = 5,
    Exit = 6
}

public readonly record struct MainMenuItem(
    string Id,
    string Label,
    MainMenuCommand Command,
    bool IsEnabled);

public sealed class MainMenuModel
{
    private const string ContinueId = "continue";
    private const string NewGameId = "new-game";
    private const string LoadGameId = "load-game";
    private const string SettingsId = "settings";
    private const string CreditsId = "credits";
    private const string ExitId = "exit";

    private readonly MainMenuItem[] _items;
    private readonly FrontendFocusModel _focus;

    public MainMenuModel(bool hasValidContinueTarget)
    {
        _items =
        [
            new MainMenuItem(ContinueId, "CONTINUE", MainMenuCommand.Continue, hasValidContinueTarget),
            new MainMenuItem(NewGameId, "NEW GAME", MainMenuCommand.NewGame, true),
            new MainMenuItem(LoadGameId, "LOAD GAME", MainMenuCommand.LoadGame, true),
            new MainMenuItem(SettingsId, "SETTINGS", MainMenuCommand.Settings, true),
            new MainMenuItem(CreditsId, "CREDITS", MainMenuCommand.Credits, true),
            new MainMenuItem(ExitId, "EXIT", MainMenuCommand.Exit, true)
        ];

        _focus = new FrontendFocusModel(
            _items.Where(static item => item.IsEnabled).Select(static item => item.Id));
    }

    public static string ProductName => FrontendLoadingController.ProductName;

    public static string Tagline => FrontendLoadingController.Tagline;

    public IReadOnlyList<MainMenuItem> Items => _items;

    public string FocusedId => _focus.FocusedId;

    public MainMenuItem FocusedItem =>
        _items.First(item => string.Equals(item.Id, FocusedId, StringComparison.Ordinal));

    public MainMenuItem MoveNext()
    {
        _focus.MoveNext();
        return FocusedItem;
    }

    public MainMenuItem MovePrevious()
    {
        _focus.MovePrevious();
        return FocusedItem;
    }

    public bool TryFocus(string id) =>
        _items.Any(item =>
            item.IsEnabled &&
            string.Equals(item.Id, id, StringComparison.Ordinal)) &&
        _focus.TryFocus(id);

    public GameFrontendAction ActivateFocused()
    {
        return FocusedItem.Command switch
        {
            MainMenuCommand.Continue => GameFrontendAction.LoadGame,
            MainMenuCommand.NewGame => GameFrontendAction.NewGame,
            MainMenuCommand.LoadGame => GameFrontendAction.LoadGame,
            MainMenuCommand.Settings => GameFrontendAction.Settings,
            MainMenuCommand.Credits => GameFrontendAction.Credits,
            MainMenuCommand.Exit => GameFrontendAction.Exit,
            _ => throw new InvalidOperationException(
                $"Unsupported main-menu command {FocusedItem.Command}.")
        };
    }
}
