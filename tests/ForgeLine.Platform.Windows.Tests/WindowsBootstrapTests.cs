using ForgeLine.Platform;
using System.Runtime.InteropServices;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsBootstrapTests
{
    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    [InlineData(288u)]
    public void DpiChangesRepaintTheSameBootstrapWindow(uint dpi)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 640, 360));
        nint handle = window.NativeHandle.Value;
        var bounds = new WindowsNative.NativeRect { Left = 100, Top = 100,
            Right = 100 + (int)(800 * dpi / 96), Bottom = 100 + (int)(600 * dpi / 96) };
        nint suggested = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsNative.NativeRect>());
        try
        {
            Marshal.StructureToPtr(bounds, suggested, false);
            _ = SendMessage(handle, 0x02E0, dpi | ((nuint)dpi << 16), suggested);
            platform.UpdateBootstrap(window, "PREPARING GRAPHICS");
            Assert.Equal(dpi, window.Dpi);
            Assert.Equal(handle, window.NativeHandle.Value);
            Assert.True(platform.BootstrapPainted(window));
            Assert.False(window.ClientSize.IsEmpty);
        }
        finally { Marshal.FreeHGlobal(suggested); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

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
