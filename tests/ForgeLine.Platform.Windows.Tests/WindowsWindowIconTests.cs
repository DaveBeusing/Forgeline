using System.Runtime.InteropServices;
using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowsWindowIconTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAndBootstrapWindowsOwnBothIconsUntilTheyClose(bool bootstrap)
    {
        using var platform = new WindowsPlatform();
        var configuration = new WindowConfiguration("FORGELINE icon test", 640, 360);
        using IWindow window = bootstrap ? platform.CreateBootstrapWindow(configuration) : platform.CreateWindow(configuration);
        nint hwnd = window.NativeHandle.Value;
        nint small = Icon(hwnd, 0);
        nint large = Icon(hwnd, 1);
        Assert.NotEqual(0, small);
        Assert.NotEqual(0, large);
        Assert.NotEqual(small, large);
        AssertIconSize(small, WindowsNative.GetSystemMetricsForDpi(49, window.Dpi));
        AssertIconSize(large, WindowsNative.GetSystemMetricsForDpi(11, window.Dpi));
        if (bootstrap)
        {
            platform.ConfigureBootstrap(window, new WindowConfiguration("FORGELINE", 800, 600));
            platform.CompleteBootstrap(window);
        }
        window.SetMode(WindowMode.BorderlessFullscreen);
        window.SetMode(WindowMode.Windowed);
        Assert.Equal(hwnd, window.NativeHandle.Value);
        Assert.Equal(small, Icon(hwnd, 0));
        Assert.Equal(large, Icon(hwnd, 1));
        window.RequestClose();
        AssertReleased(small);
        AssertReleased(large);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    [InlineData(288u)]
    public void DpiTransitionSetsSizedIconsAndReleasesReplacedHandles(uint dpi)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE icon DPI test", 640, 360));
        nint hwnd = window.NativeHandle.Value;
        uint originalDpi = window.Dpi;
        nint oldSmall = Icon(hwnd, 0);
        nint oldLarge = Icon(hwnd, 1);
        _ = WindowsNative.SendMessage(hwnd, WindowsNative.WmDpiChanged, dpi | ((nuint)dpi << 16), 0);
        Assert.Equal(hwnd, window.NativeHandle.Value);
        Assert.Equal(dpi, window.Dpi);
        AssertIconSize(Icon(hwnd, 0), WindowsNative.GetSystemMetricsForDpi(49, dpi));
        AssertIconSize(Icon(hwnd, 1), WindowsNative.GetSystemMetricsForDpi(11, dpi));
        if (originalDpi != dpi)
        {
            AssertReleased(oldSmall);
            AssertReleased(oldLarge);
        }
    }

    [Fact]
    public void RepeatedWindowRecreationDoesNotRetainPreviousIcons()
    {
        using var platform = new WindowsPlatform();
        for (int iteration = 0; iteration < 32; iteration++)
        {
            using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE icon lifetime test", 320, 180));
            nint small = Icon(window.NativeHandle.Value, 0);
            nint large = Icon(window.NativeHandle.Value, 1);
            Assert.NotEqual(0, small);
            Assert.NotEqual(0, large);
            window.Dispose();
            AssertReleased(small);
            AssertReleased(large);
            _ = platform.PumpEvents();
        }
    }

    private static nint Icon(nint window, nuint size) => WindowsNative.SendMessage(window, 0x007F, size, 0);

    private static void AssertReleased(nint icon)
    {
        bool valid = GetIconInfo(icon, out IconInfo info) != 0;
        FreeBitmaps(info);
        Assert.False(valid);
    }

    private static void AssertIconSize(nint icon, int expected)
    {
        Assert.NotEqual(0, GetIconInfo(icon, out IconInfo info));
        try
        {
            Assert.NotEqual(0, GetObject(info.ColorBitmap, Marshal.SizeOf<Bitmap>(), out Bitmap bitmap));
            Assert.Equal(expected, bitmap.Width);
            Assert.Equal(expected, bitmap.Height);
        }
        finally { FreeBitmaps(info); }
    }

    private static void FreeBitmaps(IconInfo info)
    {
        if (info.MaskBitmap != 0) _ = DeleteObject(info.MaskBitmap);
        if (info.ColorBitmap != 0) _ = DeleteObject(info.ColorBitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public nint MaskBitmap;
        public nint ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPerPixel;
        public nint Bits;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetIconInfo(nint icon, out IconInfo info);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(nint value, int length, out Bitmap bitmap);
    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(nint value);
}