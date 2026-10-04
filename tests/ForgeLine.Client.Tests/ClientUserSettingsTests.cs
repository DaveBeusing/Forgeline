using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientUserSettingsTests
{
    [Fact]
    public void DefaultsValidateAndCreateExpectedWindowConfiguration()
    {
        var settings =
            new ClientUserSettings();

        settings.Validate();
        WindowConfiguration window =
            settings.CreateWindowConfiguration();

        Assert.Equal(
            1_600,
            window.Width);
        Assert.Equal(
            900,
            window.Height);
        Assert.Equal(
            WindowMode.Windowed,
            window.Mode);
    }

    [Fact]
    public void PersistedBorderlessSettingsCreateBorderlessStartupConfiguration()
    {
        string root =
            CreateTemporaryRoot();

        try
        {
            var store =
                new ClientSettingsStore(
                    root);
            store.Save(
                new ClientUserSettings
                {
                    WindowWidth = 1_920,
                    WindowHeight = 1_080,
                    BorderlessFullscreen = true
                });

            ClientSettingsLoadResult loaded =
                store.Load();
            WindowConfiguration window =
                loaded.Settings.CreateWindowConfiguration();

            Assert.False(
                loaded.CreatedDefaults);
            Assert.False(
                loaded.RecoveredInvalidSettings);
            Assert.True(
                loaded.Settings.BorderlessFullscreen);
            Assert.Equal(
                1_920,
                window.Width);
            Assert.Equal(
                1_080,
                window.Height);
            Assert.Equal(
                WindowMode.BorderlessFullscreen,
                window.Mode);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void InvalidUiScaleIsRejected()
    {
        var settings =
            new ClientUserSettings
            {
                UiScale = 3.0f
            };

        Assert.Throws<InvalidDataException>(
            settings.Validate);
    }

    [Fact]
    public void DuplicatePrimaryCameraBindingsAreRejected()
    {
        var settings =
            new ClientUserSettings
            {
                CameraBindings =
                    new RtsCameraBindings
                    {
                        PanForward =
                            PlatformKey.W,
                        PanBackward =
                            PlatformKey.W
                    }
            };

        Assert.Throws<InvalidDataException>(
            settings.Validate);
    }

    [Fact]
    public void StoreCreatesAndRoundTripsValidatedSettings()
    {
        string root =
            CreateTemporaryRoot();

        try
        {
            var store =
                new ClientSettingsStore(
                    root);

            ClientSettingsLoadResult created =
                store.Load();

            Assert.True(
                created.CreatedDefaults);
            Assert.False(
                created.RecoveredInvalidSettings);
            Assert.True(
                File.Exists(
                    store.SettingsPath));

            var custom =
                created.Settings with
                {
                    UiScale = 1.25f,
                    EdgeScrollEnabled = false,
                    CameraPanSpeedMultiplier = 1.4f,
                    CameraBindings =
                        created.Settings.CameraBindings with
                        {
                            PanForward =
                                PlatformKey.Up,
                            PanForwardAlternate =
                                PlatformKey.W
                        }
                };

            store.Save(
                custom);

            ClientSettingsLoadResult loaded =
                store.Load();

            Assert.False(
                loaded.CreatedDefaults);
            Assert.False(
                loaded.RecoveredInvalidSettings);
            Assert.Equal(
                1.25f,
                loaded.Settings.UiScale);
            Assert.False(
                loaded.Settings.EdgeScrollEnabled);
            Assert.Equal(
                1.4f,
                loaded.Settings.CameraPanSpeedMultiplier);
            Assert.Equal(
                PlatformKey.Up,
                loaded.Settings.CameraBindings.PanForward);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void InvalidSettingsAreQuarantinedAndRecovered()
    {
        string root =
            CreateTemporaryRoot();

        try
        {
            var store =
                new ClientSettingsStore(
                    root);
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    store.SettingsPath)!);
            File.WriteAllText(
                store.SettingsPath,
                "{ invalid json");

            ClientSettingsLoadResult loaded =
                store.Load();

            Assert.True(
                loaded.RecoveredInvalidSettings);
            Assert.False(
                loaded.CreatedDefaults);
            loaded.Settings.Validate();
            Assert.True(
                File.Exists(
                    store.SettingsPath));

            string directory =
                Path.GetDirectoryName(
                    store.SettingsPath)!;
            Assert.Single(
                Directory.GetFiles(
                    directory,
                    "settings.json.invalid-*"));
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string path =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-client-tests",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(
            path);
        return path;
    }
}
