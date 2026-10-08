using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeLine.Client;

internal enum StartupPhase
{
    Launch, Settings, Window, BootRenderer, RuntimeAssets, StudioSplash,
    Frontend, SessionReconstruction, GameplayRenderer,
    WindowVisible, FirstPresentedFrame, StudioSplashFirstFrame,
    MainMenuFirstFrame, MainMenuInteractive, RuntimeAssetsReady,
    FrontendReady, SessionRuntimeReady, FirstGameplayFrame, ApplicationReady, Run, SessionReady,
    SplashBootstrapFirstFrame, SaveCatalog, FrontendDependenciesReady, SplashArtwork,
    SessionLoadRequested, SessionConfiguration, SessionAssembly, SessionReplay, SessionVerification,
    SessionPresentationBinding, SessionRendererReady, SessionLoadFailed, SessionLoadCancelled
}

internal enum StartupEventKind { Started, Completed, Milestone, Skipped, Failed, Cancelled }
internal enum StartupReadiness { Pending, Ready, Failed, Cancelled }

internal readonly record struct StartupEvent(
    int Sequence, int SessionId, StartupPhase Phase, StartupEventKind Kind,
    long Timestamp, double ElapsedMilliseconds, double? DurationMilliseconds, string? Detail);

// Collection is bounded and performs no serialization, logging or file I/O on owner threads.
internal sealed class StartupDiagnostics
{
    private const int PhaseCount = (int)StartupPhase.SessionLoadCancelled + 1;
    internal static StartupDiagnostics Disabled { get; } = new();
    private readonly object _gate = new();
    private readonly StartupEvent[]? _events;
    private readonly long[]? _starts;
    private readonly bool[]? _milestones;
    private readonly long _origin;
    private int _count;
    private int _dropped;
    private StartupReadiness _application;
    private StartupReadiness _session;
    private bool _sessionStarted;
    private int _sessionId;

    private StartupDiagnostics() { }

    internal StartupDiagnostics(long processEntryTimestamp, Guid processId, int launchId)
    {
        _origin = processEntryTimestamp;
        ProcessId = processId;
        LaunchId = launchId;
        _events = new StartupEvent[128];
        _starts = new long[PhaseCount * 2];
        Array.Fill(_starts, -1L);
        _milestones = new bool[PhaseCount * 2];
    }

    internal Guid ProcessId { get; }
    internal int LaunchId { get; }
    internal bool Enabled => _events is not null;
    internal int CurrentSessionId { get { lock (_gate) return Enabled ? _sessionId : 1; } }
    internal StartupReadiness ApplicationReadiness { get { lock (_gate) return _application; } }
    internal StartupReadiness SessionReadiness { get { lock (_gate) return _session; } }

