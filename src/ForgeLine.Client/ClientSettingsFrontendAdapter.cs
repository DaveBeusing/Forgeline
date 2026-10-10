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
        ArgumentNullException.ThrowIfNull(model);

        FrontendSettingsSnapshot snapshot =
            model.Settings;
        var settings =
            new ClientUserSettings
            {
                WindowWidth = snapshot.WindowWidth,
                WindowHeight = snapshot.WindowHeight,
                BorderlessFullscreen =
                    snapshot.BorderlessFullscreen,
                UiScale = snapshot.UiScale,
                ShowOnboarding =
                    snapshot.ShowOnboarding,
                EdgeScrollEnabled =
                    snapshot.EdgeScrollEnabled,
                CameraPanSpeedMultiplier =
                    snapshot.CameraPanSpeedMultiplier,
                CameraBindings =
                    snapshot.CameraBindings,
                GameplayBindings = snapshot.GameplayBindings ?? new()
            };

        settings.Validate();
        _store.Save(settings);
        return settings;
    }

    private static FrontendSettingsSnapshot ToSnapshot(
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
            settings.CameraBindings, settings.GameplayBindings);
    }
}
