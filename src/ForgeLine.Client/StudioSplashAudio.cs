using System.Runtime.InteropServices;

namespace ForgeLine.Client;

/// <summary>Owns a short PCM stream; volume and lifetime follow the studio presentation.</summary>
internal sealed class StudioSplashAudio : IDisposable
{
    private IntPtr _device, _data, _header;
    private bool _attempted, _prepared, _disposed;

    internal void Update(float elapsed, float opacity)
    {
        if (_disposed) return;
        if (!_attempted)
        {
            _attempted = true;
            try { Start(elapsed); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException)
            {
                Console.Error.WriteLine($"[studio:audio:unavailable] {error.Message}");
                Dispose();
                return;
            }
        }
        if (_device != IntPtr.Zero)
        {
            uint gain = (uint)(Math.Clamp(opacity, 0, 1) * ushort.MaxValue);
            _ = waveOutSetVolume(_device, gain | (gain << 16));
        }
    }

    private void Start(float elapsed)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "branding", "studio-intro.wav");
        if (!File.Exists(path)) return;
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Invalid studio audio.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Invalid studio audio.");
        WaveFormat format = default;
        byte[]? pcm = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string kind = new(reader.ReadChars(4));
            uint length = reader.ReadUInt32();
            long next = reader.BaseStream.Position + length + (length & 1);
            if (next > reader.BaseStream.Length || length > 4_000_000) throw new InvalidDataException("Invalid studio audio chunk.");
            if (kind == "fmt " && length >= 16)
                format = new WaveFormat { Tag = reader.ReadUInt16(), Channels = reader.ReadUInt16(), Rate = reader.ReadUInt32(),
                    BytesPerSecond = reader.ReadUInt32(), BlockAlign = reader.ReadUInt16(), Bits = reader.ReadUInt16() };
            else if (kind == "data") pcm = reader.ReadBytes((int)length);
            reader.BaseStream.Position = next;
        }
        if (pcm is null || format.Tag != 1 || format.Channels != 2 || format.Rate != 48000 || format.Bits != 16 || format.BlockAlign != 4)
            throw new InvalidDataException("Studio audio must be stereo 48 kHz PCM16.");
        int offset = (int)Math.Clamp((long)(Math.Max(elapsed, 0) * format.Rate) * format.BlockAlign, 0, pcm.Length);
        offset -= offset % format.BlockAlign;
        if (offset == pcm.Length) return;
        Check(waveOutOpen(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
        _data = Marshal.AllocHGlobal(pcm.Length - offset);
        Marshal.Copy(pcm, offset, _data, pcm.Length - offset);
        _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        Marshal.StructureToPtr(new WaveHeader { Data = _data, Length = (uint)(pcm.Length - offset) }, _header, false);
        Check(waveOutPrepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()));
        _prepared = true;
        Check(waveOutWrite(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()));
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"Studio audio device returned {result}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_device != IntPtr.Zero)
        {
            _ = waveOutReset(_device);
            if (_prepared) _ = waveOutUnprepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>());
            _ = waveOutClose(_device);
        }
        if (_header != IntPtr.Zero) Marshal.FreeHGlobal(_header);
        if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
        _device = _header = _data = IntPtr.Zero;
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutSetVolume(IntPtr device, uint volume);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutClose(IntPtr device);
}
