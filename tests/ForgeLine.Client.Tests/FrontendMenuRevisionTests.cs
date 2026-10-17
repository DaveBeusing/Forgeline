using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FrontendMenuRevisionTests
{
    [Fact]
    public void AudioTabIsDirectlyReachableAndSavesCuePreferences()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-menu-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            var adapter = new ClientSettingsFrontendAdapter(store);
            var model = adapter.Load();
            var interaction = new SettingsInteractionModel();
            var geometry = FrontendDesign.ResolveLayout(1920,1080).Geometry;
            var audio = geometry.SettingsTab((int)FrontendSettingsSection.Audio);
            var input = new InputState();
            input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,(int)audio.X+2,(int)audio.Y+2));
            Assert.True(interaction.HandleSectionInput(input,geometry));
            Assert.Equal(FrontendSettingsSection.Audio,interaction.Section);
            var view = FrontendPresentationAdapter.Settings(model,interaction);
            Assert.Equal(7,view.DetailLines.Count);
            Assert.Contains(view.SettingsTabs!,tab=>tab.Id=="Audio" && tab.IsFocused);
            Assert.Equal("GAME AUDIO",view.DetailLines[0].Label);
            Assert.Equal("GAME VOLUME",view.DetailLines[1].Label);
            Assert.Equal("BATTLEFIELD VOLUME",view.DetailLines[2].Label);
            Assert.Equal("ALERT VOLUME",view.DetailLines[3].Label);
            Assert.Equal("AMBIENCE VOLUME",view.DetailLines[4].Label);
            Assert.Equal("INTERFACE SOUND",view.DetailLines[5].Label);
            Assert.Equal("INTERFACE VOLUME",view.DetailLines[6].Label);
            interaction.Adjust(model,1);
            interaction.MoveNext(); interaction.Adjust(model,-1);
            interaction.Focus(FrontendSettingsField.UiSoundEnabled);
            interaction.Adjust(model,1);
            interaction.MoveNext(); interaction.Adjust(model,1);
            adapter.Apply(model);
            var saved = store.Load().Settings;
            Assert.False(saved.WorldAudio.Enabled);
            Assert.InRange(saved.WorldAudio.Master,.599f,.601f);
            Assert.True(saved.UiSoundEnabled);
            Assert.InRange(saved.UiSoundVolume,.399f,.401f);
            Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void EverySettingsFieldRemainsReachableWithinItsSection(int capacity)
    {
        var interaction = new SettingsInteractionModel { VisibleCapacity=capacity };
        var found = new HashSet<FrontendSettingsField>();
        foreach(var section in Enum.GetValues<FrontendSettingsSection>())
        {
            interaction.SelectSection(section);
            var first = interaction.FocusedField;
            do
            {
                found.Add(interaction.FocusedField);
                Assert.Contains(interaction.FocusedField,interaction.VisibleFields);
                interaction.MoveNext();
            } while(interaction.FocusedField!=first);
        }
        Assert.Equal(Enum.GetValues<FrontendSettingsField>().Length,found.Count);
    }

    [Fact]
    public void KeyboardTabsCycleSectionsWithoutEditingPreferences()
    {
        var interaction = new SettingsInteractionModel();
        var input = new InputState();
        var geometry = FrontendDesign.ResolveLayout(1280,720).Geometry;
        foreach(var expected in new[]{FrontendSettingsSection.Graphics,FrontendSettingsSection.Audio,
            FrontendSettingsSection.Bindings,FrontendSettingsSection.General})
        {
            input.Reset();
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.Tab));
            Assert.True(interaction.HandleSectionInput(input,geometry));
            Assert.Equal(expected,interaction.Section);
        }
    }
}
