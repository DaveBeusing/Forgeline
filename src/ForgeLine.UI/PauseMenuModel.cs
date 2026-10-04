namespace ForgeLine.UI;

public enum PauseMenuCommand : byte
{
    Resume = 1,
    SaveGame = 2,
    SaveAndReturnToMenu = 3,
    ReturnToMenu = 4
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

    private readonly FrontendFocusModel _focus =
        new(
            s_items
                .Where(static item => item.IsEnabled)
                .Select(static item => item.Id));

    public IReadOnlyList<PauseMenuItem> Items =>
        s_items;

    public string FocusedId =>
        _focus.FocusedId;

    public PauseMenuItem FocusedItem =>
        s_items.First(
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
        s_items.Any(
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
