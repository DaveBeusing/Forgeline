using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSettingsFrontendAdapterTests
{
    [Fact]
    public void LoadUsesExistingSettingsStore()
    {
        string root = CreateTemporaryRoot();

        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(
                new ClientUserSettings
                {
                    WindowWidth = 1_920,
                    WindowHeight = 1_080,
                    UiScale = 1.25f,
                    EdgeScrollEnabled = false
                });
            var adapter =
                new ClientSettingsFrontendAdapter(store);

            SettingsModel model =
                adapter.Load();

            Assert.Equal(1_920, model.Settings.WindowWidth);
            Assert.Equal(1_080, model.Settings.WindowHeight);
            Assert.Equal(1.25f, model.Settings.UiScale);
            Assert.False(model.Settings.EdgeScrollEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPersistsThroughExistingSettingsStore()
    {
        string root = CreateTemporaryRoot();

        try
        {
            var store = new ClientSettingsStore(root);
            var adapter =
                new ClientSettingsFrontendAdapter(store);
            SettingsModel model =
                adapter.Load();

            model.SetDisplay(
                2_560,
                1_440,
                borderlessFullscreen: true,
                uiScale: 1.5f);
            model.SetCamera(
                edgeScrollEnabled: false,
                panSpeedMultiplier: 1.4f);
            model.SetOnboarding(false);
            model.SetCameraBindings(
                model.Settings.CameraBindings with
                {
                    PanForward = PlatformKey.Up,
                    PanForwardAlternate = PlatformKey.W
                });

            ClientUserSettings applied =
                adapter.Apply(model);
            ClientSettingsLoadResult persisted =
                store.Load();

            Assert.Equal(2_560, applied.WindowWidth);
            Assert.Equal(1_440, applied.WindowHeight);
            Assert.True(applied.BorderlessFullscreen);
            Assert.Equal(1.5f, applied.UiScale);
            Assert.False(applied.EdgeScrollEnabled);
            Assert.Equal(1.4f, applied.CameraPanSpeedMultiplier);
            Assert.False(applied.ShowOnboarding);
            Assert.Equal(
                PlatformKey.Up,
                persisted.Settings.CameraBindings.PanForward);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidFrontendValuesAreRejectedByExistingValidation()
    {
        string root = CreateTemporaryRoot();

        try
        {
            var store = new ClientSettingsStore(root);
            var adapter =
                new ClientSettingsFrontendAdapter(store);
            SettingsModel model =
                adapter.Load();

            model.SetDisplay(
                100,
                100,
                borderlessFullscreen: false,
                uiScale: 4.0f);

            Assert.Throws<InvalidDataException>(
                () => adapter.Apply(model));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string path =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-settings-frontend-tests",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
