using ForgeLine.Game;

namespace ForgeLine.UI;

public enum PauseMenuCommand : byte
{
    Resume = 1,
    SaveGame = 2,
    SaveAndReturnToMenu = 3,
    ReturnToMenu = 4,
    Controls = 5,
    Settings = 6,
    DeveloperTools = 7,
    RetryPractice = 8
}

public readonly record struct PauseMenuItem(
    string Id,
    string Label,
    PauseMenuCommand Command,
    bool IsEnabled = true);

public sealed class PauseMenuModel
{
    private static readonly PauseMenuItem[] s_items =
    [
        new PauseMenuItem(
            "resume",
            "RESUME",
            PauseMenuCommand.Resume),
        new PauseMenuItem(
            "controls",
            "CONTROLS",
            PauseMenuCommand.Controls),
        new PauseMenuItem(
            "settings",
            "SETTINGS",
            PauseMenuCommand.Settings),
        new PauseMenuItem(
            "save-game",
            "SAVE GAME",
            PauseMenuCommand.SaveGame),
        new PauseMenuItem(
            "save-return",
            "SAVE AND RETURN TO MENU",
            PauseMenuCommand.SaveAndReturnToMenu),
        new PauseMenuItem(
            "return-menu",
            "RETURN TO MENU",
            PauseMenuCommand.ReturnToMenu)
    ];

    private PauseMenuItem[] _items = s_items;
    private bool _practice;
    public void SetPracticeAvailable(bool available)
    {
        if(_practice==available)return;
        _practice=available;string focused=_focus.FocusedId;
        _items=_items.Where(i=>i.Command!=PauseMenuCommand.RetryPractice).Select(i=>i.Command==PauseMenuCommand.ReturnToMenu
            ?i with {Label=available?"SKIP PRACTICE / MENU":"RETURN TO MENU"}:i).ToArray();
        if(available)_items=_items.Prepend(new PauseMenuItem("retry-practice","RETRY PRACTICE",PauseMenuCommand.RetryPractice)).ToArray();
        _focus=new(_items.Where(i=>i.IsEnabled).Select(i=>i.Id));_focus.TryFocus(focused);
    }
    private FrontendFocusModel _focus = new(s_items.Select(item => item.Id));
    public PauseMenuModel(bool developerToolsAvailable = false) => SetDeveloperToolsAvailable(developerToolsAvailable);
    public void SetDeveloperToolsAvailable(bool available)
    {
        available &= DeveloperToolsCapability.Current.IsCompiled;
        bool present = _items.Any(item => item.Command == PauseMenuCommand.DeveloperTools);
        if (present == available) return;
        string focused = _focus.FocusedId;
        _items = available ? s_items.Take(3).Append(new PauseMenuItem("developer-tools", "DEVELOPER TOOLS", PauseMenuCommand.DeveloperTools)).Concat(s_items.Skip(3)).ToArray() : s_items;
        _focus = new(_items.Where(item => item.IsEnabled).Select(item => item.Id));
        _focus.TryFocus(focused);
        bool practice=_practice;_practice=false;SetPracticeAvailable(practice);
    }

    public IReadOnlyList<PauseMenuItem> Items =>
        _items;

    public string FocusedId =>
        _focus.FocusedId;

    public PauseMenuItem FocusedItem =>
        _items.First(
            item =>
                string.Equals(
                    item.Id,
                    FocusedId,
                    StringComparison.Ordinal));

    public PauseMenuItem MoveNext()
    {
        _focus.MoveNext();
        return FocusedItem;
    }

    public PauseMenuItem MovePrevious()
    {
        _focus.MovePrevious();
        return FocusedItem;
    }

    public bool TryFocus(string id) =>
        _items.Any(
            item =>
                item.IsEnabled &&
                string.Equals(
                    item.Id,
                    id,
                    StringComparison.Ordinal)) &&
        _focus.TryFocus(id);

    public PauseMenuCommand ActivateFocused() =>
        FocusedItem.Command;
}
