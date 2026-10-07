namespace ForgeLine.Presentation;

public enum FrontendSurfaceKind : byte
{
    Loading = 1,
    MainMenu = 2,
    Detail = 3,
    PauseMenu = 4,
    StudioSplash = 5
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
    bool CanIncrease = false,
    bool IsHovered = false,
    bool IsPressed = false);

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
    string SecondaryAction = "BACK",
    string Feedback = "",
    float Transition = 1f,
    bool PrimaryHovered = false,
    bool PrimaryPressed = false,
    bool SecondaryHovered = false,
    bool SecondaryPressed = false,
    string ProductVersion = "",
    float SplashElapsedSeconds = 0f,
    float SplashMasterOpacity = 1f)
{
    public static FrontendSurfaceView StudioSplash(float elapsedSeconds, float masterOpacity = 1f) =>
        new(FrontendSurfaceKind.StudioSplash, string.Empty, string.Empty,
            false, 0f, [], [], string.Empty,
            SplashElapsedSeconds: elapsedSeconds,
            SplashMasterOpacity: masterOpacity);

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
        IReadOnlyList<FrontendMenuEntryView> entries,
        string productVersion = "") =>
        new(
            FrontendSurfaceKind.MainMenu,
            string.Empty,
            string.Empty,
            false,
            0f,
            entries,
            [],
            "ENTER  SELECT     UP/DOWN  NAVIGATE",
            ProductVersion: productVersion);

    public static FrontendSurfaceView PauseMenu(
        IReadOnlyList<FrontendMenuEntryView> entries) =>
        new(
            FrontendSurfaceKind.PauseMenu,
            "PAUSED",
            string.Empty,
            false,
            0f,
            entries,
            [],
            "ENTER  SELECT     UP/DOWN  NAVIGATE     ESC  RESUME");

    public FrontendSurfaceView WithInteraction(
        string feedback,
        float transition,
        bool primaryHovered,
        bool primaryPressed,
        bool secondaryHovered,
        bool secondaryPressed) =>
        this with
        {
            Feedback = feedback,
            Transition = transition,
            PrimaryHovered = primaryHovered,
            PrimaryPressed = primaryPressed,
            SecondaryHovered = secondaryHovered,
            SecondaryPressed = secondaryPressed
        };

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
