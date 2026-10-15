using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayBindingEditorTests
{
    [Fact]
    public void KeyboardEditorRejectsCameraAndReservedCandidatesThenSavesSupportedLetter()
    {
        var model = Model(); var editor = new SettingsInteractionModel();
        editor.Focus(FrontendSettingsField.BindingKey);
        editor.Adjust(model, 1); // B -> C conflicts with cancel job.
        Assert.False(editor.CanApply);
        Assert.Contains("CONFLICT", editor.BindingFeedback);
        Assert.Empty(model.Settings.GameplayBindings!.Overrides);
        Assert.Empty(FrontendPresentationAdapter.Settings(model, editor).PrimaryAction);
        for (int i = 0; i < 80 && editor.BindingCandidate != PlatformKey.G; i++) editor.Adjust(model, 1);
        Assert.Equal(PlatformKey.G, editor.BindingCandidate);
        Assert.True(editor.CanApply);
        Assert.Equal(PlatformKey.G, model.Bindings.Key(GameplayAction.Build));
        Assert.Equal(PlatformKey.W, model.Settings.CameraBindings.PanForward);
        var controls = FrontendPresentationAdapter.Controls(model.Settings.CameraBindings, gameplay: model.Bindings);
        Assert.Contains(controls.DetailLines, line => line.Label == "BUILD" && line.Value == "G");
        Assert.Equal(2, FrontendPresentationAdapter.Settings(model, editor).DetailLines.Count);
    }
    [Fact]
    public void ScrolledSettingsRowsRetainCorrectKeyboardAndPointerIdentity()
    {
        var editor = new SettingsInteractionModel(); editor.Focus(FrontendSettingsField.BindingKey);
        Assert.Equal(0, editor.VisibleStart);
        editor.FocusVisible(1);
        Assert.Equal(FrontendSettingsField.BindingKey, editor.FocusedField);
        editor.MovePrevious();
        Assert.Equal(FrontendSettingsField.BindingAction, editor.FocusedField);
        editor.Adjust(Model(), 1);
        Assert.Equal(GameplayAction.Process, editor.BindingAction);
    }
    [Fact]
    public void ControlsRetainReboundBaseFocusAlongsideLegacyHomeCameraMapping()
    {
        var camera = new RtsCameraBindings { PanForward = PlatformKey.Home };
        var bindings = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.FocusBase, PlatformKey.G), camera);
        var controls = FrontendPresentationAdapter.Controls(camera, gameplay: bindings);
        Assert.Contains(controls.DetailLines, line => line.Label == "HOME" && line.Value == "CAMERA BINDING");
        Assert.Contains(controls.DetailLines, line => line.Label == "BASE FOCUS" && line.Value == "G");
    }
    [Fact]
    public void CandidateCyclingTraversesReservedUnknownEntryWithoutJumpingToDefault()
    {
        var model = Model(); model.SetGameplayBindings(new GameplayBindings().With(GameplayAction.Build, PlatformKey.Z));
        var editor = new SettingsInteractionModel(); editor.Focus(FrontendSettingsField.BindingKey);
        editor.Adjust(model, 1);
        Assert.Equal(PlatformKey.Unknown, editor.BindingCandidate); Assert.False(editor.CanApply);
        editor.Adjust(model, 1);
        Assert.Equal(PlatformKey.W, editor.BindingCandidate); Assert.False(editor.CanApply);
        Assert.Contains("CAMERA", editor.BindingFeedback);
        Assert.Equal(PlatformKey.Z, model.Bindings.Key(GameplayAction.Build));
    }
    private static SettingsModel Model() => new(new(1600, 900, false, 1, true, true, 1, new(), new()));
}
