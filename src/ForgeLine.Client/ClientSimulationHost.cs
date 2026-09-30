using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal enum ClientSubmissionFailure : byte
{
    None = 0,
    BoundaryFull = 1,
    OldSession = 2,
    Terminal = 3,
    Stopping = 4
}

internal readonly record struct ClientSubmissionCompletion(
    ulong HostSequence,
    ClientSubmissionFailure Failure,
    PlayerCommandSubmissionReceipt? Receipt)
{
    public bool Accepted =>
        Failure == ClientSubmissionFailure.None &&
        Receipt?.Accepted == true;
}

internal sealed class ClientSimulationHost : IDisposable
{
    private const int DefaultBoundaryCapacity = 256;
    private const int MaximumCatchUpTicks = 5;

    private readonly object _boundaryGate = new();
    private readonly object _progressGate = new();
    private readonly VerticalSliceScenario _scenario;
    private readonly SimulationCoordinator _simulation;
    private readonly PlayerCommandGateway _commands;
    private readonly PresentationSnapshotBuffer _snapshots;
    private readonly EntityId _matchStateEntity;
    private readonly ConcurrentQueue<HostMessage> _messages = new();
    private readonly ConcurrentQueue<ClientSubmissionCompletion> _completions = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private readonly int _capacity;

    private ExceptionDispatchInfo? _failure;
    private long _nextHostSequence;
    private int _queuedCount;
    private int _inFlightSubmissions;
    private int _completionCount;
    private int _paused;
    private int _terminalFrozen;
    private int _stopping;
    private bool _disposed;

    public ClientSimulationHost(
        VerticalSliceScenario scenario,
        PlayerCommandGateway commands,
        PresentationSnapshotBuffer snapshots,
        int boundaryCapacity = DefaultBoundaryCapacity)
    {
        _scenario =
            scenario ??
            throw new ArgumentNullException(nameof(scenario));
        _simulation =
            scenario.Simulation;
        _commands =
            commands ??
            throw new ArgumentNullException(nameof(commands));
        _snapshots =
            snapshots ??
            throw new ArgumentNullException(nameof(snapshots));

        if (_commands.SessionId !=
            _simulation.SessionId)
        {
            throw new ArgumentException(
                "Simulation host and command gateway must use the same session.",
                nameof(commands));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            boundaryCapacity,
            1);

        _capacity =
            boundaryCapacity;
        _matchStateEntity =
            scenario.BattlefieldRuntime.MatchStateEntity;

        _thread =
            new Thread(SimulationLoop)
            {
                IsBackground = true,
                Name = "ForgeLine Simulation"
            };
        _thread.Start();
        _started.Wait();
        ThrowIfFaulted();
    }

    public SimulationSessionId SessionId =>
        _simulation.SessionId;

    public PlayerCommandResultBuffer CommandResults =>
        _commands.Results;

    public bool IsPaused =>
        Volatile.Read(ref _paused) != 0;

    public bool IsTerminalFrozen =>
        Volatile.Read(ref _terminalFrozen) != 0;

    public int OutstandingHostMessages
    {
        get
        {
            lock (_boundaryGate)
            {
                return
                    _queuedCount +
                    _inFlightSubmissions +
                    _completionCount;
            }
        }
    }

    internal SimulationTick CurrentTick =>
        _simulation.CurrentTick;

    internal bool WaitForTickAtLeast(
        SimulationTick target,
        TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline =
            Stopwatch.GetTimestamp() +
            ToStopwatchTicks(timeout);

        lock (_progressGate)
        {
            while (_simulation.CurrentTick <
                   target)
            {
                ThrowIfFaulted();

                long remainingTicks =
                    deadline -
                    Stopwatch.GetTimestamp();

                if (remainingTicks <= 0)
                {
                    return false;
                }

                Monitor.Wait(
                    _progressGate,
                    StopwatchElapsed(
                        0,
                        remainingTicks));
            }

            return true;
        }
    }

    internal bool WaitForPauseState(
        bool paused,
        TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline =
            Stopwatch.GetTimestamp() +
            ToStopwatchTicks(timeout);

        lock (_progressGate)
        {
            while (IsPaused != paused)
            {
                ThrowIfFaulted();

                long remainingTicks =
                    deadline -
                    Stopwatch.GetTimestamp();

                if (remainingTicks <= 0)
                {
                    return false;
                }

                Monitor.Wait(
                    _progressGate,
                    StopwatchElapsed(
                        0,
                        remainingTicks));
            }

            return true;
        }
    }

