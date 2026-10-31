using System.ComponentModel;
using System.Runtime.InteropServices;
using ForgeLine.Core;
using ForgeLine.Platform;

namespace ForgeLine.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    private const int ErrorAccessDenied = 5;

    private static readonly object DpiInitializationLock = new();

    private static bool s_dpiInitialized;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    private bool _disposed;
    private bool _quitRequested;
    private PlatformWorkloadProbe? _workload;

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

    public IWindow CreateBootstrapWindow(WindowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfUnavailable();
        PrepareForWindowCreation();
        return new WindowsWindow(configuration, bootstrap: true);
    }

    public bool BootstrapPainted(IWindow window) => BootstrapWindow(window).BootstrapPainted;
    public void UpdateBootstrap(IWindow window, string status, bool failed = false) =>
        BootstrapWindow(window).UpdateBootstrap(status, failed);
    public void CompleteBootstrap(IWindow window) => BootstrapWindow(window).CompleteBootstrap();
    public void ConfigureBootstrap(IWindow window, WindowConfiguration configuration) =>
        BootstrapWindow(window).ConfigureBootstrap(configuration);

    private WindowsWindow BootstrapWindow(IWindow window)
    {
        ThrowIfUnavailable();
        return window as WindowsWindow ?? throw new ArgumentException("A Windows window is required.", nameof(window));
    }

    public IWindow CreateWindow(WindowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfUnavailable();
        PrepareForWindowCreation();

        return new WindowsWindow(configuration);
    }

    public void EnableWorkloadObservations(IWindow window)
    {
        ThrowIfUnavailable();
        if(window is not WindowsWindow native)throw new ArgumentException("Windows workload observations require the creating-owner window.",nameof(window));
        _workload ??= new PlatformWorkloadProbe();
        native.AttachWorkloadObservations(_workload);
    }
    // This optional copied-value provider may be read by another owner. It never
    // exposes a mutable window/queue or changes the core IPlatform contract.
    public PlatformWorkloadTelemetry CaptureWorkloadTelemetry() => _workload?.Capture() ?? default;

    public bool PumpEvents()
    {
        ThrowIfUnavailable();
        _workload?.BeginPump();bool success=false;
        try
        {
        while (WindowsNative.PeekMessage(
                   out var message,
                   0,
                   0,
                   0,
                   WindowsNative.PmRemove) != 0)
        {
            _workload?.Removed(message.Message==WindowsNative.WmQuit);
            if (message.Message == WindowsNative.WmQuit)
            {
                _quitRequested = true;
                continue;
            }

            DispatchObservedMessage(ref message);
        }

        success=true;
        return !_quitRequested;
        }
        finally { _workload?.EndPump(success); }
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

    private void PrepareForWindowCreation()
    {
        _workload?.BeginPump(creation:true);bool success=false;
        try
        {
        while (WindowsNative.PeekMessage(
                   out var message,
                   0,
                   0,
                   0,
                   WindowsNative.PmRemove) != 0)
        {
            _workload?.Removed(message.Message==WindowsNative.WmQuit);
            // WM_QUIT is generated only after higher-priority messages are drained.
            // A quit-only filter can leave a previous window's quit pending.
            if (message.Message != WindowsNative.WmQuit)
            {
                DispatchObservedMessage(ref message);
            }
        }

        _quitRequested = false;success=true;
        }
        finally { _workload?.EndPump(success); }
    }

    private void DispatchObservedMessage(ref WindowsNative.NativeMessage message)
    {
        _workload?.BeginDispatch();bool success=false;
        try { _=WindowsNative.TranslateMessage(ref message);_=WindowsNative.DispatchMessage(ref message);success=true; }
        finally { _workload?.EndDispatch(success); }
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

        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Windows platform operations must run on the creating thread.");
        }
    }
}
