using ForgeLine.UI;

namespace ForgeLine.Client;

internal sealed class ClientSettingsFrontendAdapter
{
    private readonly ClientSettingsStore _store;

    internal ClientSettingsFrontendAdapter(
        ClientSettingsStore store)
    {
        _store =
            store ??
            throw new ArgumentNullException(nameof(store));
    }

    internal SettingsModel Load()
    {
        ClientSettingsLoadResult result =
            _store.Load();

        return new SettingsModel(
            ToSnapshot(result.Settings));
    }

    internal ClientUserSettings Apply(
        SettingsModel model)
    {
        ClientUserSettings settings = CreateCandidate(model);
        _store.Save(settings);
        return settings;
    }

    internal ClientUserSettings CreateCandidate(SettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        FrontendSettingsSnapshot snapshot =
            model.Settings;
        ClientUserSettings saved = _store.Load().Settings;
        var settings =
            saved with
            {
                WindowWidth = snapshot.WindowWidth,
                WindowHeight = snapshot.WindowHeight,
                BorderlessFullscreen =
                    snapshot.BorderlessFullscreen,
                UiScale = snapshot.UiScale,
                RenderQuality = snapshot.GraphicsSettings ?? saved.RenderQuality with { Quality = (ForgeLine.Presentation.RenderQuality)snapshot.RenderQuality },
                SceneAtmosphere = saved.SceneAtmosphere with { Quality = ForgeLine.Graphics.AtmosphereQuality.Disabled },
                Particles = ForgeLine.Presentation.ParticleSettings.Default,
                ShowOnboarding =
                    snapshot.ShowOnboarding,
                EdgeScrollEnabled =
                    snapshot.EdgeScrollEnabled,
                CameraPanSpeedMultiplier =
                    snapshot.CameraPanSpeedMultiplier,
                CameraBindings =
                    snapshot.CameraBindings,
                GameplayBindings = snapshot.GameplayBindings ?? new(),
                ReducedUiMotion = snapshot.FeedbackSettings.ReducedMotion,
                UiSoundEnabled = snapshot.FeedbackSettings.SoundEnabled,
                UiSoundVolume = snapshot.FeedbackSettings.SoundVolume,
                WorldAudio = snapshot.WorldAudio
            };

        settings.Validate();
        return settings;
    }

    internal static FrontendSettingsSnapshot ToSnapshot(
        ClientUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new FrontendSettingsSnapshot(
            settings.WindowWidth,
            settings.WindowHeight,
            settings.BorderlessFullscreen,
            settings.UiScale,
            settings.ShowOnboarding,
            settings.EdgeScrollEnabled,
            settings.CameraPanSpeedMultiplier,
            settings.CameraBindings, settings.GameplayBindings, (FrontendRenderQuality)settings.RenderQuality.Quality, settings.CreateRenderQualitySettings(), settings.CreateUiFeedbackSettings()) { WorldAudio = settings.WorldAudio };
    }
}
