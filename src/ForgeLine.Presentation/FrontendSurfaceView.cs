namespace ForgeLine.Presentation;

public enum FrontendSurfaceKind : byte
{
    Loading = 1,
    MainMenu = 2,
    Detail = 3
}

public readonly record struct FrontendMenuEntryView(
    string Id,
    string Label,
    bool IsEnabled,
    bool IsFocused);

public readonly record struct FrontendDetailLineView(
    string Label,
    string Value,
    bool IsWarning = false,
    bool IsFocused = false,
    bool CanDecrease = false,
    bool CanIncrease = false);

public readonly record struct FrontendSurfaceView(
    FrontendSurfaceKind Kind,
    string Title,
    string Status,
    bool HasProgress,
    float Progress,
    IReadOnlyList<FrontendMenuEntryView> MenuEntries,
    IReadOnlyList<FrontendDetailLineView> DetailLines,
    string Footer,
    string PrimaryAction = "",
    string SecondaryAction = "BACK")
{
    public static FrontendSurfaceView Loading(
        string status,
        bool hasProgress,
        float progress) =>
        new(
            FrontendSurfaceKind.Loading,
            string.Empty,
            status,
            hasProgress,
            progress,
            [],
            [],
            string.Empty);

    public static FrontendSurfaceView MainMenu(
        IReadOnlyList<FrontendMenuEntryView> entries) =>
        new(
            FrontendSurfaceKind.MainMenu,
            string.Empty,
            string.Empty,
            false,
            0f,
            entries,
            [],
            "ENTER  SELECT     UP/DOWN  NAVIGATE");

    public static FrontendSurfaceView Detail(
        string title,
        IReadOnlyList<FrontendDetailLineView> lines,
        string footer,
        string primaryAction = "",
        string secondaryAction = "BACK") =>
        new(
            FrontendSurfaceKind.Detail,
            title,
            string.Empty,
            false,
            0f,
            [],
            lines,
            footer,
            primaryAction,
            secondaryAction);
}
