using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Audio;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using static ForgeLine.Client.WaveOutUiVoice;

namespace ForgeLine.Client;

internal interface IWorldAudioOutput : IDisposable
{
    bool Ready { get; }
    void Write(short[] block);
    void Stop();
}

/// <summary>Three fixed 10 ms buffers. Only the audio owner calls WinMM.</summary>
internal sealed class WaveOutWorldOutput : IWorldAudioOutput
{
    private const int Buffers = 3, Bytes = WorldPcmMixer.BlockFrames * 4;
    private readonly IWaveOutApi _api;
    private readonly IntPtr[] _data = new IntPtr[Buffers], _headers = new IntPtr[Buffers];
    private readonly bool[] _prepared = new bool[Buffers];
    private readonly long[] _deadlines = new long[Buffers];
    private readonly Func<long> _clock;
    private IntPtr _device;
    private bool _disposed;
    private int _next;
    internal bool OwnsNativeResources => _device != IntPtr.Zero;
    internal int QueuedBuffers
    {
        get { int count = 0; for (int i = 0; i < Buffers; i++) if (_prepared[i] && (Marshal.PtrToStructure<WaveHeader>(_headers[i]).Flags & 1) == 0) count++; return count; }
    }
    internal WaveOutWorldOutput(IWaveOutApi? api = null, Func<long>? clock = null)
    {
        _api = api ?? new WinMmApi(); _clock = clock ?? Stopwatch.GetTimestamp;
        var format = new WaveFormat { Tag = 1, Channels = 2, Rate = 48000, BytesPerSecond = 192000, BlockAlign = 4, Bits = 16 };
        if (_api.Open(out _device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            throw new InvalidOperationException("World audio device is unavailable.");
        for (int i = 0; i < Buffers; i++) { _data[i] = Marshal.AllocHGlobal(Bytes); _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>()); }
    }
    public bool Ready
    {
        get
        {
            if (_disposed || _device == IntPtr.Zero) return false;
            if (!_prepared[_next]) return true;
            if ((Marshal.PtrToStructure<WaveHeader>(_headers[_next]).Flags & 1) != 0) return true;
            if (_clock() > _deadlines[_next]) throw new InvalidOperationException("World audio output stopped returning buffers.");
            return false;
        }
    }
    public void Write(short[] block)
    {
        if (block.Length != WorldPcmMixer.BlockFrames * 2) throw new ArgumentException("Invalid stereo block.", nameof(block));
        if (!Ready) throw new InvalidOperationException("World audio output is busy.");
        int i = _next;
        if (_prepared[i])
        {
            if (_api.Unprepare(_device, _headers[i], (uint)Marshal.SizeOf<WaveHeader>()) != 0) throw new InvalidOperationException("World audio buffer could not be returned.");
            _prepared[i] = false;
        }
        Marshal.Copy(block, 0, _data[i], block.Length);
        Marshal.StructureToPtr(new WaveHeader { Data = _data[i], Length = Bytes }, _headers[i], false);
        if (_api.Prepare(_device, _headers[i], (uint)Marshal.SizeOf<WaveHeader>()) != 0) throw new InvalidOperationException("World audio buffer preparation failed.");
        _prepared[i] = true;
        if (_api.Write(_device, _headers[i], (uint)Marshal.SizeOf<WaveHeader>()) != 0) throw new InvalidOperationException("World audio write failed.");
        _deadlines[i] = _clock() + Stopwatch.Frequency; // bounded driver-stall detection
        _next = (i + 1) % Buffers;
    }
    public void Stop()
    {
        if (_device == IntPtr.Zero) return;
        if (_api.Reset(_device) != 0) throw new InvalidOperationException("World audio reset failed.");
    }
    public void Dispose()
    {
        _disposed = true;
        if (_device == IntPtr.Zero) return;
        _ = _api.Reset(_device);
        bool retained = false;
        for (int i = 0; i < Buffers; i++)
        {
            if (_prepared[i] && _api.Unprepare(_device, _headers[i], (uint)Marshal.SizeOf<WaveHeader>()) == 0) _prepared[i] = false;
            retained |= _prepared[i];
        }
        // Failed drivers retain ownership; never free a header they may still access.
        if (retained || _api.Close(_device) != 0) return;
        _device = IntPtr.Zero;
        for (int i = 0; i < Buffers; i++)
        { Marshal.FreeHGlobal(_data[i]); Marshal.FreeHGlobal(_headers[i]); _data[i] = _headers[i] = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
    ~WaveOutWorldOutput() => Dispose();
}

internal readonly record struct WorldAudioDiagnostics(int LoadedClips, long Blocks, long Failures, int Voices, bool Available, bool Stopped, int Loops = 0, long DroppedShots = 0, long LimitedSamples = 0, int QueuedBuffers = 0);

/// <summary>Single-slot immutable handoff. Device/file/PCM work never runs on rendering or simulation threads.</summary>
internal sealed class WaveOutWorldAudio : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private readonly Func<IWorldAudioOutput> _createOutput;
    private readonly Action<WorldPcmMixer> _load;
    private readonly SimulationSessionId? _expectedSession;
    private Packet _packet;
    private bool _stop;
    private long _resetSerial;
    private WorldAudioDiagnostics _diagnostics;
    private readonly OwnerWorkloadProbe? _workload;
    internal OwnerWorkloadSnapshot Workload => _workload?.Capture() ?? default;
    internal WorldAudioDiagnostics Diagnostics { get { lock (_gate) return _diagnostics; } }
    private readonly record struct Packet(PresentationSnapshot? Snapshot, AudioListener Listener, WorldAudioSettings Settings, bool Active, long Timestamp);
    internal WaveOutWorldAudio(Func<IWorldAudioOutput>? createOutput = null, Action<WorldPcmMixer>? load = null, SimulationSessionId? expectedSession = null,bool observeWorkload = false)
    {
        _expectedSession = expectedSession;
        _workload=observeWorkload ? new OwnerWorkloadProbe(expectedSession?.Value ?? 0) : null;
        _createOutput = createOutput ?? (() => new WaveOutWorldOutput());
        _load = load ?? (m => { int clips = WorldAudioClips.Load(m); Console.WriteLine($"[world:audio:library] loaded={clips} expected=56"); lock (_gate) _diagnostics = _diagnostics with { LoadedClips = clips }; });
        _thread = new Thread(Run) { IsBackground = true, Name = "ForgeLine World Audio" }; _thread.Start();
    }
    internal void Update(PresentationSnapshot? snapshot, RtsCameraState camera, WorldAudioSettings settings, bool active)
    {
        var right = new System.Numerics.Vector3(-MathF.Cos(camera.YawRadians), 0, MathF.Sin(camera.YawRadians));
        Publish(snapshot, new(camera.Target, right, Math.Clamp(camera.Distance * 1.5f, 180, 900)), settings, active);
    }
    internal void Publish(PresentationSnapshot? snapshot, AudioListener listener, WorldAudioSettings settings, bool active)
    {
        lock (_gate)
        {
            if (_stop) return;
            _workload?.Event(2);
            if (_packet.Active != active || _packet.Settings.Enabled != settings.Enabled || (_packet.Settings.Master > 0) != (settings.Master > 0)) _resetSerial++;
            _packet = new(snapshot, listener, settings, active, Stopwatch.GetTimestamp()); _signal.Set();
        }
    }
    internal void StopPlayback()
    { lock (_gate) { if (_stop) return; if (_packet.Active) _resetSerial++; _packet = _packet with { Active = false }; _signal.Set(); } }
    private void Run()
    {
        _workload?.BindOwner();long initialization=_workload?.Begin(0) ?? 0;
        IWorldAudioOutput? output = null;
        var mixer = new WorldPcmMixer(); var playback = new WorldAudioPlayback(mixer, _expectedSession);
        short[] block = new short[WorldPcmMixer.BlockFrames * 2];
        long retryAt = 0, blocks = 0, failures = 0, resetSerial = -1; int attempts = 0; bool wasActive = false, retained = false;
        try
        {
            _load(mixer);_workload?.End(0,initialization);
            while (true)
            {
                long iteration=_workload?.Begin(1) ?? 0;bool completed=false;
                try
                {
                _signal.WaitOne(2);
                Packet packet; long serial;
                lock (_gate) { if (_stop) { completed=true;break; } packet = _packet; serial = _resetSerial; }
                long now = Stopwatch.GetTimestamp();
                bool active = packet.Active && now >= packet.Timestamp && Stopwatch.GetElapsedTime(packet.Timestamp, now) <= TimeSpan.FromMilliseconds(250);
                bool requested = active && !retained && packet.Settings.Enabled && packet.Settings.Master > 0;
                if (!requested) attempts = 0;
                try
                {
                    if (serial != resetSerial)
                    {
                        _workload?.Event(4);
                        output?.Stop(); mixer.Clear();
                        playback.Update(packet.Snapshot, packet.Listener, packet.Settings, false, now);
                        resetSerial = serial; wasActive = false;
                    }
                    if (!requested && wasActive && output is not null) { output.Stop(); mixer.Clear(); }
                    bool previouslyActive = wasActive;
                    if (requested && output is null && !retained && attempts < 4 && now >= retryAt)
                    {
                        long opened=_workload?.Begin(2) ?? 0;
                        try { output=_createOutput(); }
                        // A handled device failure completes this attempt; Events1 retains its outcome.
                        finally { _workload?.End(2,opened); }
                    }
                    playback.Update(packet.Snapshot, packet.Listener, packet.Settings, active && output is not null && !retained, now);
                    if (!playback.Active && previouslyActive && output is not null) output.Stop();
                    wasActive = playback.Active;
                    if (wasActive && output!.Ready) { mixer.Render(block); output.Write(block); blocks++; attempts = 0;_workload?.Event(0); }
                }
                catch (Exception error) when (error is InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException)
                {
                    failures++; attempts++;_workload?.Event(1);mixer.Clear();
                    playback.Update(packet.Snapshot, packet.Listener, packet.Settings, false, now);
                    output?.Dispose(); retained = output is WaveOutWorldOutput native && native.OwnsNativeResources;
                    if (!retained) output = null;
                    retryAt = now + (long)(Stopwatch.Frequency * (attempts == 1 ? .25 : attempts == 2 ? 1 : 4));
                    wasActive = false;
                    Console.Error.WriteLine($"[world:audio:device-unavailable] attempt={attempts} retained={retained} error={error.GetType().Name}");
                }
                lock (_gate) _diagnostics = _diagnostics with { Blocks = blocks, Failures = failures, Voices = mixer.ActiveVoices, Loops = mixer.ActiveLoops, DroppedShots = mixer.DroppedShots, LimitedSamples = mixer.LimitedSamples, QueuedBuffers = (output as WaveOutWorldOutput)?.QueuedBuffers ?? 0, Available = output is not null && !retained };
                completed=true;
                }
                finally { _workload?.End(1,iteration,completed); }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or FormatException or KeyNotFoundException)
        { _workload?.Fault();Console.Error.WriteLine($"[world:audio:owner-unavailable] {error.GetType().Name}"); }
        finally
        {
            output?.Dispose();
            Console.WriteLine($"[world:audio:stopped] blocks={blocks} failures={failures} dropped={mixer.DroppedShots} limited={mixer.LimitedSamples} retained={output is WaveOutWorldOutput native && native.OwnsNativeResources}");
            lock (_gate) { _stop = true; _diagnostics = _diagnostics with { Stopped = true, Available = false, Voices = 0 }; _signal.Dispose(); }
        }
    }
    public void Dispose() { lock (_gate) { if (_stop) return; _stop = true; _signal.Set(); } }
    internal bool WaitForStop(TimeSpan timeout) => _thread.Join(timeout);
}
