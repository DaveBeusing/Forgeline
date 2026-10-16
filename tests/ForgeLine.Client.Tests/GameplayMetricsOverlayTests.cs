using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayMetricsOverlayTests
{
    [Theory]
    [InlineData(PlatformKey.LeftShift)]
    [InlineData(PlatformKey.RightShift)]
    public void BriefGesturesToggleOnceWithAnOpenDockAndAfterResize(PlatformKey shift)
    {
        var input = new InputState();
        var dock = new PlayerActionPanelController();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.B));
        dock.Update(input, null, 1280, 720);
        bool enabled = false, held = false;
        for (int gesture = 0; gesture < 4; gesture++)
        {
            input.BeginFrame();
            Key(shift, true); Key(PlatformKey.F1, true);
            Key(PlatformKey.F1, false); Key(shift, false);
            ClientApplication.UpdateMetricsOverlay(input, false, ref enabled, ref held);
            Assert.Equal(gesture % 2 == 0, enabled);
            ClientApplication.UpdateMetricsOverlay(input, false, ref enabled, ref held);
            Assert.Equal(gesture % 2 == 0, enabled);
            dock.CreateView(gesture % 2 == 0 ? 1280 : 2560, gesture % 2 == 0 ? 720 : 1440, null, 144, 1);
            input.BeginFrame();
            ClientApplication.UpdateMetricsOverlay(input, false, ref enabled, ref held);
        }
        void Key(PlatformKey key, bool down) => input.Apply(PlatformInputEvent.KeyChanged(
            down ? PlatformInputEventKind.KeyDown : PlatformInputEventKind.KeyUp, key));
    }

    [Fact]
    public void PlainF1AndModalGesturesDoNotOpenStatistics()
    {
        var input = new InputState(); bool enabled = false, held = false;
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F1));
        ClientApplication.UpdateMetricsOverlay(input, false, ref enabled, ref held);
        Assert.False(enabled);
        input.Reset();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F1));
        ClientApplication.UpdateMetricsOverlay(input, true, ref enabled, ref held);
        ClientApplication.UpdateMetricsOverlay(input, false, ref enabled, ref held);
        Assert.False(enabled);
    }
}