    internal bool WaitForTerminalState(
        bool terminal,
        TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline =
            Stopwatch.GetTimestamp() +
            ToStopwatchTicks(timeout);

        lock (_progressGate)
        {
            while (IsTerminalFrozen != terminal)
            {
                ThrowIfFaulted();

                long remainingTicks =
                    deadline -
                    Stopwatch.GetTimestamp();

                if (remainingTicks <= 0)
                {
                    return false;
                }

                Monitor.Wait(
                    _progressGate,
                    StopwatchElapsed(
                        0,
                        remainingTicks));
            }

            return true;
        }
    }

    internal bool WaitForPlayerMatchStatus(
        PlayerMatchStatus status,
        TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline =
            Stopwatch.GetTimestamp() +
            ToStopwatchTicks(timeout);

        lock (_progressGate)
        {
            while (!HasPlayerMatchStatus(status))
            {
                ThrowIfFaulted();

                long remainingTicks =
                    deadline -
                    Stopwatch.GetTimestamp();

                if (remainingTicks <= 0)
                {
                    return false;
                }

                Monitor.Wait(
                    _progressGate,
                    StopwatchElapsed(
                        0,
                        remainingTicks));
            }

            return true;
        }
    }

    internal bool WaitForSubmissionCompletion(
        TimeSpan timeout,
        out ClientSubmissionCompletion completion)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline =
            Stopwatch.GetTimestamp() +
            ToStopwatchTicks(timeout);

