using System.Runtime.InteropServices;

namespace ForgeLine.Platform.Windows;

/// <summary>Bounded Unicode text transfer on the window owner. Clipboard contention is a normal failure.</summary>
public static partial class WindowsClipboard
{
    private const uint UnicodeText = 13;
    public static bool TryWrite(nint owner, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 128 || !OpenClipboard(owner)) return false;
        nint memory = 0;
        try
        {
            memory = GlobalAlloc(2, (nuint)((text.Length + 1) * 2));
            if (memory == 0) return false;
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return false;
            try
            {
                for (int i = 0; i < text.Length; i++) Marshal.WriteInt16(pointer, i * 2, unchecked((short)text[i]));
                Marshal.WriteInt16(pointer, text.Length * 2, 0);
            }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(UnicodeText, memory) == 0) return false;
            memory = 0; // Ownership transferred to the clipboard.
            return true;
        }
        finally { if (memory != 0) GlobalFree(memory); CloseClipboard(); }
    }
    public static bool TryRead(nint owner, out string text)
    {
        text = string.Empty;
        if (!OpenClipboard(owner)) return false;
        try
        {
            nint memory = GetClipboardData(UnicodeText);
            if (memory == 0) return false;
            nuint bytes = GlobalSize(memory);
            if (bytes < 2) return false;
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return false;
            try
            {
                int limit = (int)(bytes / 2 > 129 ? 129 : bytes / 2);
                for (int i = 0; i < limit; i++)
                    if (Marshal.ReadInt16(pointer, i * 2) == 0)
                    { text = Marshal.PtrToStringUni(pointer, i)!; return true; }
                return false;
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] private static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("user32.dll")] private static partial nint GetClipboardData(uint format);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalFree(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nuint GlobalSize(nint memory);
}
