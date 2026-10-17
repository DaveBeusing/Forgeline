using System.Runtime.InteropServices;
using ForgeLine.Audio;

namespace ForgeLine.Client;

/// <summary>One audio-owner-thread-owned, nonlooping voice. The studio stream is independent.</summary>
internal sealed class WaveOutUiVoice : IUiAudioOutput
{
    private readonly UiPcmClip?[] _clips = new UiPcmClip?[5];
    private readonly byte[] _scaled = new byte[UiPcmClip.MaximumBytes];
    private readonly string? _assetRoot;
    private readonly IWaveOutApi _api;
    private readonly Func<long> _clock;
    private long _playbackDeadline;
    private IntPtr _device, _data, _header;
    private bool _attempted, _prepared, _disposed, _failed;
    private int _openFailures;
    private long _retryAt;
    internal long ClipFailures { get; private set; }
    internal long DeviceFailures { get; private set; }
    internal bool Available => _device != IntPtr.Zero && !_failed && !_disposed;
    internal bool OwnsNativeResources => _device != IntPtr.Zero || _data != IntPtr.Zero || _header != IntPtr.Zero;
    internal uint? DeviceVolume => _device != IntPtr.Zero && _api.GetVolume(_device, out uint volume) == 0 ? volume : null;
    internal WaveOutUiVoice(string? assetRoot = null, IWaveOutApi? api = null, Func<long>? clock = null)
    { _assetRoot = assetRoot; _api = api ?? new WinMmApi(); _clock = clock ?? System.Diagnostics.Stopwatch.GetTimestamp; }

    internal void PrepareClips()
    {
        if (_attempted || _disposed) return;
        _attempted = true;
        string[] names = ["navigation", "intent", "accepted", "rejected"];
        string root = _assetRoot ?? Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "audio", "ui");
        for (int i = 0; i < names.Length; i++)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(root, names[i] + ".wav"));
                _clips[i + 1] = UiPcmClip.Read(stream);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                ClipFailures++;
                Console.Error.WriteLine($"[ui:audio:clip-unavailable] cue={names[i]} error={error.GetType().Name}");
            }
        }
    }

    internal void ResetRecovery() { _openFailures = 0; _retryAt = 0; }

    public bool TryPlay(UiAudioCue cue, float volume)
    {
        if (_disposed || _failed || cue is <= UiAudioCue.None or > UiAudioCue.Rejected ||
            !float.IsFinite(volume) || volume is <= 0 or > 1) return false;
        PrepareClips();
        if (_clips[(int)cue] is not { } clip) return false;
        if (_device == IntPtr.Zero)
        {
            if (_openFailures >= 4 || _clock() < _retryAt) return false;
            try { Initialize(); }
            catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                DeviceFailed();
                return false;
            }
        }
        if (_device == IntPtr.Zero) return false;
        if (_prepared)
        {
            var previous = Marshal.PtrToStructure<WaveHeader>(_header);
            if ((previous.Flags & 1) == 0)
            {
                if (_clock() > _playbackDeadline) DeviceFailed();
                return false;
            } // WHDR_DONE; never reset a busy cue to play another.
            if (_api.Unprepare(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) != 0) { DeviceFailed(); return false; }
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
        uint prepare = _api.Prepare(_device, _header, (uint)Marshal.SizeOf<WaveHeader>());
        if (prepare != 0) { DeviceFailed(); return false; }
        _prepared = true;
        if (_api.Write(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) == 0)
        {
            _openFailures = 0;
            _playbackDeadline = _clock() + (long)((clip.Samples.Length / 192000d + .5) * System.Diagnostics.Stopwatch.Frequency);
            return true;
        }
        DeviceFailed(); return false;
    }

    private void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        var format = new WaveFormat { Tag = 1, Channels = 2, Rate = 48_000, BytesPerSecond = 192_000, BlockAlign = 4, Bits = 16 };
        if (_api.Open(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            throw new InvalidOperationException("UI audio output is unavailable.");
        _data = Marshal.AllocHGlobal(UiPcmClip.MaximumBytes);
        _header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
    }

    private void DeviceFailed()
    {
        DeviceFailures++;
        _openFailures++;
        Release();
        // Never reopen while a failed driver still owns a header or buffer.
        _failed = OwnsNativeResources;
        int delay = _openFailures switch { 1 => 250, 2 => 1000, _ => 4000 };
        _retryAt = _clock() +
            (long)(delay / 1000d * System.Diagnostics.Stopwatch.Frequency);
        Console.Error.WriteLine($"[ui:audio:device-unavailable] attempt={_openFailures} retained={_failed}");
    }

    public void StopPlayback()
    {
        if (_device != IntPtr.Zero && _prepared)
        {
            _ = _api.Reset(_device);
            if (_api.Unprepare(_device, _header, (uint)Marshal.SizeOf<WaveHeader>()) == 0) _prepared = false;
        }

    }

    private void Release()
    {
        StopPlayback();
        // A failing driver must not retain a pointer to memory that we free.
        // The finalizer can retry teardown; an unreturned native buffer remains owned.
        if (_prepared) { _failed = true; return; }
        if (_device != IntPtr.Zero && _api.Close(_device) != 0) { _failed = true; return; }
        if (_header != IntPtr.Zero) Marshal.FreeHGlobal(_header);
        if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
        _device = _header = _data = IntPtr.Zero;
        _prepared = false;
    }

    public void Dispose()
    {
        if (_disposed && !OwnsNativeResources) return;
        Release(); _disposed = true;
        if (_device == IntPtr.Zero) GC.SuppressFinalize(this);
    }

    ~WaveOutUiVoice() => Release();

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    internal interface IWaveOutApi
    {
        uint Open(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        uint Prepare(IntPtr device, IntPtr header, uint size);
        uint Unprepare(IntPtr device, IntPtr header, uint size);
        uint Write(IntPtr device, IntPtr header, uint size);
        uint Reset(IntPtr device);
        uint Close(IntPtr device);
        uint GetVolume(IntPtr device, out uint volume);
    }
    internal sealed class WinMmApi : IWaveOutApi
    {
        public uint Open(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags) => waveOutOpen(out device, id, ref format, callback, instance, flags);
        public uint Prepare(IntPtr device, IntPtr header, uint size) => waveOutPrepareHeader(device, header, size);
        public uint Unprepare(IntPtr device, IntPtr header, uint size) => waveOutUnprepareHeader(device, header, size);
        public uint Write(IntPtr device, IntPtr header, uint size) => waveOutWrite(device, header, size);
        public uint Reset(IntPtr device) => waveOutReset(device);
        public uint Close(IntPtr device) => waveOutClose(device);
        public uint GetVolume(IntPtr device, out uint volume) => waveOutGetVolume(device, out volume);
    }
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutClose(IntPtr device);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint waveOutGetVolume(IntPtr device, out uint volume);
}