        lock (_progressGate)
        {
            while (!TryReadSubmissionCompletion(
                       out completion))
            {
                ThrowIfFaulted();

                long remainingTicks =
                    deadline -
                    Stopwatch.GetTimestamp();

                if (remainingTicks <= 0)
                {
                    completion = default;
                    return false;
                }

                Monitor.Wait(
                    _progressGate,
                    StopwatchElapsed(
                        0,
                        remainingTicks));
            }

            return true;
        }
    }

    internal bool IsExecutionThreadAlive =>
        _thread.IsAlive;

    public bool TrySubmit(
        SimulationSessionId expectedSession,
        Func<PlayerCommandGateway, PlayerCommandSubmissionReceipt> submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        ulong sequence =
            AllocateHostSequence();

        return TryQueue(
            HostMessage.CreateSubmission(
                sequence,
                expectedSession,
                submission));
    }

    public bool TrySetPaused(bool paused) =>
        TryQueue(
            paused
                ? HostMessage.Pause()
                : HostMessage.Resume());

    public bool TryAcknowledgeTerminal(
        SimulationSessionId expectedSession,
        PlayerId issuer,
        SimulationTick observedTick) =>
        TryQueue(
            HostMessage.AcknowledgeTerminal(
                expectedSession,
                issuer,
                observedTick));

    public bool TryRunSmokeCompletion(
        EntityId opposingCommandCore) =>
        TryQueue(
            HostMessage.SmokeCompletion(
                opposingCommandCore));

    public bool TryReadSubmissionCompletion(
        out ClientSubmissionCompletion completion)
    {
        if (!_completions.TryDequeue(
                out completion))
        {
            return false;
        }

        lock (_boundaryGate)
        {
            _completionCount--;
        }

        return true;
    }

    public void ThrowIfFaulted()
    {
        Volatile.Read(ref _failure)?.Throw();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(
            ref _stopping,
            1);
        _signal.Set();

        if (Thread.CurrentThread !=
            _thread)
        {
            _thread.Join();
        }

        _started.Dispose();
        _signal.Dispose();
        _disposed = true;
    }

    private void SimulationLoop()
    {
        try
        {
            _simulation.AdvanceOneTick();
            NotifyProgress();
            UpdateTerminalState();

            long nextTickAt =
                Stopwatch.GetTimestamp() +
                ToStopwatchTicks(
                    _simulation.Clock.TickDuration);

            _started.Set();
            NotifyProgress();

            while (Volatile.Read(ref _stopping) == 0)
            {
                DrainMessages();

                if (Volatile.Read(ref _stopping) != 0)
                {
                    break;
                }

                if (IsPaused ||
                    IsTerminalFrozen)
                {
                    _signal.WaitOne(
                        TimeSpan.FromMilliseconds(16));
                    continue;
                }

                long now =
                    Stopwatch.GetTimestamp();

                if (now < nextTickAt)
                {
                    TimeSpan wait =
                        StopwatchElapsed(
                            now,
                            nextTickAt);

                    _signal.WaitOne(
                        wait > TimeSpan.FromMilliseconds(16)
                            ? TimeSpan.FromMilliseconds(16)
                            : wait);
                    continue;
                }

                int catchUpTicks = 0;

                while (now >= nextTickAt &&
                       catchUpTicks <
                       MaximumCatchUpTicks &&
                       !IsPaused &&
                       !IsTerminalFrozen &&
                       Volatile.Read(ref _stopping) == 0)
                {
                    DrainMessages();

                    if (IsPaused ||
                        IsTerminalFrozen ||
                        Volatile.Read(ref _stopping) != 0)
                    {
                        break;
                    }

                    _simulation.AdvanceOneTick();
                    NotifyProgress();
                    UpdateTerminalState();

                    catchUpTicks++;
                    nextTickAt +=
                        ToStopwatchTicks(
                            _simulation.Clock.TickDuration);
                    now =
                        Stopwatch.GetTimestamp();
                }

                if (catchUpTicks ==
                        MaximumCatchUpTicks &&
                    now >= nextTickAt)
                {
                    nextTickAt =
                        now +
                        ToStopwatchTicks(
                            _simulation.Clock.TickDuration);
                }
            }
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(
                ref _failure,
                ExceptionDispatchInfo.Capture(
                    exception),
                null);
            _started.Set();
        }
        finally
        {
            RejectRemainingSubmissions(
                ClientSubmissionFailure.Stopping);
        }
    }

    private void DrainMessages()
    {
        while (_messages.TryDequeue(
                   out HostMessage message))
        {
            lock (_boundaryGate)
            {
                _queuedCount--;

                if (message.Kind ==
                    HostMessageKind.Submission)
                {
                    _inFlightSubmissions++;
                }
            }

            switch (message.Kind)
            {
                case HostMessageKind.Submission:
                    ProcessSubmission(
                        message);
                    break;

                case HostMessageKind.Pause:
                    Volatile.Write(
                        ref _paused,
                        1);
                    NotifyProgress();
                    break;

                case HostMessageKind.Resume:
                    Volatile.Write(
                        ref _paused,
                        0);
                    NotifyProgress();
                    break;

                case HostMessageKind.AcknowledgeTerminal:
                    ProcessTerminalAcknowledgement(
                        message);
                    break;

                case HostMessageKind.SmokeCompletion:
                    ProcessSmokeCompletion(
                        message);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported simulation host message '{message.Kind}'.");
            }
        }
    }

    private void ProcessSubmission(
        in HostMessage message)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            PublishCompletion(
                message.Sequence,
                ClientSubmissionFailure.Stopping,
                receipt: null);
            return;
        }

        if (message.ExpectedSession !=
            SessionId)
        {
            PublishCompletion(
                message.Sequence,
                ClientSubmissionFailure.OldSession,
                receipt: null);
            return;
        }

        if (IsTerminalFrozen)
        {
            PublishCompletion(
                message.Sequence,
                ClientSubmissionFailure.Terminal,
                receipt: null);
            return;
        }

        PlayerCommandSubmissionReceipt receipt =
            message.Submission!(
                _commands);

        PublishCompletion(
            message.Sequence,
            ClientSubmissionFailure.None,
            receipt);
    }

    private void ProcessTerminalAcknowledgement(
        in HostMessage message)
    {
        if (message.ExpectedSession !=
                SessionId ||
            !IsTerminalFrozen)
        {
            return;
        }

        var command =
            new EndMatchCommand(
                message.Issuer,
                _matchStateEntity,
                message.ObservedTick);

        _simulation.ExecuteControlCommand(
            command);
        NotifyProgress();

        if (!command.Accepted)
        {
            throw new InvalidOperationException(
                "Terminal acknowledgement was rejected by authoritative match state.");
        }
    }

    private void ProcessSmokeCompletion(
        in HostMessage message)
    {
        while (_simulation.CurrentTick.Value < 2)
        {
            _simulation.AdvanceOneTick();
            NotifyProgress();
        }

        if (_simulation.Entities.IsAlive(
                message.SmokeCommandCore))
        {
            _simulation.Entities.DestroyEntity(
                message.SmokeCommandCore);
        }

        _simulation.AdvanceOneTick();
        NotifyProgress();
        UpdateTerminalState();
    }

    private void UpdateTerminalState()
    {
        if (!_snapshots.TryReadLatest(
                out PresentationSnapshot snapshot) ||
            snapshot.SessionId !=
                SessionId)
        {
            return;
        }

        bool terminal =
            snapshot.PlayerExperience?
                .IsMatchComplete ==
            true;

        int value =
            terminal ? 1 : 0;
        int previous =
            Interlocked.Exchange(
                ref _terminalFrozen,
                value);

        if (previous != value)
        {
            NotifyProgress();
        }
    }

    private bool TryQueue(
        in HostMessage message)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();

        if (Volatile.Read(ref _stopping) != 0)
        {
            return false;
        }

        lock (_boundaryGate)
        {
            if (_queuedCount +
                _inFlightSubmissions +
                _completionCount >=
                _capacity)
            {
                return false;
            }

            _messages.Enqueue(
                message);
            _queuedCount++;
        }

        _signal.Set();
        return true;
    }

    private void PublishCompletion(
        ulong sequence,
        ClientSubmissionFailure failure,
        PlayerCommandSubmissionReceipt? receipt)
    {
        lock (_boundaryGate)
        {
            _inFlightSubmissions--;
            _completionCount++;
            _completions.Enqueue(
                new ClientSubmissionCompletion(
                    sequence,
                    failure,
                    receipt));
        }

        NotifyProgress();
    }

    private void RejectRemainingSubmissions(
        ClientSubmissionFailure failure)
    {
        while (_messages.TryDequeue(
                   out HostMessage message))
        {
            lock (_boundaryGate)
            {
                _queuedCount--;

                if (message.Kind ==
                    HostMessageKind.Submission)
                {
                    _inFlightSubmissions++;
                }
            }

            if (message.Kind ==
                HostMessageKind.Submission)
            {
                PublishCompletion(
                    message.Sequence,
                    failure,
                    receipt: null);
            }
        }
    }

    private bool HasPlayerMatchStatus(
        PlayerMatchStatus status) =>
        _snapshots.TryReadLatest(
            out PresentationSnapshot snapshot) &&
        snapshot.SessionId ==
            SessionId &&
        snapshot.PlayerExperience?.MatchStatus ==
            status;

    private void NotifyProgress()
    {
        lock (_progressGate)
        {
            Monitor.PulseAll(
                _progressGate);
        }
    }

    private ulong AllocateHostSequence()
    {
        long value =
            Interlocked.Increment(
                ref _nextHostSequence);

        if (value <= 0)
        {
            throw new OverflowException(
                "Client simulation host sequence space has been exhausted.");
        }

        return checked((ulong)value);
    }

    private static long ToStopwatchTicks(
        TimeSpan duration) =>
        checked(
            (long)Math.Ceiling(
                duration.TotalSeconds *
                Stopwatch.Frequency));

    private static TimeSpan StopwatchElapsed(
        long start,
        long end) =>
        TimeSpan.FromSeconds(
            (end - start) /
            (double)Stopwatch.Frequency);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private enum HostMessageKind : byte
    {
        Submission = 1,
        Pause = 2,
        Resume = 3,
        AcknowledgeTerminal = 4,
        SmokeCompletion = 5
    }

    private readonly record struct HostMessage(
        HostMessageKind Kind,
        ulong Sequence,
        SimulationSessionId ExpectedSession,
        Func<PlayerCommandGateway, PlayerCommandSubmissionReceipt>? Submission,
        PlayerId Issuer,
        SimulationTick ObservedTick,
        EntityId SmokeCommandCore)
    {
        public static HostMessage CreateSubmission(
            ulong sequence,
            SimulationSessionId expectedSession,
            Func<PlayerCommandGateway, PlayerCommandSubmissionReceipt> submission) =>
            new(
                HostMessageKind.Submission,
                sequence,
                expectedSession,
                submission,
                PlayerId.None,
                SimulationTick.Zero,
                EntityId.Invalid);

        public static HostMessage Pause() =>
            new(
                HostMessageKind.Pause,
                0,
                SimulationSessionId.None,
                null,
                PlayerId.None,
                SimulationTick.Zero,
                EntityId.Invalid);

        public static HostMessage Resume() =>
            new(
                HostMessageKind.Resume,
                0,
                SimulationSessionId.None,
                null,
                PlayerId.None,
                SimulationTick.Zero,
                EntityId.Invalid);

        public static HostMessage AcknowledgeTerminal(
            SimulationSessionId expectedSession,
            PlayerId issuer,
            SimulationTick observedTick) =>
            new(
                HostMessageKind.AcknowledgeTerminal,
                0,
                expectedSession,
                null,
                issuer,
                observedTick,
                EntityId.Invalid);

        public static HostMessage SmokeCompletion(
            EntityId opposingCommandCore) =>
            new(
                HostMessageKind.SmokeCompletion,
                0,
                SimulationSessionId.None,
                null,
                PlayerId.None,
                SimulationTick.Zero,
                opposingCommandCore);
    }
}
