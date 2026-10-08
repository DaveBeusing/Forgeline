using System.Runtime.InteropServices;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlatformCanCreateWindowAfterPreviousWindowIsDisposed(bool queueMessageBeforeRecreation)
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

        if (queueMessageBeforeRecreation)
        {
            // A posted thread message defers generation of the previous window's WM_QUIT.
            Assert.NotEqual(0, PostMessage(0, 0x8000, 0, 0));
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

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern int PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
