using ForgeLine.Input;
using ForgeLine.Platform;

namespace ForgeLine.UI;

public readonly record struct FrontendSettingsSnapshot(
    int WindowWidth,
    int WindowHeight,
    bool BorderlessFullscreen,
    float UiScale,
    bool ShowOnboarding,
    bool EdgeScrollEnabled,
    float CameraPanSpeedMultiplier,
    RtsCameraBindings CameraBindings, GameplayBindings? GameplayBindings = null);

public sealed class SettingsModel
{
    public SettingsModel(
        FrontendSettingsSnapshot settings)
    {
        Settings = settings;
        Bindings = new(settings.GameplayBindings ?? new(), settings.CameraBindings);
    }

    public FrontendSettingsSnapshot Settings { get; private set; }
    public GameplayBindingRegistry Bindings { get; private set; }

    public void SetDisplay(
        int width,
        int height,
        bool borderlessFullscreen,
        float uiScale)
    {
        Settings = Settings with
        {
            WindowWidth = width,
            WindowHeight = height,
            BorderlessFullscreen = borderlessFullscreen,
            UiScale = uiScale
        };
    }

    public void SetCamera(
        bool edgeScrollEnabled,
        float panSpeedMultiplier)
    {
        Settings = Settings with
        {
            EdgeScrollEnabled = edgeScrollEnabled,
            CameraPanSpeedMultiplier = panSpeedMultiplier
        };
    }

    public void SetOnboarding(
        bool enabled)
    {
        Settings = Settings with
        {
            ShowOnboarding = enabled
        };
    }

    public void SetCameraBindings(
        RtsCameraBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        Settings = Settings with
        {
            CameraBindings = bindings
        };
        Bindings = new(Settings.GameplayBindings ?? new(), bindings);
    }

    public void SetGameplayBindings(GameplayBindings bindings)
    {
        var registry = new GameplayBindingRegistry(bindings, Settings.CameraBindings);
        Settings = Settings with { GameplayBindings = bindings };
        Bindings = registry;
    }

    public static GameFrontendAction Back() =>
        GameFrontendAction.Back;
}
