using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsWindowLifecycleIntegrationTests
{
    [Fact]
    public void BorderlessStartupAndRoundTripKeepValidRenderableClientState()
    {
        using var platform =
            new WindowsPlatform();
        using IWindow window =
            platform.CreateWindow(
                new WindowConfiguration(
                    "FORGELINE Window Lifecycle Test",
                    1_280,
                    720,
                    resizable: true,
                    WindowMode.BorderlessFullscreen));

        Assert.True(
            window.NativeHandle.IsValid);
        Assert.Equal(
            WindowMode.BorderlessFullscreen,
            window.Mode);
        Assert.False(
            window.ClientSize.IsEmpty);
        Assert.False(
            window.IsMinimized);

        window.SetMode(
            WindowMode.Windowed);

        Assert.Equal(
            WindowMode.Windowed,
            window.Mode);
        Assert.False(
            window.ClientSize.IsEmpty);

        window.SetMode(
            WindowMode.BorderlessFullscreen);

        Assert.Equal(
            WindowMode.BorderlessFullscreen,
            window.Mode);
        Assert.False(
            window.ClientSize.IsEmpty);

        window.SetMode(
            WindowMode.Windowed);

        Assert.Equal(
            WindowMode.Windowed,
            window.Mode);
        Assert.False(
            window.ClientSize.IsEmpty);

        bool sawFinalValidResize = false;
        bool sawModeChange = false;

        while (window.TryDequeueEvent(
                   out WindowEvent windowEvent))
        {
            if (windowEvent.Kind ==
                    WindowEventKind.Resized &&
                !windowEvent.ClientSize.IsEmpty)
            {
                sawFinalValidResize = true;
            }

            if (windowEvent.Kind ==
                WindowEventKind.ModeChanged)
            {
                sawModeChange = true;
            }
        }

        Assert.True(
            sawFinalValidResize);
        Assert.True(
            sawModeChange);
    }
    [Fact]
    public void PlatformCanCreateWindowAfterPreviousWindowIsDisposed()
    {
        using var platform =
            new WindowsPlatform();
        var configuration =
            new WindowConfiguration(
                "FORGELINE Window Recreation Test",
                1_280,
                720,
                resizable: true,
                WindowMode.Windowed);

        using (IWindow firstWindow =
               platform.CreateWindow(configuration))
        {
            Assert.True(
                platform.PumpEvents());
            Assert.True(
                firstWindow.IsOpen);
        }

        using IWindow secondWindow =
            platform.CreateWindow(configuration);

        Assert.True(
            platform.PumpEvents());
        Assert.True(
            secondWindow.IsOpen);
        Assert.True(
            secondWindow.NativeHandle.IsValid);
    }

}
