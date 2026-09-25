using System.ComponentModel;
using System.Runtime.InteropServices;
using ForgeLine.Platform;

namespace ForgeLine.Platform.Windows;

internal sealed class WindowsWindow : IWindow
{
    private const string WindowClassName = "ForgeLine.Client.Window";

    private static readonly object RegistrationLock = new();
    private static readonly WindowsNative.WindowProcedure WindowProcedure = StaticWindowProcedure;

    private static nint s_instanceHandle;
    private static ushort s_windowClassAtom;

    private readonly WindowConfiguration _configuration;
    private readonly Queue<WindowEvent> _events = new(16);
    private readonly Queue<PlatformInputEvent> _inputEvents = new(64);
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly uint _windowedStyle;

    private GCHandle _selfHandle;
    private WindowsNative.NativeRect _windowedRect;
    private nint _handle;
    private WindowSize _clientSize;
    private uint _dpi = 96;
    private bool _disposed;
    private bool _hasWindowedRect;
    private bool _isFocused;
    private bool _isMinimized;
    private bool _isOpen;
    private WindowMode _mode = WindowMode.Windowed;

    internal WindowsWindow(WindowConfiguration configuration)
    {
        _configuration = configuration;
        _windowedStyle = GetWindowStyle(configuration.Resizable);

        EnsureWindowClassRegistered();

        uint initialDpi = WindowsNative.GetDpiForSystem();
        if (initialDpi == 0)
        {
            initialDpi = 96;
        }

        var outerRect = new WindowsNative.NativeRect
        {
            Left = 0,
            Top = 0,
            Right = configuration.Width,
            Bottom = configuration.Height
        };

        if (WindowsNative.AdjustWindowRectExForDpi(
                ref outerRect,
                _windowedStyle,
                0,
                0,
                initialDpi) == 0)
        {
            throw CreateLastErrorException("Unable to calculate the initial Windows client area.");
        }

        _selfHandle = GCHandle.Alloc(this);

        try
        {
            _handle = WindowsNative.CreateWindowEx(
                0,
                WindowClassName,
                configuration.Title,
                _windowedStyle,
                WindowsNative.CurrentWindowPosition,
                WindowsNative.CurrentWindowPosition,
                outerRect.Width,
                outerRect.Height,
                0,
                0,
                s_instanceHandle,
                GCHandle.ToIntPtr(_selfHandle));

            if (_handle == 0)
            {
                throw CreateLastErrorException("Unable to create the ForgeLine client window.");
            }

            _isOpen = true;

            uint windowDpi = WindowsNative.GetDpiForWindow(_handle);
            _dpi = windowDpi == 0 ? initialDpi : windowDpi;
            RefreshClientSizeOrThrow();

            _ = WindowsNative.ShowWindow(_handle, WindowsNative.SwShow);
            _ = WindowsNative.UpdateWindow(_handle);

            if (configuration.Mode != WindowMode.Windowed)
            {
                SetMode(configuration.Mode);
            }

            EnqueueEvent(WindowEventKind.Created);
        }
        catch
        {
            if (_handle != 0)
            {
                _ = WindowsNative.DestroyWindow(_handle);
                _handle = 0;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            throw;
        }
    }

    public NativeWindowHandle NativeHandle => new(_handle);

    public WindowSize ClientSize => _clientSize;

    public uint Dpi => _dpi;

    public bool IsFocused => _isFocused;

    public bool IsMinimized => _isMinimized;

    public bool IsOpen => _isOpen;

    public WindowMode Mode => _mode;

    public void RequestClose()
    {
        ThrowIfUnavailable();

        if (_handle == 0)
        {
            return;
        }

        EnqueueEvent(WindowEventKind.CloseRequested);

        if (WindowsNative.DestroyWindow(_handle) == 0)
        {
            throw CreateLastErrorException("Unable to close the ForgeLine client window.");
        }
    }

    public void SetMode(WindowMode mode)
    {
        ThrowIfUnavailable();

        if (_mode == mode)
        {
            return;
        }

        switch (mode)
        {
            case WindowMode.Windowed:
                RestoreWindowedMode();
                break;

            case WindowMode.BorderlessFullscreen:
                EnterBorderlessFullscreen();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported window mode.");
        }

        _mode = mode;
        RefreshClientSizeOrThrow();
        EnqueueEvent(WindowEventKind.ModeChanged);
    }

    public bool TryDequeueEvent(out WindowEvent windowEvent)
    {
        EnsureOwnerThread();

        if (_events.Count == 0)
        {
            windowEvent = default;
            return false;
        }

        windowEvent = _events.Dequeue();
        return true;
    }

    public bool TryDequeueInputEvent(out PlatformInputEvent inputEvent)
    {
        EnsureOwnerThread();

        if (_inputEvents.Count == 0)
        {
            inputEvent = default;
            return false;
        }

        inputEvent = _inputEvents.Dequeue();
        return true;
    }

    public void Dispose()
    {
        EnsureOwnerThread();

        if (_disposed)
        {
            return;
        }

        if (_handle != 0)
        {
            _ = WindowsNative.DestroyWindow(_handle);
            _handle = 0;
        }

        _isOpen = false;
        _inputEvents.Clear();

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _disposed = true;
    }

    private static void EnsureWindowClassRegistered()
    {
        lock (RegistrationLock)
        {
            if (s_windowClassAtom != 0)
            {
                return;
            }

            s_instanceHandle = WindowsNative.GetModuleHandle(null);
            if (s_instanceHandle == 0)
            {
                throw CreateLastErrorException("Unable to resolve the current process module handle.");
            }

            nint className = Marshal.StringToCoTaskMemUni(WindowClassName);

            try
            {
                var windowClass = new WindowsNative.WindowClassEx
                {
                    Size = (uint)Marshal.SizeOf<WindowsNative.WindowClassEx>(),
                    Style = WindowsNative.CsHorizontalRedraw | WindowsNative.CsVerticalRedraw,
                    WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
                    Instance = s_instanceHandle,
                    Cursor = WindowsNative.LoadCursor(0, new nint(WindowsNative.CursorArrow)),
                    BackgroundBrush = new nint(WindowsNative.ColorWindow + 1),
                    ClassName = className
                };

                if (windowClass.Cursor == 0)
                {
                    throw CreateLastErrorException("Unable to load the default Windows cursor.");
                }

                s_windowClassAtom = WindowsNative.RegisterClassEx(ref windowClass);
                if (s_windowClassAtom == 0)
                {
                    throw CreateLastErrorException("Unable to register the ForgeLine Windows window class.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(className);
            }
        }
    }

    private static nint StaticWindowProcedure(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        try
        {
            WindowsWindow? window = null;

            if (message == WindowsNative.WmNcCreate && lParam != 0)
            {
                var createStruct = Marshal.PtrToStructure<WindowsNative.CreateStruct>(lParam);
                if (createStruct.CreateParameters != 0)
                {
                    var selfHandle = GCHandle.FromIntPtr(createStruct.CreateParameters);
                    window = selfHandle.Target as WindowsWindow;

                    if (window is not null)
                    {
                        window._handle = windowHandle;
                        _ = WindowsNative.SetWindowLongPtr(
                            windowHandle,
                            WindowsNative.GwlpUserData,
                            createStruct.CreateParameters);
                    }
                }
            }
            else
            {
                nint userData = WindowsNative.GetWindowLongPtr(windowHandle, WindowsNative.GwlpUserData);
                if (userData != 0)
                {
                    var selfHandle = GCHandle.FromIntPtr(userData);
                    window = selfHandle.Target as WindowsWindow;
                }
            }

            return window is null
                ? WindowsNative.DefWindowProc(windowHandle, message, wParam, lParam)
                : window.ProcessMessage(windowHandle, message, wParam, lParam);
        }
        catch
        {
            return WindowsNative.DefWindowProc(windowHandle, message, wParam, lParam);
        }
    }

    private nint ProcessMessage(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowsNative.WmClose:
                EnqueueEvent(WindowEventKind.CloseRequested);
                _ = WindowsNative.DestroyWindow(windowHandle);
                return 0;

            case WindowsNative.WmDestroy:
                _isOpen = false;
                EnqueueEvent(WindowEventKind.Closed);
                WindowsNative.PostQuitMessage(0);
                return 0;

            case WindowsNative.WmSize:
                ProcessSizeMessage(wParam, lParam);
                return 0;

            case WindowsNative.WmSetFocus:
                _isFocused = true;
                EnqueueEvent(WindowEventKind.FocusGained);
                return 0;

            case WindowsNative.WmKillFocus:
                _isFocused = false;
                _inputEvents.Enqueue(PlatformInputEvent.FocusLost());
                EnqueueEvent(WindowEventKind.FocusLost);
                return 0;

            case WindowsNative.WmKeyDown:
                if (TryMapKey(wParam, out PlatformKey keyDown))
                {
                    _inputEvents.Enqueue(
                        PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, keyDown));
                    return 0;
                }

                break;

            case WindowsNative.WmKeyUp:
                if (TryMapKey(wParam, out PlatformKey keyUp))
                {
                    _inputEvents.Enqueue(
                        PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, keyUp));
                    return 0;
                }

                break;

            case WindowsNative.WmMouseMove:
                (int moveX, int moveY) = DecodePointerPosition(lParam);
                _inputEvents.Enqueue(PlatformInputEvent.PointerMoved(moveX, moveY));
                return 0;

            case WindowsNative.WmMouseWheel:
                EnqueueMouseWheel(wParam, lParam);
                return 0;

            case WindowsNative.WmLButtonDown:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, lParam);
                return 0;

            case WindowsNative.WmLButtonUp:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, lParam);
                return 0;

