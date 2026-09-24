using System.ComponentModel;
using System.Runtime.InteropServices;
using ForgeLine.Core;

namespace ForgeLine.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    private const int ErrorAccessDenied = 5;

    private static readonly object DpiInitializationLock = new();

    private static bool s_dpiInitialized;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    private bool _disposed;
    private bool _quitRequested;

    public WindowsPlatform()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ForgeLine.Platform.Windows requires Windows.");
        }

        EnsurePerMonitorDpiAwareness();
        Clock = new WindowsHighResolutionClock();
    }

    public IClock Clock { get; }

    public IWindow CreateWindow(WindowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfUnavailable();

        return new WindowsWindow(configuration);
    }

    public bool PumpEvents()
    {
        ThrowIfUnavailable();

        while (WindowsNative.PeekMessage(
                   out var message,
                   0,
                   0,
                   0,
                   WindowsNative.PmRemove) != 0)
        {
            if (message.Message == WindowsNative.WmQuit)
            {
                _quitRequested = true;
                continue;
            }

            _ = WindowsNative.TranslateMessage(ref message);
            _ = WindowsNative.DispatchMessage(ref message);
        }

        return !_quitRequested;
    }

    public void WaitForEvents(TimeSpan maximumWait)
    {
        ThrowIfUnavailable();

        if (_quitRequested || maximumWait <= TimeSpan.Zero)
        {
            return;
        }

        double totalMilliseconds = Math.Ceiling(maximumWait.TotalMilliseconds);
        uint timeout = totalMilliseconds >= uint.MaxValue
            ? uint.MaxValue - 1
            : (uint)totalMilliseconds;

        uint result = WindowsNative.MsgWaitForMultipleObjectsEx(
            0,
            0,
            timeout,
            WindowsNative.QsAllInput,
            WindowsNative.MwmoInputAvailable);

        if (result == WindowsNative.WaitFailed)
        {
            throw CreateLastErrorException("Waiting for Windows messages failed.");
        }
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        _disposed = true;
    }

    private static void EnsurePerMonitorDpiAwareness()
    {
        lock (DpiInitializationLock)
        {
            if (s_dpiInitialized)
            {
                return;
            }

            nint perMonitorAwareV2 = new(-4);
            if (WindowsNative.SetProcessDpiAwarenessContext(perMonitorAwareV2) == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != ErrorAccessDenied)
                {
                    throw new Win32Exception(error, "Unable to enable per-monitor-v2 DPI awareness.");
                }
            }

            s_dpiInitialized = true;
        }
    }

    private static Win32Exception CreateLastErrorException(string message)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, message);
    }

    private void ThrowIfUnavailable()
    {
        EnsureOwnerThread();

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsPlatform));
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Windows platform operations must run on the creating thread.");
        }
    }
}
