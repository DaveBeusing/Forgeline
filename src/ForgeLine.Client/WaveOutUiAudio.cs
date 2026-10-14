using System.Runtime.InteropServices;
using ForgeLine.Audio;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>One render-thread-owned, nonlooping voice. The studio stream is independent.</summary>
internal sealed class WaveOutUiAudio : IUiAudioOutput
{
    private readonly UiPcmClip?[] _clips = new UiPcmClip?[5];
    private readonly byte[] _scaled = new byte[UiPcmClip.MaximumBytes];
    private IntPtr _device, _data, _header;
    private bool _attempted, _prepared, _disposed, _failed;
    private TimeSpan _lastCue;
    private bool _hasCue;

    internal bool Available => _device != IntPtr.Zero && !_failed && !_disposed;

    internal void Update(UiAudioCue cue, in UiFeedbackSettings settings, TimeSpan now, bool active)
    {
        if (!active || !settings.SoundEnabled || settings.SoundVolume <= 0) { StopPlayback(); return; }
        // No late queue: navigation and autorepeat bursts are dropped at the source.
        if (cue == UiAudioCue.None || _hasCue && now - _lastCue < TimeSpan.FromMilliseconds(60)) return;
        _lastCue = now; _hasCue = true;
        _ = TryPlay(cue, settings.SoundVolume);
    }

    public bool TryPlay(UiAudioCue cue, float volume)
    {
        if (_disposed || _failed || cue is <= UiAudioCue.None or > UiAudioCue.Rejected ||
            !float.IsFinite(volume) || volume is <= 0 or > 1) return false;
        if (!_attempted)
        {
            _attempted = true;
            try { Initialize(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or
                UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
            {
                _failed = true;
                Release();
                Console.Error.WriteLine($"[ui:audio:unavailable] {error.GetType().Name}");
                return false;
            }
        }
        if (_device == IntPtr.Zero || _clips[(int)cue] is not { } clip) return false;
        if (_prepared)
        {
            var previous = Marshal.PtrToStructure<WaveHeader>(_header);
            if ((previous.Flags & 1) == 0) return false; // WHDR_DONE; never reset a busy cue to play another.
            if (waveOutUnprepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) != 0) return false;
            _prepared = false;
        }
        for (int i = 0; i < clip.Samples.Length; i += 2)
        {
            short sample = (short)(clip.Samples[i] | clip.Samples[i + 1] << 8);
            short scaled = (short)Math.Clamp((int)(sample * volume), short.MinValue, short.MaxValue);
            _scaled[i] = (byte)scaled; _scaled[i + 1] = (byte)(scaled >> 8);
        }
        Marshal.Copy(_scaled, 0, _data, clip.Samples.Length);
        Marshal.StructureToPtr(new WaveHeader { Data = _data, Length = (uint)clip.Samples.Length }, _header, false);
        uint prepare = waveOutPrepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>());
        if (prepare != 0) { _failed = true; Release(); return false; }
        _prepared = true;
        if (waveOutWrite(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) == 0) return true;
        _failed = true; Release(); return false;
    }

    private void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        string[] names = ["navigation", "intent", "accepted", "rejected"];
        for (int i = 0; i < names.Length; i++)
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "audio", "ui", names[i] + ".wav"));
            _clips[i + 1] = UiPcmClip.Read(stream);
        }
        var format = new WaveFormat { Tag = 1, Channels = 2, Rate = 48_000, BytesPerSecond = 192_000, BlockAlign = 4, Bits = 16 };
        if (waveOutOpen(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            throw new InvalidOperationException("UI audio output is unavailable.");
        _data = Marshal.AllocHGlobal(UiPcmClip.MaximumBytes);
        _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
    }

    public void StopPlayback()
    {
        if (_device != IntPtr.Zero && _prepared)
        {
            _ = waveOutReset(_device);
            if (waveOutUnprepareHeader(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) == 0) _prepared = false;
        }
        _hasCue = false;
    }

    private void Release()
    {
        StopPlayback();
        if (_device != IntPtr.Zero) _ = waveOutClose(_device);
        if (_header != IntPtr.Zero) Marshal.FreeHGlobal(_header);
        if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
        _device = _header = _data = IntPtr.Zero;
        _prepared = false;
    }

    public void Dispose() { if (_disposed) return; Release(); _disposed = true; }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutClose(IntPtr device);
}