            case WindowsNative.WmRButtonDown:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Right, lParam);
                return 0;

            case WindowsNative.WmRButtonUp:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Right, lParam);
                return 0;

            case WindowsNative.WmMButtonDown:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Middle, lParam);
                return 0;

            case WindowsNative.WmMButtonUp:
                EnqueueMouseButton(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Middle, lParam);
                return 0;

            case WindowsNative.WmXButtonDown:
                EnqueueMouseButton(
                    PlatformInputEventKind.MouseButtonDown,
                    DecodeXButton(wParam),
                    lParam);
                return 0;

            case WindowsNative.WmXButtonUp:
                EnqueueMouseButton(
                    PlatformInputEventKind.MouseButtonUp,
                    DecodeXButton(wParam),
                    lParam);
                return 0;

            case WindowsNative.WmDpiChanged:
                ProcessDpiChanged(wParam, lParam);
                return 0;

            case WindowsNative.WmNcDestroy:
                nint result = WindowsNative.DefWindowProc(windowHandle, message, wParam, lParam);
                _ = WindowsNative.SetWindowLongPtr(windowHandle, WindowsNative.GwlpUserData, 0);
                _handle = 0;
                return result;
        }

        return WindowsNative.DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void EnqueueMouseButton(
        PlatformInputEventKind kind,
        PlatformMouseButton button,
        nint lParam)
    {
        (int x, int y) = DecodePointerPosition(lParam);
        _inputEvents.Enqueue(PlatformInputEvent.MouseButtonChanged(kind, button, x, y));
    }

    private void EnqueueMouseWheel(nuint wParam, nint lParam)
    {
        (int x, int y) = DecodePointerPosition(lParam);
        var point = new WindowsNative.NativePoint { X = x, Y = y };

        if (WindowsNative.ScreenToClient(_handle, ref point) != 0)
        {
            x = point.X;
            y = point.Y;
        }

        int delta = unchecked((short)(((ulong)wParam >> 16) & 0xFFFF));
        _inputEvents.Enqueue(PlatformInputEvent.MouseWheel(x, y, delta));
    }

    private static PlatformMouseButton DecodeXButton(nuint wParam)
    {
        int button = (int)(((ulong)wParam >> 16) & 0xFFFF);
        return button == 1 ? PlatformMouseButton.X1 : PlatformMouseButton.X2;
    }

    private static (int X, int Y) DecodePointerPosition(nint lParam)
    {
        long raw = lParam.ToInt64();
        int x = unchecked((short)(raw & 0xFFFF));
        int y = unchecked((short)((raw >> 16) & 0xFFFF));
        return (x, y);
    }

    private static bool TryMapKey(nuint virtualKey, out PlatformKey key)
    {
        key = (int)virtualKey switch
        {
            WindowsNative.VkW => PlatformKey.W,
            WindowsNative.VkA => PlatformKey.A,
            WindowsNative.VkS => PlatformKey.S,
            WindowsNative.VkD => PlatformKey.D,
            WindowsNative.VkQ => PlatformKey.Q,
            WindowsNative.VkE => PlatformKey.E,
            WindowsNative.VkR => PlatformKey.R,
            WindowsNative.VkF => PlatformKey.F,
            WindowsNative.VkF1 => PlatformKey.F1,
            WindowsNative.VkF2 => PlatformKey.F2,
            WindowsNative.VkF3 => PlatformKey.F3,
            WindowsNative.VkUp => PlatformKey.Up,
            WindowsNative.VkDown => PlatformKey.Down,
            WindowsNative.VkLeft => PlatformKey.Left,
            WindowsNative.VkRight => PlatformKey.Right,
            WindowsNative.VkLShift => PlatformKey.LeftShift,
            WindowsNative.VkRShift => PlatformKey.RightShift,
            WindowsNative.VkEscape => PlatformKey.Escape,
            WindowsNative.VkSpace => PlatformKey.Space,
            _ => PlatformKey.Unknown
        };

        return key != PlatformKey.Unknown;
    }

    private void ProcessSizeMessage(nuint wParam, nint lParam)
    {
        int width = unchecked((ushort)(lParam.ToInt64() & 0xFFFF));
        int height = unchecked((ushort)((lParam.ToInt64() >> 16) & 0xFFFF));
        _clientSize = new WindowSize(width, height);

        bool wasMinimized = _isMinimized;
        _isMinimized = wParam == WindowsNative.SizeMinimized;

        if (_isMinimized)
        {
            if (!wasMinimized)
            {
                EnqueueEvent(WindowEventKind.Minimized);
            }

            return;
        }

        if (wasMinimized)
        {
            EnqueueEvent(WindowEventKind.Restored);
        }

        EnqueueEvent(WindowEventKind.Resized);
    }

    private void ProcessDpiChanged(nuint wParam, nint lParam)
    {
        uint newDpi = (uint)(wParam & 0xFFFF);
        if (newDpi != 0)
        {
            _dpi = newDpi;
        }

        if (lParam != 0)
        {
            var suggestedRect = Marshal.PtrToStructure<WindowsNative.NativeRect>(lParam);
            _ = WindowsNative.SetWindowPos(
                _handle,
                0,
                suggestedRect.Left,
                suggestedRect.Top,
                suggestedRect.Width,
                suggestedRect.Height,
                WindowsNative.SwpNoActivate | WindowsNative.SwpNoZOrder);
        }

        TryRefreshClientSize();
        EnqueueEvent(WindowEventKind.DpiChanged);
    }

    private void EnterBorderlessFullscreen()
    {
        if (WindowsNative.GetWindowRect(_handle, out _windowedRect) == 0)
        {
            throw CreateLastErrorException("Unable to capture the windowed placement.");
        }

        _hasWindowedRect = true;

        nint monitor = WindowsNative.MonitorFromWindow(_handle, WindowsNative.MonitorDefaultToNearest);
        if (monitor == 0)
        {
            throw CreateLastErrorException("Unable to resolve the target monitor.");
        }

        var monitorInfo = new WindowsNative.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<WindowsNative.MonitorInfo>()
        };

        if (WindowsNative.GetMonitorInfo(monitor, ref monitorInfo) == 0)
        {
            throw CreateLastErrorException("Unable to read the target monitor bounds.");
        }

        SetWindowStyle(WindowsNative.WsPopup);

        if (WindowsNative.SetWindowPos(
                _handle,
                0,
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Width,
                monitorInfo.Monitor.Height,
                WindowsNative.SwpFrameChanged | WindowsNative.SwpNoActivate | WindowsNative.SwpNoZOrder) == 0)
        {
            throw CreateLastErrorException("Unable to enter borderless fullscreen mode.");
        }
    }

    private void RestoreWindowedMode()
    {
        SetWindowStyle(_windowedStyle);

        if (!_hasWindowedRect)
        {
            return;
        }

        if (WindowsNative.SetWindowPos(
                _handle,
                0,
                _windowedRect.Left,
                _windowedRect.Top,
                _windowedRect.Width,
                _windowedRect.Height,
                WindowsNative.SwpFrameChanged | WindowsNative.SwpNoActivate | WindowsNative.SwpNoZOrder) == 0)
        {
            throw CreateLastErrorException("Unable to restore windowed mode.");
        }
    }

    private void SetWindowStyle(uint style)
    {
        Marshal.SetLastPInvokeError(0);
        nint previousStyle = WindowsNative.SetWindowLongPtr(
            _handle,
            WindowsNative.GwlStyle,
            new nint(unchecked((int)style)));

        if (previousStyle == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error != 0)
            {
                throw new Win32Exception(error, "Unable to update the native window style.");
            }
        }
    }

    private void RefreshClientSizeOrThrow()
    {
        if (WindowsNative.GetClientRect(_handle, out var rect) == 0)
        {
            throw CreateLastErrorException("Unable to query the Windows client area.");
        }

        _clientSize = new WindowSize(rect.Width, rect.Height);
    }

    private void TryRefreshClientSize()
    {
        if (_handle != 0 && WindowsNative.GetClientRect(_handle, out var rect) != 0)
        {
            _clientSize = new WindowSize(rect.Width, rect.Height);
        }
    }

    private void EnqueueEvent(WindowEventKind kind)
    {
        _events.Enqueue(new WindowEvent(
            kind,
            _clientSize,
            _dpi,
            _isFocused,
            _isMinimized,
            _mode));
    }

    private static uint GetWindowStyle(bool resizable)
    {
        uint style = WindowsNative.WsCaption |
                     WindowsNative.WsSysMenu |
                     WindowsNative.WsMinimizeBox;

        if (resizable)
        {
            style |= WindowsNative.WsThickFrame | WindowsNative.WsMaximizeBox;
        }

        return style;
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

        if (!_isOpen || _handle == 0)
        {
            throw new InvalidOperationException("The Windows window is no longer open.");
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Windows window operations must run on the creating thread.");
        }
    }
}
