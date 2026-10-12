using System.Buffers.Binary;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace ForgeLine.Platform.Windows;

// Each window owns its handles; the immutable source bytes are shared and never require disk I/O.
internal sealed class WindowsWindowIcons : IDisposable
{
    private static readonly Lazy<byte[]> Source = new(() =>
    {
        using Stream stream = typeof(WindowsWindowIcons).Assembly.GetManifestResourceStream("ForgeLine.ApplicationIcon")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    });

    private readonly IconHandle _small;
    private readonly IconHandle _large;
    internal nint Small => _small.DangerousGetHandle();
    internal nint Large => _large.DangerousGetHandle();
    internal uint Dpi { get; }

    internal WindowsWindowIcons(uint dpi)
    {
        Dpi = dpi;
        _small = Create(WindowsNative.GetSystemMetricsForDpi(49, dpi), WindowsNative.GetSystemMetricsForDpi(50, dpi));
        try { _large = Create(WindowsNative.GetSystemMetricsForDpi(11, dpi), WindowsNative.GetSystemMetricsForDpi(12, dpi)); }
        catch { _small.Dispose(); throw; }
    }

    internal void Assign(nint window)
    {
        _ = WindowsNative.SendMessage(window, WindowsNative.WmSetIcon, 0, Small);
        _ = WindowsNative.SendMessage(window, WindowsNative.WmSetIcon, 1, Large);
    }

    private static IconHandle Create(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        byte[] data = Source.Value;
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != 1)
            throw new InvalidDataException("Invalid embedded application icon header.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        if (count == 0 || data.Length < 6 + count * 16)
            throw new InvalidDataException("Invalid embedded application icon directory.");

        // Prefer the smallest source at least as large as the requested size, then the largest available.
        int selected = -1;
        int best = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int size = data[entry] == 0 ? 256 : data[entry];
            int score = size >= Math.Max(width, height) ? size : 1000 - size;
            if (score < best) { selected = entry; best = score; }
        }
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(selected + 8));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(selected + 12));
        if (length == 0 || (ulong)offset + length > (ulong)data.Length)
            throw new InvalidDataException("The embedded application icon payload is truncated.");
        byte[] payload = data.AsSpan(checked((int)offset), checked((int)length)).ToArray();
        nint handle = WindowsNative.CreateIconFromResourceEx(payload, length, 1, 0x00030000, width, height, 0);
        if (handle == 0) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(),
            "Unable to create the application window icon.");
        return new IconHandle(handle);
    }

    public void Dispose()
    {
        _small.Dispose();
        _large.Dispose();
    }

    private sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal IconHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => WindowsNative.DestroyIcon(handle) != 0;
    }
}