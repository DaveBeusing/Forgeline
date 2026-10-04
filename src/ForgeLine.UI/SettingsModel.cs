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
    RtsCameraBindings CameraBindings);

public sealed class SettingsModel
{
    public SettingsModel(
        FrontendSettingsSnapshot settings)
    {
        Settings = settings;
    }

    public FrontendSettingsSnapshot Settings { get; private set; }

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
    }

    public static GameFrontendAction Back() =>
        GameFrontendAction.Back;
}
