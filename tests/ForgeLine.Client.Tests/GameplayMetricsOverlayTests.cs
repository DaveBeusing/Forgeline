using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayMetricsOverlayTests
{
    [Fact]
    public void PlainF1OpensPublicStatisticsWithoutDeveloperTools()
    {
        var input = new InputState();
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        bool metrics = false;

        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F1));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.F1));
        Assert.True(picker.Update(input, true, false, 1920, 1080, 1, debug, ref metrics));
        Assert.True(picker.Visible);
        Assert.False(debug.Enabled);
        Assert.False(metrics);
        Assert.Equal(DebugOverlayCategory.None, picker.Capture(false, metrics, debug.View).Categories);
    }

    [Fact]
    public void NormalControlsRetainF10AndDoNotOfferF1AsAnAssignableBinding()
    {
        var bindings = GameplayBindingRegistry.Default;
        Assert.Equal(PlatformKey.F10, bindings.Key(GameplayAction.Overlay));
        Assert.False(GameplayBindingRegistry.IsAssignable(PlatformKey.F1));
        Assert.Contains(GameplayBindingRegistry.FixedBindings, line => line.Contains("F1 STATISTICS"));
        Assert.Contains(GameplayBindingRegistry.FixedBindingsFor(true), line => line.Contains("DEVELOPER TOOLS"));
        Assert.DoesNotContain(GameplayBindingRegistry.FixedBindingsFor(true), line =>
            line.Contains("F1 DEVELOPER"));
    }
}
