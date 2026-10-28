using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class TacticalDisplaySettingsTests
{
    [Fact]
    public void PresetApplyReloadAndResetPreserveUnrelatedSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "ForgeLine-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            var original = new ClientUserSettings
            {
                UiScale = 1.5f, SceneExposure = 1.7f, CameraPanSpeedMultiplier = 1.4f,
                UiSoundEnabled = true, UiSoundVolume = .6f,
                GameplayBindings = new GameplayBindings().With(GameplayAction.Build, PlatformKey.N)
            };
            store.Save(original);
            var adapter = new ClientSettingsFrontendAdapter(store);
            var model = adapter.Load();
            model.ApplyAccessibilityPreset(AccessibilityPreset.HighContrast);
            Assert.Equal(original.UiScale, store.Load().Settings.UiScale); // Draft cancellation never writes.
            Assert.False(store.Load().Settings.TacticalDisplay.HighContrast);
            adapter.Apply(model);
            var saved = store.Load().Settings;
            Assert.True(saved.TacticalDisplay.HighContrast);
            Assert.Equal(TacticalTeamPalette.BlueOrange, saved.TacticalDisplay.TeamPalette);
            Assert.Equal(1.25f, saved.UiScale);
            Assert.True(saved.ReducedUiMotion);
            Assert.Equal(saved.TacticalDisplay, PausedSettingsController.LiveSettings(original, saved).TacticalDisplay);
            model = adapter.Load();
            model.ResetAccessibility();
            Assert.True(store.Load().Settings.TacticalDisplay.HighContrast);
            adapter.Apply(model);
            var reset = store.Load().Settings;
            Assert.Equal(new TacticalDisplaySettings(), reset.TacticalDisplay);
            Assert.Equal(1f, reset.UiScale);
            Assert.False(reset.ReducedUiMotion);
            Assert.Equal(original.SceneExposure, reset.SceneExposure);
            Assert.Equal(original.CameraPanSpeedMultiplier, reset.CameraPanSpeedMultiplier);
            Assert.Equal(PlatformKey.N, new GameplayBindingRegistry(reset.GameplayBindings, reset.CameraBindings).Key(GameplayAction.Build));
            Assert.Equal(original.UiSoundVolume, reset.UiSoundVolume);
            Assert.True(reset.UiSoundEnabled);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"version\":99,\"teamPalette\":\"BlueOrange\"}")]
    [InlineData("{\"version\":1,\"teamPalette\":255}")]
    [InlineData("null")]
    public void InvalidDisplayRecoversOnlyItsOwnPreferences(string display)
    {
        string root = Path.Combine(Path.GetTempPath(), "ForgeLine-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(new ClientUserSettings { UiScale = 1.5f, CameraPanSpeedMultiplier = 1.4f });
            File.WriteAllText(store.SettingsPath, "{\"schemaVersion\":4,\"uiScale\":1.5,\"cameraPanSpeedMultiplier\":1.4,\"tacticalDisplay\":" + display + "}");
            var loaded = store.Load();
            Assert.True(loaded.RecoveredInvalidSettings);
            Assert.Equal(1.5f, loaded.Settings.UiScale);
            Assert.Equal(1.4f, loaded.Settings.CameraPanSpeedMultiplier);
            Assert.Equal(new TacticalDisplaySettings(), loaded.Settings.TacticalDisplay);
            Assert.False(store.Load().RecoveredInvalidSettings);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(FrontendSettingsField.DisplayPreset)]
    [InlineData(FrontendSettingsField.HighContrast)]
    [InlineData(FrontendSettingsField.TeamPalette)]
    [InlineData(FrontendSettingsField.ResetAccessibility)]
    public void NewRowsRemainFocusableAndVisibleInCompactSettings(FrontendSettingsField field)
    {
        var model = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
        var interaction = new SettingsInteractionModel { VisibleCapacity = 3 };
        interaction.Focus(field);
        var surface = FrontendPresentationAdapter.Settings(model, interaction);
        Assert.Contains(surface.DetailLines!, line => line.IsFocused && line.CanIncrease);
        Assert.True(surface.DetailLines!.Count <= 3);
        interaction.Adjust(model, 1);
        model.Settings.TacticalDisplay.Validate();
    }
}
