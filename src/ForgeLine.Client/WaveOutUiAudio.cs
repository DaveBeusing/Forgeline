using System.Diagnostics;
using ForgeLine.Audio;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal readonly record struct UiAudioRequest(UiAudioCue Cue, TimeSpan CreatedAt);

/// <summary>One bounded pending cue; results outrank decorative input. All timestamps are monotonic.</summary>
internal sealed class UiAudioMailbox
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMilliseconds(150);
    private UiAudioRequest _pending;
    private TimeSpan? _lastDecorative;
    internal long Dropped { get; private set; }
    internal int Count => _pending.Cue == UiAudioCue.None ? 0 : 1;
    internal static bool IsResult(UiAudioCue cue) => cue is UiAudioCue.Accepted or UiAudioCue.Rejected;
    private static int Priority(UiAudioCue cue) => cue switch
    { UiAudioCue.Rejected => 3, UiAudioCue.Accepted => 2, UiAudioCue.Intent => 1, _ => 0 };

    internal void Offer(UiAudioRequest request)
    {
        if (request.Cue is <= UiAudioCue.None or > UiAudioCue.Rejected) return;
        if (_pending.Cue != UiAudioCue.None)
        {
            Dropped++;
            if (Priority(request.Cue) < Priority(_pending.Cue)) return;
            // A failed old attempt must never replace a newer result.
            if (request.CreatedAt < _pending.CreatedAt && Priority(request.Cue) == Priority(_pending.Cue)) return;
        }
        _pending = request;
    }

    internal UiAudioRequest Take(TimeSpan now)
    {
        var request = _pending;
        _pending = default;
        if (request.Cue == UiAudioCue.None) return default;
        if (now < request.CreatedAt || now - request.CreatedAt > Lifetime ||
            !IsResult(request.Cue) && _lastDecorative is { } previous && now - previous < TimeSpan.FromMilliseconds(60))
        { Dropped++; return default; }
        if (!IsResult(request.Cue)) _lastDecorative = now;
        return request;
    }

    internal void Clear()
    {
        if (Count != 0) Dropped++;
        _pending = default;
        _lastDecorative = null;
    }
}

internal readonly record struct UiAudioDiagnostics(bool Available, bool OwnsNativeResources,
    long Played, long Dropped, long PlaybackFailures, long ClipFailures, long DeviceFailures, int QueueDepth, bool OwnerStopped);

/// <summary>Bounded asynchronous UI output. Only the audio owner touches files, WinMM and native buffers.</summary>
internal sealed class WaveOutUiAudio : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _owner;
    private readonly Func<IUiAudioOutput> _createOutput;
    private readonly UiAudioMailbox _mailbox = new();
    private bool _enabled, _stopping;
    private float _volume;
    private long _generation;
    private UiAudioDiagnostics _diagnostics;
    internal UiAudioDiagnostics Diagnostics { get { lock (_gate) return _diagnostics with { Dropped = _mailbox.Dropped, QueueDepth = _mailbox.Count }; } }
    internal bool Available => Diagnostics.Available;
    internal bool OwnsNativeResources => Diagnostics.OwnsNativeResources;
    internal WaveOutUiAudio(Func<IUiAudioOutput>? createOutput = null)
    {
        _createOutput = createOutput ?? (() => new WaveOutUiVoice());
        _owner = new Thread(Run) { IsBackground = true, Name = "ForgeLine UI Audio" };
        _owner.Start();
    }

    internal void Update(UiAudioCue cue, in UiFeedbackSettings settings, TimeSpan now, bool active)
    {
        lock (_gate)
        {
            if (_stopping) return;
            bool enabled = active && settings.SoundEnabled && float.IsFinite(settings.SoundVolume) && settings.SoundVolume is > 0 and <= 1;
            if (_enabled != enabled) { _generation++; _mailbox.Clear(); }
            _enabled = enabled;
            _volume = settings.SoundVolume;
            if (enabled) _mailbox.Offer(new(cue, now));
            _signal.Set();
        }
    }

    internal void StopPlayback()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _generation++;
            _enabled = false;
            _mailbox.Clear();
            _signal.Set();
        }
    }

    private void Run()
    {
        IUiAudioOutput? output = null;
        long generation = -1, played = 0, failures = 0;
        try
        {
            output = _createOutput();
            if (output is WaveOutUiVoice voice) voice.PrepareClips();
            while (true)
            {
                _signal.WaitOne(10);
                UiAudioRequest request;
                long observed;
                float volume;
                bool enabled;
                TimeSpan now = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                lock (_gate)
                {
                    if (_stopping) break;
                    observed = _generation; enabled = _enabled; volume = _volume;
                    request = enabled ? _mailbox.Take(now) : default;
                }
                if (generation != observed)
                {
                    output.StopPlayback();
                    if (enabled && output is WaveOutUiVoice recovering) recovering.ResetRecovery();
                    generation = observed;
                }
                if (request.Cue != UiAudioCue.None)
                {
                    bool success = output.TryPlay(request.Cue, volume);
                    if (success) played++; else failures++;
                    lock (_gate)
                    {
                        if (!success && _generation == observed && _enabled && !_stopping && UiAudioMailbox.IsResult(request.Cue))
                            _mailbox.Offer(request);
                    }
                }
                lock (_gate)
                {
                    var native = output as WaveOutUiVoice;
                    _diagnostics = new(native?.Available ?? false, native?.OwnsNativeResources ?? false,
                        played, _mailbox.Dropped, failures, native?.ClipFailures ?? 0, native?.DeviceFailures ?? 0, _mailbox.Count, false);
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or
            UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"[ui:audio:owner-unavailable] {error.GetType().Name}");
        }
        finally
        {
            output?.Dispose();
            lock (_gate)
            {
                _stopping = true;
                _mailbox.Clear();
                _diagnostics = _diagnostics with { Available = false, OwnsNativeResources = (output as WaveOutUiVoice)?.OwnsNativeResources ?? false, OwnerStopped = true };
                _signal.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true; _mailbox.Clear(); _signal.Set();
        }
        // The owner releases its own resources. A blocked driver cannot stall the renderer or
        // cause another thread to free buffers still owned by WinMM.
    }
    internal bool WaitForStop(TimeSpan timeout) => _owner.Join(timeout);
}
