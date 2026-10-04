namespace ForgeLine.Presentation;

public enum FrontendSurfaceKind : byte
{
    Loading = 1,
    MainMenu = 2
}

public readonly record struct FrontendMenuEntryView(
    string Id,
    string Label,
    bool IsEnabled,
    bool IsFocused);

public readonly record struct FrontendSurfaceView(
    FrontendSurfaceKind Kind,
    string Status,
    bool HasProgress,
    float Progress,
    IReadOnlyList<FrontendMenuEntryView> MenuEntries)
{
    public static FrontendSurfaceView Loading(
        string status,
        bool hasProgress,
        float progress) =>
        new(
            FrontendSurfaceKind.Loading,
            status,
            hasProgress,
            progress,
            []);

    public static FrontendSurfaceView MainMenu(
        IReadOnlyList<FrontendMenuEntryView> entries) =>
        new(
            FrontendSurfaceKind.MainMenu,
            string.Empty,
            false,
            0f,
            entries);
}
