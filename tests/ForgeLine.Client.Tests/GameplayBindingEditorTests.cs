using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayBindingEditorTests
{
    [Fact] public void KeyboardEditorRejectsCameraAndReservedCandidatesThenSavesSupportedLetter()
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
        Assert.Contains(controls.DetailLines, line => line.Label == "COMMAND PANELS" && line.Value.StartsWith("G BUILD", StringComparison.Ordinal));
        Assert.Equal(7, FrontendPresentationAdapter.Settings(model, editor).DetailLines.Count);
    }
    [Fact] public void ScrolledSettingsRowsRetainCorrectKeyboardAndPointerIdentity()
    {
        var editor = new SettingsInteractionModel(); editor.Focus(FrontendSettingsField.BindingKey);
        Assert.Equal(1, editor.VisibleStart);
        editor.FocusVisible(6);
        Assert.Equal(FrontendSettingsField.BindingKey, editor.FocusedField);
        editor.MovePrevious();
        Assert.Equal(FrontendSettingsField.BindingAction, editor.FocusedField);
        editor.Adjust(Model(), 1);
        Assert.Equal(GameplayAction.Process, editor.BindingAction);
    }
    [Fact] public void HelpSuppressesReboundHeldInputUntilPhysicalRelease()
    {
        var input = new InputState(); var help = new GameplayHelpController();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.G));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F1));
        help.Update(input, true);
        Assert.True(help.Visible); Assert.False(input.IsKeyDown(PlatformKey.G));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.G));
        Assert.False(input.IsKeyDown(PlatformKey.G));
    }
    private static SettingsModel Model() => new(new(1600, 900, false, 1, true, true, 1, new(), new()));
}