    internal void Begin(StartupPhase phase, int sessionId = 0)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            int slot = Slot(phase, sessionId);
            if (_starts![slot] >= 0) return;
            long now = Stopwatch.GetTimestamp();
            _starts[slot] = now;
            Add(phase, StartupEventKind.Started, sessionId, now, null, null);
        }
    }

    internal void End(StartupPhase phase, StartupEventKind kind = StartupEventKind.Completed,
        int sessionId = 0, string? detail = null)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            int slot = Slot(phase, sessionId);
            long start = _starts![slot];
            if (start < 0) return;
            long now = Stopwatch.GetTimestamp();
            _starts[slot] = -1;
            Add(phase, kind, sessionId, now, Stopwatch.GetElapsedTime(start, now).TotalMilliseconds, detail);
        }
    }

    internal T Measure<T>(StartupPhase phase, Func<T> operation, int sessionId = 0)
    {
        Begin(phase, sessionId);
        try
        {
            T result = operation();
            End(phase, sessionId: sessionId);
            return result;
        }
        catch (Exception error)
        {
            End(phase, error is OperationCanceledException ? StartupEventKind.Cancelled : StartupEventKind.Failed,
                sessionId, error.GetType().Name);
            throw;
        }
    }

    internal void Mark(StartupPhase phase, int sessionId = 0, string? detail = null)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            int slot = Slot(phase, sessionId);
            if (_milestones![slot]) return;
            _milestones[slot] = true;
            Add(phase, StartupEventKind.Milestone, sessionId, Stopwatch.GetTimestamp(), null, detail);
        }
    }

    internal bool Has(StartupPhase phase, int sessionId = 0)
    {
        if (!Enabled) return false;
        lock (_gate) return _milestones![Slot(phase, sessionId)];
    }

    internal void BeginSession()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_sessionStarted)
            {
                for (int phase = 0; phase < PhaseCount; phase++)
                    End((StartupPhase)phase, StartupEventKind.Cancelled, _sessionId);
                Array.Fill(_starts!, -1L, PhaseCount, PhaseCount);
                Array.Clear(_milestones!, PhaseCount, PhaseCount);
            }
            _sessionStarted = true;
            _session = StartupReadiness.Pending;
            _sessionId++;
            Mark(StartupPhase.SessionLoadRequested, _sessionId);
        }
    }

    internal void SessionLoadEnded(bool cancelled, string? detail = null)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            _session = cancelled ? StartupReadiness.Cancelled : StartupReadiness.Failed;
            Mark(cancelled ? StartupPhase.SessionLoadCancelled : StartupPhase.SessionLoadFailed, _sessionId, detail);
            for (int phase = 0; phase < PhaseCount; phase++)
                End((StartupPhase)phase, cancelled ? StartupEventKind.Cancelled : StartupEventKind.Failed, _sessionId, detail);
        }
    }

    internal void MenuInteractive()
    {
        if (!Has(StartupPhase.MainMenuFirstFrame)) return;
        lock (_gate)
        {
            Mark(StartupPhase.MainMenuInteractive);
            ReadyApplication();
        }
    }

    internal void GameplayPresented()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (!_sessionStarted || _session != StartupReadiness.Pending) return;
            Mark(StartupPhase.FirstPresentedFrame);
            Mark(StartupPhase.FirstGameplayFrame, _sessionId);
            Mark(StartupPhase.SessionReady, _sessionId);
            _session = StartupReadiness.Ready;
            ReadyApplication();
        }
    }

    private void ReadyApplication()
    {
        if (_application != StartupReadiness.Pending) return;
        _application = StartupReadiness.Ready;
        Mark(StartupPhase.ApplicationReady);
        End(StartupPhase.Launch);
    }

    internal void Finish(Exception? error = null)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            StartupEventKind kind = error is null or OperationCanceledException
                ? StartupEventKind.Cancelled : StartupEventKind.Failed;
            StartupReadiness state = kind == StartupEventKind.Failed
                ? StartupReadiness.Failed : StartupReadiness.Cancelled;
            if (error is not null || _application == StartupReadiness.Pending) _application = state;
            if (_sessionStarted && (error is not null || _session == StartupReadiness.Pending)) _session = state;
            End(StartupPhase.Run, error is null && _application == StartupReadiness.Ready
                ? StartupEventKind.Completed : kind, detail: error?.GetType().Name);
            for (int slot = 0; slot < _starts!.Length; slot++)
                if (_starts[slot] >= 0)
                    End((StartupPhase)(slot % PhaseCount), kind, slot < PhaseCount ? 0 : _sessionId, error?.GetType().Name);
        }
    }

    internal StartupEvent[] Snapshot()
    {
        if (!Enabled) return [];
        lock (_gate) return _events![.._count];
    }

    internal void WriteReport(string path)
    {
        if (!Enabled) return;
        object report;
        lock (_gate)
            report = new
            {
                SchemaVersion = 1, ProcessId, LaunchId, ProcessEntryTimestamp = _origin,
                StopwatchFrequency = Stopwatch.Frequency,
                ApplicationReadiness = _application,
                SessionReadiness = _sessionStarted ? _session.ToString() : "NotRequested",
                DroppedEvents = _dropped, Events = Snapshot(),
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription
            };
        try
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, ReportSerialization.Options));
            Console.WriteLine($"[startup:report] path=\"{fullPath}\"");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Console.Error.WriteLine($"[startup:report-failed] type={error.GetType().Name} message={error.Message}");
        }
    }

    private static class ReportSerialization
    {
        internal static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    private static int Slot(StartupPhase phase, int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        return (int)phase + (sessionId == 0 ? 0 : PhaseCount);
    }

    private void Add(StartupPhase phase, StartupEventKind kind, int sessionId,
        long now, double? duration, string? detail)
    {
        if (_count == _events!.Length) { _dropped++; return; }
        _events[_count] = new StartupEvent(_count + 1, sessionId, phase, kind, now,
            Stopwatch.GetElapsedTime(_origin, now).TotalMilliseconds, duration, detail);
        _count++;
    }
}
