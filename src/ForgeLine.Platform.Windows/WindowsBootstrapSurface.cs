using System.Runtime.InteropServices;

namespace ForgeLine.Platform.Windows;

// GDI is used only until a successful graphics presentation, on the HWND owner.
// Text and geometry live in the assembly and require no catalog, decoder or GPU.
internal static partial class WindowsBootstrapSurface
{
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PaintState
    {
        internal nint DeviceContext;
        internal int Erase;
        internal WindowsNative.NativeRect Area;
        internal int Restore, Incremental;
        internal fixed byte Reserved[32];
    }

    internal static bool Paint(nint window, uint dpi, string status, bool failed)
    {
        nint dc = BeginPaint(window, out PaintState paint);
        if (dc == 0) return false;
        try
        {
            if (WindowsNative.GetClientRect(window, out var bounds) == 0) return false;
            if (bounds.Width <= 0 || bounds.Height <= 0 ||
                FillRect(dc, ref bounds, WindowsNative.GetStockObject(WindowsNative.BlackBrush)) == 0) return false;
            _ = SetBkMode(dc, 1);
            int Scale(int value) => Math.Max(1, (int)Math.Round(value * dpi / 96d));
            int center = bounds.Height / 2;
            bool brandDrawn = Text(dc, "FORGELINE", new() { Left = 12, Top = center - Scale(60),
                Right = bounds.Right - 12, Bottom = center }, Scale(38), 700, 0x00E2E8EE);
            var accent = new WindowsNative.NativeRect { Left = bounds.Width / 2 - Scale(64),
                Top = center + Scale(4), Right = bounds.Width / 2 + Scale(64), Bottom = center + Scale(6) };
            nint brush = CreateSolidBrush(failed ? 0x004E6EDBu : 0x007DA8C8u);
            if (brush != 0)
            {
                try { _ = FillRect(dc, ref accent, brush); }
                finally { _ = DeleteObject(brush); }
            }
            bool statusDrawn = Text(dc, status, new() { Left = Scale(24), Top = center + Scale(30),
                Right = bounds.Right - Scale(24), Bottom = bounds.Bottom - Scale(12) },
                Scale(16), 400, failed ? 0x008CA8EEu : 0x00A2A9AEu);
            return brandDrawn && statusDrawn;
        }
        finally { _ = EndPaint(window, ref paint); }
    }

    private static bool Text(nint dc, string text, WindowsNative.NativeRect bounds,
        int height, int weight, uint color)
    {
        nint font = CreateFont(-height, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        nint previous = font == 0 ? 0 : SelectObject(dc, font);
        try
        {
            _ = SetTextColor(dc, color);
            return DrawText(dc, text, text.Length, ref bounds, 0x00000001 | 0x00000010 | 0x00000800) > 0;
        }
        finally
        {
            if (previous != 0) _ = SelectObject(dc, previous);
            if (font != 0) _ = DeleteObject(font);
        }
    }

    [LibraryImport("user32.dll")] private static partial nint BeginPaint(nint window, out PaintState paint);
    [LibraryImport("user32.dll")] private static partial int EndPaint(nint window, ref PaintState paint);
    [LibraryImport("user32.dll")] private static partial int FillRect(nint dc, ref WindowsNative.NativeRect rect, nint brush);
    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawText(nint dc, string text, int count, ref WindowsNative.NativeRect rect, uint format);
    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode")] private static partial int SetBkMode(nint dc, int mode);
    [LibraryImport("gdi32.dll")] private static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport("gdi32.dll")] private static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint value);
    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint family, string face);
}
