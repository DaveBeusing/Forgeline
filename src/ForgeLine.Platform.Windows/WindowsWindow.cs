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
    private readonly WindowLifecycleState _lifecycle = new();
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly uint _windowedStyle;

    private GCHandle _selfHandle;
    private WindowsNative.NativeRect _currentMonitorBounds;
    private WindowsNative.NativeRect _windowedRect;
    private nint _currentMonitor;
    private nint _handle;
    private uint _dpi = 96;
    private bool _disposed;
    private bool _hasWindowedRect;
    private bool _isFocused;
    private bool _isOpen;

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
            _lifecycle.InitializeClientSize(
                ReadClientSizeOrThrow());
            RefreshCurrentMonitor();

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

    public WindowSize ClientSize => _lifecycle.ValidClientSize;

    public uint Dpi => _dpi;

    public bool IsFocused => _isFocused;

    public bool IsMinimized => _lifecycle.IsMinimized;

    public bool IsOpen => _isOpen;

    public WindowMode Mode => _lifecycle.CurrentMode;

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

        WindowMode previousMode =
            _lifecycle.CurrentMode;

        if (!_lifecycle.BeginModeTransition(mode))
        {
            return;
        }

        ulong previousResizeGeneration =
            _lifecycle.ResizeGeneration;

        try
        {
            switch (mode)
            {
                case WindowMode.Windowed:
                    RestoreWindowedMode();
                    break;

                case WindowMode.BorderlessFullscreen:
                    EnterBorderlessFullscreen();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mode),
                        mode,
                        "Unsupported window mode.");
            }

            WindowSize finalClientSize =
                ReadClientSizeOrThrow();
            _lifecycle.CompleteModeTransition(
                finalClientSize);
            RefreshCurrentMonitor();

            EnqueueEvent(
                WindowEventKind.Resized);
            EnqueueEvent(
                WindowEventKind.ModeChanged);

            WriteModeTransitionDiagnostic(
                previousMode,
                previousResizeGeneration);
        }
        catch
        {
            _lifecycle.AbortModeTransition();
            throw;
        }
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
                var tracking = new WindowsNative.MouseTracking
                {
                    Size = WindowsNative.MouseTrackingSize,
                    Flags = WindowsNative.TmeLeave,
                    Window = _handle
                };
                if (WindowsNative.TrackMouseEvent(ref tracking) == 0)
                {
                    _inputEvents.Enqueue(PlatformInputEvent.PointerLeft());
                    return 0;
                }
                _inputEvents.Enqueue(PlatformInputEvent.PointerMoved(moveX, moveY));
                return 0;

            case WindowsNative.WmMouseLeave:
                _inputEvents.Enqueue(PlatformInputEvent.PointerLeft());
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
            WindowsNative.Vk0 => PlatformKey.D0,
            WindowsNative.Vk1 => PlatformKey.D1,
            WindowsNative.Vk2 => PlatformKey.D2,
            WindowsNative.Vk3 => PlatformKey.D3,
            WindowsNative.Vk4 => PlatformKey.D4,
            WindowsNative.Vk5 => PlatformKey.D5,
            WindowsNative.Vk6 => PlatformKey.D6,
            WindowsNative.Vk7 => PlatformKey.D7,
            WindowsNative.Vk8 => PlatformKey.D8,
            WindowsNative.Vk9 => PlatformKey.D9,
            WindowsNative.VkW => PlatformKey.W,
            WindowsNative.VkA => PlatformKey.A,
            WindowsNative.VkB => PlatformKey.B,
            0x47 => PlatformKey.G, 0x49 => PlatformKey.I, 0x4A => PlatformKey.J,
            0x4E => PlatformKey.N, 0x4F => PlatformKey.O, 0x56 => PlatformKey.V,
            0x58 => PlatformKey.X, 0x5A => PlatformKey.Z,
            WindowsNative.VkC => PlatformKey.C,
            WindowsNative.VkS => PlatformKey.S,
            WindowsNative.VkD => PlatformKey.D,
            WindowsNative.VkQ => PlatformKey.Q,
            WindowsNative.VkE => PlatformKey.E,
            WindowsNative.VkR => PlatformKey.R,
            WindowsNative.VkF => PlatformKey.F,
            WindowsNative.VkH => PlatformKey.H,
            WindowsNative.VkK => PlatformKey.K,
            WindowsNative.VkL => PlatformKey.L,
            WindowsNative.VkM => PlatformKey.M,
            WindowsNative.VkP => PlatformKey.P,
            WindowsNative.VkT => PlatformKey.T,
            WindowsNative.VkU => PlatformKey.U,
            WindowsNative.VkY => PlatformKey.Y,
            WindowsNative.VkF1 => PlatformKey.F1,
            WindowsNative.VkF2 => PlatformKey.F2,
            WindowsNative.VkF3 => PlatformKey.F3,
            WindowsNative.VkF4 => PlatformKey.F4,
            WindowsNative.VkF5 => PlatformKey.F5,
            WindowsNative.VkF6 => PlatformKey.F6,
            WindowsNative.VkF7 => PlatformKey.F7,
            WindowsNative.VkF8 => PlatformKey.F8,
            WindowsNative.VkF9 => PlatformKey.F9,
            WindowsNative.VkF10 => PlatformKey.F10,
            WindowsNative.VkF11 => PlatformKey.F11,
            WindowsNative.VkF12 => PlatformKey.F12,
            WindowsNative.VkUp => PlatformKey.Up,
            WindowsNative.VkDown => PlatformKey.Down,
            WindowsNative.VkLeft => PlatformKey.Left,
            WindowsNative.VkRight => PlatformKey.Right,
            WindowsNative.VkLShift => PlatformKey.LeftShift,
            WindowsNative.VkRShift => PlatformKey.RightShift,
            WindowsNative.VkLControl => PlatformKey.LeftControl,
            WindowsNative.VkRControl => PlatformKey.RightControl,
            WindowsNative.VkEscape => PlatformKey.Escape,
            WindowsNative.VkEnter => PlatformKey.Enter,
            WindowsNative.VkTab => PlatformKey.Tab,
            WindowsNative.VkSpace => PlatformKey.Space,
            WindowsNative.VkHome => PlatformKey.Home,
            _ => PlatformKey.Unknown
        };

        return key != PlatformKey.Unknown;
    }

    private void ProcessSizeMessage(nuint wParam, nint lParam)
    {
        int width =
            unchecked(
                (ushort)(
                    lParam.ToInt64() &
                    0xFFFF));
        int height =
            unchecked(
                (ushort)(
                    (lParam.ToInt64() >> 16) &
                    0xFFFF));

        WindowSizeMessageTransition transition =
            _lifecycle.ApplySizeMessage(
                width,
                height,
                wParam ==
                    WindowsNative.SizeMinimized);

        if (transition.BecameMinimized)
        {
            EnqueueEvent(
                WindowEventKind.Minimized);
        }

        if (!transition.ValidClientSizeObserved)
        {
            return;
        }

        if (transition.Restored)
        {
            EnqueueEvent(
                WindowEventKind.Restored);
        }

        if (!_lifecycle.TransitionInProgress &&
            (transition.ClientSizeChanged ||
             transition.Restored))
        {
            EnqueueEvent(
                WindowEventKind.Resized);
        }
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
        RefreshCurrentMonitor();
        EnqueueEvent(WindowEventKind.DpiChanged);
    }

    private void EnterBorderlessFullscreen()
    {
        if (WindowsNative.GetWindowRect(
                _handle,
                out _windowedRect) == 0)
        {
            throw CreateLastErrorException(
                "Unable to capture the windowed placement.");
        }

        _hasWindowedRect = true;

        WindowsNative.MonitorInfo monitorInfo =
            GetCurrentMonitorInfo();

        SetWindowStyle(
            WindowsNative.WsPopup |
            WindowsNative.WsVisible);

        if (WindowsNative.SetWindowPos(
                _handle,
                0,
                monitorInfo.Monitor.Left,
                monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Width,
                monitorInfo.Monitor.Height,
                WindowsNative.SwpFrameChanged |
                WindowsNative.SwpNoActivate |
                WindowsNative.SwpNoZOrder |
                WindowsNative.SwpShowWindow) == 0)
        {
            throw CreateLastErrorException(
                "Unable to enter borderless fullscreen mode.");
        }
    }

    private void RestoreWindowedMode()
    {
        SetWindowStyle(
            _windowedStyle |
            WindowsNative.WsVisible);

        WindowsNative.NativeRect restoreRect =
            ResolveWindowedRestoreRect();

        if (WindowsNative.SetWindowPos(
                _handle,
                0,
                restoreRect.Left,
                restoreRect.Top,
                restoreRect.Width,
                restoreRect.Height,
                WindowsNative.SwpFrameChanged |
                WindowsNative.SwpNoActivate |
                WindowsNative.SwpNoZOrder |
                WindowsNative.SwpShowWindow) == 0)
        {
            throw CreateLastErrorException(
                "Unable to restore windowed mode.");
        }

        _windowedRect = restoreRect;
        _hasWindowedRect = true;
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

    private WindowSize ReadClientSizeOrThrow()
    {
        if (WindowsNative.GetClientRect(
                _handle,
                out WindowsNative.NativeRect rect) == 0)
        {
            throw CreateLastErrorException(
                "Unable to query the Windows client area.");
        }

        var clientSize =
            new WindowSize(
                rect.Width,
                rect.Height);

        if (clientSize.IsEmpty)
        {
            throw new InvalidOperationException(
                "The Windows client area resolved to an invalid zero-sized surface.");
        }

        return clientSize;
    }

    private void TryRefreshClientSize()
    {
        if (_handle == 0 ||
            WindowsNative.GetClientRect(
                _handle,
                out WindowsNative.NativeRect rect) == 0 ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            return;
        }

        _lifecycle.SetValidClientSize(
            new WindowSize(
                rect.Width,
                rect.Height));
    }

    private WindowsNative.NativeRect ResolveWindowedRestoreRect()
    {
        WindowsNative.NativeRect candidate =
            _hasWindowedRect
                ? _windowedRect
                : CreateFallbackWindowedRect();

        nint monitor =
            WindowsNative.MonitorFromRect(
                ref candidate,
                WindowsNative.MonitorDefaultToNull);

        if (monitor == 0)
        {
            monitor =
                WindowsNative.MonitorFromWindow(
                    _handle,
                    WindowsNative.MonitorDefaultToNearest);
        }

        if (monitor == 0)
        {
            throw CreateLastErrorException(
                "Unable to resolve a monitor for the restored window.");
        }

        WindowsNative.MonitorInfo monitorInfo =
            ReadMonitorInfo(
                monitor);
        WindowsNative.NativeRect fallback =
            CreateFallbackWindowedRect();

        WindowBounds normalized =
            WindowPlacement.ConstrainToWorkArea(
                ToBounds(candidate),
                ToBounds(monitorInfo.Work),
                fallback.Width,
                fallback.Height);

        return ToNativeRect(
            normalized);
    }

    private WindowsNative.NativeRect CreateFallbackWindowedRect()
    {
        var rect =
            new WindowsNative.NativeRect
            {
                Left = 0,
                Top = 0,
                Right = _configuration.Width,
                Bottom = _configuration.Height
            };

        if (WindowsNative.AdjustWindowRectExForDpi(
                ref rect,
                _windowedStyle,
                0,
                0,
                _dpi) == 0)
        {
            throw CreateLastErrorException(
                "Unable to calculate fallback windowed bounds.");
        }

        return rect;
    }

    private WindowsNative.MonitorInfo GetCurrentMonitorInfo()
    {
        nint monitor =
            WindowsNative.MonitorFromWindow(
                _handle,
                WindowsNative.MonitorDefaultToNearest);

        if (monitor == 0)
        {
            throw CreateLastErrorException(
                "Unable to resolve the target monitor.");
        }

        WindowsNative.MonitorInfo monitorInfo =
            ReadMonitorInfo(
                monitor);
        _currentMonitor = monitor;
        _currentMonitorBounds =
            monitorInfo.Monitor;
        return monitorInfo;
    }

    private void RefreshCurrentMonitor()
    {
        _ = GetCurrentMonitorInfo();
    }

    private static WindowsNative.MonitorInfo ReadMonitorInfo(
        nint monitor)
    {
        var monitorInfo =
            new WindowsNative.MonitorInfo
            {
                Size =
                    (uint)Marshal.SizeOf<
                        WindowsNative.MonitorInfo>()
            };

        if (WindowsNative.GetMonitorInfo(
                monitor,
                ref monitorInfo) == 0)
        {
            throw CreateLastErrorException(
                "Unable to read the target monitor bounds.");
        }

        return monitorInfo;
    }

    private static WindowBounds ToBounds(
        in WindowsNative.NativeRect rect) =>
        new(
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height);

    private static WindowsNative.NativeRect ToNativeRect(
        in WindowBounds bounds) =>
        new()
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right =
                bounds.Left +
                bounds.Width,
            Bottom =
                bounds.Top +
                bounds.Height
        };

    private void WriteModeTransitionDiagnostic(
        WindowMode previousMode,
        ulong previousResizeGeneration)
    {
        Console.WriteLine(
            $"[platform:window-mode] previous={previousMode} " +
            $"requested={_lifecycle.RequestedMode} " +
            $"result={_lifecycle.CurrentMode} " +
            $"monitor=0x{_currentMonitor:X} " +
            $"monitorBounds={_currentMonitorBounds.Left},{_currentMonitorBounds.Top}," +
            $"{_currentMonitorBounds.Width}x{_currentMonitorBounds.Height} " +
            $"client={_lifecycle.ValidClientSize.Width}x{_lifecycle.ValidClientSize.Height} " +
            $"minimized={_lifecycle.IsMinimized} " +
            $"resizeGeneration={previousResizeGeneration}->{_lifecycle.ResizeGeneration} " +
            "resizeNotification=emitted");
    }

    private void EnqueueEvent(WindowEventKind kind)
    {
        _events.Enqueue(
            new WindowEvent(
                kind,
                _lifecycle.ValidClientSize,
                _dpi,
                _isFocused,
                _lifecycle.IsMinimized,
                _lifecycle.CurrentMode));
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
