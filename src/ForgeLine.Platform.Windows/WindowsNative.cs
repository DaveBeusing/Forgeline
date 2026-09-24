using System.Runtime.InteropServices;

namespace ForgeLine.Platform.Windows;

internal static partial class WindowsNative
{
    internal const uint CsHorizontalRedraw = 0x0002;
    internal const uint CsVerticalRedraw = 0x0001;

    internal const int ColorWindow = 5;
    internal const int CursorArrow = 32512;
    internal const int CurrentWindowPosition = unchecked((int)0x80000000);

    internal const int GwlStyle = -16;
    internal const int GwlpUserData = -21;

    internal const uint MonitorDefaultToNearest = 0x00000002;

    internal const uint MwmoInputAvailable = 0x0004;
    internal const uint PmRemove = 0x0001;
    internal const uint QsAllInput = 0x04FF;

    internal const int SizeMinimized = 1;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int SwShow = 5;

    internal const uint WaitFailed = 0xFFFFFFFF;

    internal const uint WmClose = 0x0010;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmDpiChanged = 0x02E0;
    internal const uint WmKillFocus = 0x0008;
    internal const uint WmNcCreate = 0x0081;
    internal const uint WmNcDestroy = 0x0082;
    internal const uint WmQuit = 0x0012;
    internal const uint WmSetFocus = 0x0007;
    internal const uint WmSize = 0x0005;

    internal const uint WsCaption = 0x00C00000;
    internal const uint WsMaximizeBox = 0x00010000;
    internal const uint WsMinimizeBox = 0x00020000;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsSysMenu = 0x00080000;
    internal const uint WsThickFrame = 0x00040000;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint windowHandle, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        internal nint MenuName;
        internal nint ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        internal nint WindowHandle;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateStruct
    {
        internal nint CreateParameters;
        internal nint Instance;
        internal nint Menu;
        internal nint Parent;
        internal int Height;
        internal int Width;
        internal int Y;
        internal int X;
        internal int Style;
        internal nint Name;
        internal nint ClassName;
        internal uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect Work;
        internal uint Flags;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "AdjustWindowRectExForDpi", SetLastError = true)]
    internal static partial int AdjustWindowRectExForDpi(
        ref NativeRect rect,
        uint style,
        int hasMenu,
        uint extendedStyle,
        uint dpi);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint createParameters);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint windowHandle, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    internal static partial int DestroyWindow(nint windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(ref NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect", SetLastError = true)]
    internal static partial int GetClientRect(nint windowHandle, out NativeRect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")]
    internal static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    internal static partial int GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    internal static partial int GetWindowRect(nint windowHandle, out NativeRect rect);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    internal static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    internal static partial nint MonitorFromWindow(nint windowHandle, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjectsEx", SetLastError = true)]
    internal static partial uint MsgWaitForMultipleObjectsEx(
        uint count,
        nint handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    internal static partial int PeekMessage(
        out NativeMessage message,
        nint windowHandle,
        uint messageFilterMin,
        uint messageFilterMax,
        uint removeMessage);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(ref WindowClassEx windowClass);

    [LibraryImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext", SetLastError = true)]
    internal static partial int SetProcessDpiAwarenessContext(nint dpiContext);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    internal static partial int SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    internal static partial int ShowWindow(nint windowHandle, int command);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    internal static partial int TranslateMessage(ref NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow", SetLastError = true)]
    internal static partial int UpdateWindow(nint windowHandle);
}
