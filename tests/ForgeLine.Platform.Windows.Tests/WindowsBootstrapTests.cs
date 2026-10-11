using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsBootstrapTests
{
    [Fact]
    public void BootstrapPaintsBeforeGraphicsAndRetainsItsWindowThroughConfigurationAndHandoff()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 640, 360));
        nint handle = window.NativeHandle.Value;
        Assert.True(platform.BootstrapPainted(window));
        platform.UpdateBootstrap(window, "LOADING SETTINGS");
        platform.ConfigureBootstrap(window, new WindowConfiguration("FORGELINE", 800, 600));
        Assert.Equal(handle, window.NativeHandle.Value);
        Assert.Equal(new WindowSize(800, 600), window.ClientSize);
        platform.ConfigureBootstrap(window, new WindowConfiguration("FORGELINE", 800, 600,
            mode: WindowMode.BorderlessFullscreen));
        Assert.Equal(handle, window.NativeHandle.Value);
        Assert.Equal(WindowMode.BorderlessFullscreen, window.Mode);
        platform.CompleteBootstrap(window);
        Assert.False(((WindowsWindow)window).BootstrapActive);
        Assert.True(platform.PumpEvents());
        Assert.True(window.IsOpen);
    }

    [Fact]
    public void FailureSurfaceCanReturnAfterGraphicsHandoffAndCloseNormally()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 640, 360));
        platform.CompleteBootstrap(window);
        platform.UpdateBootstrap(window, "STARTUP FAILED\nRuntime manifest is missing.", failed: true);
        Assert.True(((WindowsWindow)window).BootstrapActive);
        Assert.True(platform.BootstrapPainted(window));
        window.RequestClose();
        Assert.False(window.IsOpen);
        Assert.False(platform.PumpEvents());
    }

    [Fact]
    public void BootstrapOperationsRequireTheNativeOwner()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 640, 360));
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(() =>
            platform.UpdateBootstrap(window, "WRONG OWNER")));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(error);
    }
}
