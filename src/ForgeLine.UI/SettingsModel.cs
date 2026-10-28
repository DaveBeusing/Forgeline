using ForgeLine.Audio;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.UI;

public enum FrontendRenderQuality { Low, Medium, High, Ultra }

public readonly record struct FrontendSettingsSnapshot(
    int WindowWidth,
    int WindowHeight,
    bool BorderlessFullscreen,
    float UiScale,
    bool ShowOnboarding,
    bool EdgeScrollEnabled,
    float CameraPanSpeedMultiplier,
    RtsCameraBindings CameraBindings, GameplayBindings? GameplayBindings = null,
    FrontendRenderQuality RenderQuality = FrontendRenderQuality.Medium, RenderQualitySettings? GraphicsSettings = null, UiFeedbackSettings FeedbackSettings = default)
{
    public WorldAudioSettings WorldAudio { get; init; } = WorldAudioSettings.Default;
    public NotificationPreferences Notifications { get; init; } = NotificationPreferences.Default;
    public HoverTooltipSettings Tooltips { get; init; } = HoverTooltipSettings.Default;
    public TacticalDisplaySettings TacticalDisplay { get; init; } = new();
}

public enum AccessibilityPreset : byte { Standard, Readable, HighContrast }

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
    public RenderQualitySettings GraphicsSettings => Settings.GraphicsSettings ?? new RenderQualitySettings { Quality = (RenderQuality)Settings.RenderQuality };

    public void SetUiFeedback(UiFeedbackSettings settings)
    {
        settings.Validate();
        Settings = Settings with { FeedbackSettings = settings };
    }

    public void SetNotifications(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences); preferences.Validate();
        Settings = Settings with { Notifications = preferences };
    }

    public void SetTacticalDisplay(TacticalDisplaySettings settings)
    {
        settings.Validate();
        Settings = Settings with { TacticalDisplay = settings };
    }

    public AccessibilityPreset? DisplayPreset => (Settings.UiScale, Settings.FeedbackSettings.ReducedMotion,
        Settings.TacticalDisplay.HighContrast, Settings.TacticalDisplay.TeamPalette) switch
    {
        (1f, false, false, TacticalTeamPalette.Standard) => AccessibilityPreset.Standard,
        (1.25f, true, false, TacticalTeamPalette.Standard) => AccessibilityPreset.Readable,
        (1.25f, true, true, TacticalTeamPalette.BlueOrange) => AccessibilityPreset.HighContrast,
        _ => null
    };

    public void ApplyAccessibilityPreset(AccessibilityPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        Settings = Settings with
        {
            UiScale = preset == AccessibilityPreset.Standard ? 1f : 1.25f,
            TacticalDisplay = new() { HighContrast = preset == AccessibilityPreset.HighContrast,
                TeamPalette = preset == AccessibilityPreset.HighContrast ? TacticalTeamPalette.BlueOrange : TacticalTeamPalette.Standard },
            FeedbackSettings = Settings.FeedbackSettings with { ReducedMotion = preset != AccessibilityPreset.Standard }
        };
    }

    public void ResetAccessibility()
    {
        ApplyAccessibilityPreset(AccessibilityPreset.Standard);
        Settings = Settings with { Tooltips = HoverTooltipSettings.Default };
    }

    public void SetTooltips(HoverTooltipSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = Settings with { Tooltips = settings };
    }

    public void SetWorldAudio(WorldAudioSettings settings)
    {
        settings.Validate();
        Settings = Settings with { WorldAudio = settings };
    }

    public void SetGraphicsSettings(RenderQualitySettings settings)
    {
        settings.Validate();
        Settings = Settings with { RenderQuality = (FrontendRenderQuality)settings.Quality, GraphicsSettings = settings };
    }

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

    public void SetRenderQuality(FrontendRenderQuality quality)
    {
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        SetGraphicsSettings(new RenderQualitySettings
        {
            Quality = (RenderQuality)quality, ColorGrade = GraphicsSettings.ColorGrade,
            Environment = GraphicsSettings.Environment, Vegetation = GraphicsSettings.Vegetation,
            TerrainResidency = GraphicsSettings.TerrainResidency
        });
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
