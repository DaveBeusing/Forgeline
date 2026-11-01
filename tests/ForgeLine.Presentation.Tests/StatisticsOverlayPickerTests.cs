using ForgeLine.Input;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class StatisticsOverlayPickerTests
{
    [Fact]
    public void PlainF1OpensAndClosesOnlyTheNonmodalPicker()
    {
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        var input = new InputState();
        bool performance = false;

        Key(input, PlatformKey.F1);
        Assert.True(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        Assert.True(picker.Visible);
        Assert.False(debug.Enabled);
        Assert.False(performance);

        Key(input, PlatformKey.F1);
        Assert.True(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        Assert.False(picker.Visible);
        Assert.False(debug.Enabled);
    }

    [Fact]
    public void PublicPickerOnlyOffersSafePerformanceStatistics()
    {
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        var input = new InputState();
        bool performance = false;
        Key(input, PlatformKey.F1);
        picker.Update(input, true, false, 1920, 1080, 1, debug, ref performance);
        var layout = StatisticsOverlayPickerGeometry.Create(1920, 1080, 1, false);
        Assert.Equal(1, layout.RowCount);
        Click(input, layout.X + 20, layout.Y + layout.HeaderHeight + 7);
        Assert.True(picker.Update(input, true, false, 1920, 1080, 1, debug, ref performance));
        Assert.True(performance);
        Assert.False(debug.Enabled);
        Assert.Equal(DebugOverlayCategory.None, picker.Capture(false, performance, debug.View).Categories);
        Assert.False(input.TryGetMousePressPosition(PlatformMouseButton.Left, out _));
    }

    [Fact]
    public void DeveloperCategoriesToggleIndependentlyAndStopHeavyCaptureWhenCleared()
    {
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        var input = new InputState();
        bool performance = false;
        Key(input, PlatformKey.F1);
        picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance);
        var layout = StatisticsOverlayPickerGeometry.Create(1920, 1080, 1, true);

        void Row(int row)
        {
            Click(input, layout.X + 15,
                layout.Y + layout.HeaderHeight + (row + .5f) * layout.RowHeight);
            Assert.True(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        }

        Row(1); // Rendering alone is presentation-only.
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Rendering));
        Assert.False(DebugOverlayPolicy.RequiresPresentationDebugSnapshot(
            debug.View.EffectiveCategories, StrategicOverlayMode.None));
        Row(2); // Navigation needs simulation debug capture.
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Navigation));
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Rendering));
        Assert.True(DebugOverlayPolicy.RequiresPresentationDebugSnapshot(
            debug.View.EffectiveCategories, StrategicOverlayMode.None));
        Row(2);
        Assert.False(DebugOverlayPolicy.RequiresPresentationDebugSnapshot(
            debug.View.EffectiveCategories, StrategicOverlayMode.None));
        Row(1);
        Assert.Equal(DebugOverlayCategory.None, debug.View.EffectiveCategories);
        Assert.False(debug.Enabled);
        Assert.True(picker.Visible);
    }

    [Fact]
    public void EscapeAndOutsideClickClosePickerButRetainSelectedOverlays()
    {
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        var input = new InputState();
        bool performance = false;
        Key(input, PlatformKey.F1);
        picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance);
        var rect = StatisticsOverlayPickerGeometry.Create(1920, 1080, 1, true);
        Click(input, rect.X + 15, rect.Y + rect.HeaderHeight + rect.RowHeight * 1.5f);
        picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance);
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Rendering));

        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Escape));
        Assert.True(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        Assert.False(input.WasKeyPressed(PlatformKey.Escape));
        Assert.False(picker.Visible);
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Rendering));

        Key(input, PlatformKey.F1);
        picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance);
        Click(input, 900, 500);
        Assert.True(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        Assert.False(picker.Visible);
        Assert.True(debug.View.IsEnabled(DebugOverlayCategory.Rendering));
    }

    [Theory]
    [InlineData(1920, 1080, 1f)]
    [InlineData(2560, 1440, 1.5f)]
    [InlineData(3840, 2160, 2f)]
    [InlineData(3440, 1440, 2f)]
    public void PanelFitsViewportAndMaintainsSharedHitTesting(int width, int height, float scale)
    {
        var rect = StatisticsOverlayPickerGeometry.Create(width, height, scale, true);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width);
        Assert.True(rect.Y + rect.Height <= height);
        for (int row = 0; row < rect.RowCount; row++)
            Assert.Equal(row, rect.RowAt(rect.Y + rect.HeaderHeight + (row + .5f) * rect.RowHeight));
    }

    [Fact]
    public void ModalAndModifierGesturesNeverOpenPickerAndRevocationClearsDebug()
    {
        var picker = new StatisticsOverlayPicker();
        var debug = new DebugOverlayController();
        var input = new InputState();
        bool performance = false;

        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F1));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.F1));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.LeftShift));
        Assert.False(picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance));
        Assert.False(picker.Visible);

        Key(input, PlatformKey.F1);
        Assert.False(picker.Update(input, false, true, 1920, 1080, 1, debug, ref performance));
        Assert.False(picker.Visible);

        Key(input, PlatformKey.F1);
        picker.Update(input, true, true, 1920, 1080, 1, debug, ref performance);
        debug.SetCategories(DebugOverlayCategory.Navigation);
        debug.SetEnabled(true);
        picker.ClearDeveloperSelection(debug);
        Assert.False(picker.Visible);
        Assert.False(debug.Enabled);
        Assert.Equal(DebugOverlayCategory.None, debug.Categories);
    }

    private static void Key(InputState input, PlatformKey key)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
    }

    private static void Click(InputState input, float x, float y)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.MouseButtonChanged(
            PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, (int)x, (int)y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(
            PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, (int)x, (int)y));
    }
}
