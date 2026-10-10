using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayBindingSettingsTests
{
    [Theory]
    [InlineData("\"gameplayBindings\": {\"overrides\":[{\"action\":\"Build\",\"key\":\"P\"}]},")]
    [InlineData("\"gameplayBindings\": {\"overrides\":[{\"action\":\"UnknownAction\",\"key\":\"B\"}]},")]
    [InlineData("\"gameplayBindings\": null,")]
    [InlineData("\"gameplayBindings\": {\"overrides\":[{\"action\":\"Build\",\"key\":\"Down\"},{\"action\":\"Build\",\"key\":\"Up\"}]},")]
    public void RecoversOnlyBadGameplayBindings(string gameplay)
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-binding-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
            File.WriteAllText(store.SettingsPath, "{" + gameplay + "\"schemaVersion\":1,\"uiScale\":1.5,\"cameraBindings\":{\"panForward\":\"Home\"}}");
            var loaded = store.Load();
            Assert.True(loaded.RecoveredInvalidSettings);
            Assert.Contains("preserved", loaded.RecoveryMessage!);
            Assert.Equal(PlatformKey.Home, loaded.Settings.CameraBindings.PanForward);
            Assert.Equal(1.5f, loaded.Settings.UiScale);
            Assert.Empty(loaded.Settings.GameplayBindings.Overrides);
            Assert.Equal(2, loaded.Settings.SchemaVersion);
            Assert.False(store.Load().RecoveredInvalidSettings);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void MigratesLegacyAndPersistsRebindWithoutChangingCamera()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-binding-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
            File.WriteAllText(store.SettingsPath, "{\"schemaVersion\":1,\"cameraBindings\":{\"panForward\":\"Home\"}}");
            var legacy = store.Load();
            Assert.False(legacy.RecoveredInvalidSettings);
            Assert.Equal(PlatformKey.Home, legacy.Settings.CameraBindings.PanForward);
            store.Save(legacy.Settings with { GameplayBindings = new GameplayBindings().With(GameplayAction.Build, PlatformKey.W) });
            var rebound = store.Load().Settings;
            Assert.Equal(PlatformKey.W, new GameplayBindingRegistry(rebound.GameplayBindings, rebound.CameraBindings).Key(GameplayAction.Build));
            Assert.Equal(PlatformKey.Home, rebound.CameraBindings.PanForward);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
