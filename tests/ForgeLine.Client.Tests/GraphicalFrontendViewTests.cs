using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GraphicalFrontendViewTests
{
    [Fact]
    public void EverySettingsFieldHasTypedArtworkWithoutParsingItsLiveLabel()
    {
        var model = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
        var interaction = new SettingsInteractionModel();
        foreach (var field in Enum.GetValues<FrontendSettingsField>())
        {
            interaction.Focus(field);
            var view = FrontendPresentationAdapter.Settings(model, interaction);
            var row = Assert.Single(view.DetailLines, line => line.IsFocused);
            Assert.NotEqual(FrontendValueKind.Text, row.ValueKind);
            Assert.InRange(row.NormalizedValue, 0, 1);
            Assert.Single(view.SettingsTabs!, tab => tab.IsFocused);
        }
        interaction.Focus(FrontendSettingsField.UiSoundEnabled);
        Assert.Equal(FrontendValueKind.Toggle, FrontendPresentationAdapter.Settings(model, interaction).DetailLines[0].ValueKind);
        interaction.Focus(FrontendSettingsField.UiSoundVolume);
        var volume = Assert.Single(FrontendPresentationAdapter.Settings(model, interaction).DetailLines, row => row.IsFocused);
        Assert.Equal(FrontendValueKind.Slider, volume.ValueKind);
        Assert.Equal(model.Settings.FeedbackSettings.SoundVolume, volume.NormalizedValue);
    }
    [Theory]
    [InlineData(1280,720,96,.75f)]
    [InlineData(1920,1080,144,1f)]
    [InlineData(3440,1440,192,2f)]
    public void MenuAndTabArtworkStatesUseExistingHalfOpenHitRectangles(int width,int height,int dpi,float scale)
    {
        var layout = FrontendDesign.ResolveLayout(width,height,scale,dpi);
        var view = FrontendSurfaceView.MainMenu([new("disabled","Disabled",false,false),new("enabled","Enabled",true,true)]);
        var region = layout.Geometry.MenuRow(1);
        var pointer = FrontendHitTesting.WithMenuPointer(view,layout,region.X,region.Y,true);
        Assert.True(pointer.MenuEntries[1].IsHovered); Assert.True(pointer.MenuEntries[1].IsPressed);
        Assert.False(FrontendHitTesting.WithMenuPointer(view,layout,region.Right,region.Y,true).MenuEntries[1].IsHovered);
        var disabled = layout.Geometry.MenuRow(0);
        Assert.False(FrontendHitTesting.WithMenuPointer(view,layout,disabled.X,disabled.Y,true).MenuEntries[0].IsPressed);
        var model = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
        var settings = FrontendPresentationAdapter.Settings(model);
        var tab = layout.Geometry.SettingsTab(2);
        var hovering = FrontendHitTesting.WithMenuPointer(settings,layout,tab.X+1,tab.Y+1,true);
        Assert.True(hovering.SettingsTabs![2].IsPressed);
        Assert.Equal(settings.SettingsTabs![2].IsFocused,hovering.SettingsTabs[2].IsFocused);
    }
    [Fact]
    public void DisabledSaveActionIsVisibleButDoesNotAcquireAnEnabledCommand()
    {
        var empty = FrontendPresentationAdapter.LoadGame(new LoadGameModel([]));
        Assert.Empty(empty.PrimaryAction); Assert.Equal("LOAD",empty.DisabledPrimaryAction);
        var broken = FrontendPresentationAdapter.LoadGame(new LoadGameModel([new("broken","Broken","broken.json",0,LoadGameEntryState.Corrupt)]));
        Assert.Empty(broken.PrimaryAction); Assert.Equal("LOAD",broken.DisabledPrimaryAction);
        Assert.True(broken.DetailLines[0].IsWarning);
    }
    [Fact]
    public void LoadingFailureIsTypedAndNeverFabricatesCompletedReadiness()
    {
        var view = FrontendPresentationAdapter.Loading(new(FrontendLoadingPhase.Failed,"Cannot read save",1,4,"Failure"));
        Assert.True(view.IsError); Assert.Equal(.25f,view.Progress); Assert.Equal("Cannot read save",view.Status);
    }
    [Fact]
    public void StepperHoverDoesNotMakeTheDisabledDirectionActionable()
    {
        var layout = FrontendDesign.ResolveLayout(1920,1080);
        var view = FrontendSurfaceView.Detail("Settings",[new("Volume","0%",CanIncrease:true,ValueKind:FrontendValueKind.Slider)],"");
        var minus = layout.Geometry.Decrease(0); var plus = layout.Geometry.Increase(0);
        var off = FrontendHitTesting.WithDetailPointer(view,layout,minus.X+1,minus.Y+1,true);
        Assert.Equal(0,off.DetailLines[0].AdjustPress);
        var on = FrontendHitTesting.WithDetailPointer(view,layout,plus.X+1,plus.Y+1,true);
        Assert.Equal(1,on.DetailLines[0].AdjustPress);
        Assert.Null(FrontendHitTesting.DetailAdjust(minus.X+1,minus.Y+1,layout,view));
    }
}
