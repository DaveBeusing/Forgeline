using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class NotificationPreferenceTests
{
    [Fact]
    public void ExistingSettingsDefaultToAllAndRoundTripOnlyExplicitPreferenceChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-notification-settings", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(new ClientUserSettings { UiScale = 1.25f, ShowStudioSplash = false, CameraPanSpeedMultiplier = 1.4f });
            var adapter = new ClientSettingsFrontendAdapter(store);
            var model = adapter.Load();
            Assert.Equal(NotificationMode.All, model.Settings.Notifications.Mode);
            Assert.True(model.Settings.Notifications.ShowInformational);
            var editor = new SettingsInteractionModel();
            editor.Focus(FrontendSettingsField.NotificationMode); editor.Adjust(model, 1);
            Assert.Equal(NotificationMode.Batched, model.Settings.Notifications.Mode);
            Assert.Contains(FrontendPresentationAdapter.Settings(model, editor).DetailLines,
                row => row.Label == "NOTIFICATIONS" && row.Value == "BATCHED");
            editor.Focus(FrontendSettingsField.InformationalAlerts); editor.Adjust(model, 1);
            var applied = adapter.Apply(model);
            var loaded = store.Load().Settings;
            Assert.False(loaded.Notifications.ShowInformational);
            Assert.Equal(NotificationMode.Batched, loaded.Notifications.Mode);
            Assert.Equal(1.25f, loaded.UiScale); Assert.False(loaded.ShowStudioSplash);
            Assert.Equal(1.4f, loaded.CameraPanSpeedMultiplier);
            var live = PausedSettingsController.LiveSettings(new ClientUserSettings(), applied);
            Assert.Equal(loaded.Notifications, live.Notifications);
            model.SetNotifications(new() { Mode = NotificationMode.Quiet });
            Assert.Equal(NotificationMode.Batched, store.Load().Settings.Notifications.Mode); // Unsaved draft is isolated.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void UnknownModeIsRejectedBeforePersistence()
    {
        Assert.Throws<InvalidDataException>(() => new ClientUserSettings
            { Notifications = new() { Mode = (NotificationMode)255 } }.Validate());
        Assert.Throws<InvalidDataException>(() => new ClientUserSettings { Notifications = null! }.Validate());
        var model = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
        Assert.Throws<InvalidDataException>(() => model.SetNotifications(new() { Mode = (NotificationMode)255 }));
        Assert.Equal(NotificationMode.All, model.Settings.Notifications.Mode);
    }
}
