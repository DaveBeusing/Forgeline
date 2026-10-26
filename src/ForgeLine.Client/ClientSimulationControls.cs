using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

[Flags]
internal enum ClientSimulationPauseReason : byte
{
    None = 0, Legacy = 1, Menu = 2, Window = 4, SessionLoading = 8, Developer = 16,
    Terminal = 32, Faulted = 64, Stopping = 128
}

internal enum ClientDeveloperControlFailure : byte
{
    None, CapabilityUnavailable, NotActivated, OldSession, StaleRevision, InvalidRequest,
    UnsupportedOperation, BoundaryFull, Stopping, Faulted, Terminal, DeveloperPauseRequired,
    BlockedByPauseOwner, StepInProgress, Cancelled, MenuPauseRequired
}

internal readonly record struct ClientDeveloperControlReceipt(DeveloperResult Result,
    ClientDeveloperControlFailure Failure, ulong HostSequence = 0);

internal readonly record struct ClientDeveloperControlCompletion(DeveloperResult Result,
    ClientDeveloperControlFailure Failure, uint RequestedSteps, uint CompletedSteps,
    SimulationTick CompletedTick, ulong HostSequence);

/// <summary>A copied boundary observation. Pause owners and pacing state are not authoritative match data.</summary>
internal sealed record ClientSimulationControlStatus(SimulationSessionId Session,
    DeveloperStateRevision Revision, double RequestedSpeed, double? AchievedSpeed,
    ulong CompletedTicks, SimulationTick CompletedTick, TimeSpan TickDuration,
    ClientSimulationPauseReason PauseReasons, bool IsPaused, bool IsStepping,
    uint RequestedSteps, uint CompletedSteps, uint RemainingSteps,
    bool DeveloperActivated, bool MenuPauseTransferred);

/// <summary>A composition-owned handle can change only its own regular pause reason.</summary>
internal sealed class ClientSimulationPauseOwner
{
    private readonly ClientSimulationHost _host;
    private readonly ClientSimulationPauseReason _reason;
    internal ClientSimulationPauseOwner(ClientSimulationHost host, ClientSimulationPauseReason reason)
    { _host = host; _reason = reason; }
    internal bool TrySetPaused(bool paused) => _host.TrySetOwnedPause(_reason, paused);
    internal ClientDeveloperControlReceipt TransferToDeveloper(DeveloperSessionAccess permission,
        DeveloperStateRevision revision) => _host.SubmitMenuPauseTransfer(_reason, permission, revision, true);
    internal ClientDeveloperControlReceipt RestoreFromDeveloper(DeveloperSessionAccess permission,
        DeveloperStateRevision revision) => _host.SubmitMenuPauseTransfer(_reason, permission, revision, false);
}

internal sealed partial class ClientSimulationHost
{
    private ClientSimulationPauseReason _pauseReasons;
    private ClientSimulationPauseReason _pendingPauseValues;
    private ClientSimulationPauseReason _pendingPauseChanges;
    private ulong _controlRevision;
    private double _requestedSpeed = 1;
    private SimulationTick _lastCompletedTick;
    private ulong _pacedCompletedTicks;
    private ulong _measurementBaseTicks;
    private long _measurementStartedAt;
    private bool _measurementActive;
    private uint _requestedSteps { get; set; }
    private uint _completedSteps { get; set; }
    private uint _remainingSteps { get; set; }
    private bool _developerActivated { get; set; }
    private bool _menuPauseTransferred { get; set; }
    private ClientSimulationControlStatus _controlStatus = null!;
    private IDeveloperSessionController _developerSessions = null!;

    internal ClientSimulationPauseOwner MenuPause { get; private set; } = null!;
    internal ClientSimulationPauseOwner WindowPause { get; private set; } = null!;
    internal ClientSimulationPauseOwner SessionLoadingPause { get; private set; } = null!;
    internal IDeveloperSessionController DeveloperSessions => _developerSessions;
    internal ClientSimulationControlStatus CaptureControlStatus() => Volatile.Read(ref _controlStatus);

    private void InitializeSessionControls(DeveloperSessionAccess? access)
    {
        if (_time.TimestampFrequency <= 0) throw new InvalidOperationException("A positive host clock frequency is required.");
        MenuPause = new(this, ClientSimulationPauseReason.Menu);
        WindowPause = new(this, ClientSimulationPauseReason.Window);
        SessionLoadingPause = new(this, ClientSimulationPauseReason.SessionLoading);
        _developerSessions = DeveloperToolsComposition.Create().Sessions;
        InitializeDeveloperSession(access, ref _developerSessions);
        PublishControlStatus();
    }

    internal ClientDeveloperControlReceipt SubmitDeveloper(DeveloperSessionAccess permission,
        DeveloperExecutionContext context, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var result = new ClientDeveloperControlReceipt(DeveloperResult.Rejected(request,
            CaptureControlStatus().Revision, DeveloperFailureReason.CapabilityUnavailable), ClientDeveloperControlFailure.CapabilityUnavailable);
        SubmitDeveloperCore(permission, request, false, ref result);
        return result;
    }

    internal bool TryReadDeveloperCompletion(out ClientDeveloperControlCompletion completion)
    {
        completion = default;
        bool found = false;
        ReadDeveloperCompletion(ref completion, ref found);
        return found;
    }

    internal bool TrySetDeveloperAccess(DeveloperSessionAccess permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        bool accepted = false;
        QueueDeveloperAccess(permission, ref accepted);
        return accepted;
    }

    internal ClientDeveloperControlReceipt SubmitMenuPauseTransfer(ClientSimulationPauseReason owner,
        DeveloperSessionAccess permission, DeveloperStateRevision revision, bool toDeveloper)
    {
        ArgumentNullException.ThrowIfNull(permission);
        var request = new DeveloperRequest(SessionId, Guid.NewGuid(), revision,
            toDeveloper ? DeveloperOperation.Pause : DeveloperOperation.Resume, DeveloperTarget.Global, DeveloperParameterId.None, []);
        var result = new ClientDeveloperControlReceipt(DeveloperResult.Rejected(request,
            CaptureControlStatus().Revision, DeveloperFailureReason.CapabilityUnavailable), ClientDeveloperControlFailure.CapabilityUnavailable);
        if (owner == ClientSimulationPauseReason.Menu) SubmitDeveloperCore(permission, request, true, ref result);
        return result;
    }

    internal bool TrySetOwnedPause(ClientSimulationPauseReason reason, bool paused)
    {
        if (reason is not (ClientSimulationPauseReason.Menu or ClientSimulationPauseReason.Window or ClientSimulationPauseReason.SessionLoading)) return false;
        lock (_boundaryGate)
        {
            if (_disposed || IsStopping) return false;
            _pendingPauseValues = paused ? _pendingPauseValues | reason : _pendingPauseValues & ~reason;
            _pendingPauseChanges |= reason;
            _signal.Set();
            return true;
        }
    }

    private void DrainPauseOwners()
    {
        ClientSimulationPauseReason changes, values;
        lock (_boundaryGate)
        {
            changes = _pendingPauseChanges; values = _pendingPauseValues;
            _pendingPauseChanges = ClientSimulationPauseReason.None;
        }
        if (changes != ClientSimulationPauseReason.None) ApplyPauseReasons((_pauseReasons & ~changes) | (values & changes));
    }

    private void ApplyPauseReasons(ClientSimulationPauseReason reasons)
    {
        if (_pauseReasons != reasons)
        {
            _pauseReasons = reasons;
            _controlRevision = checked(_controlRevision + 1);
            PauseOwnersChanged();
        }
        SynchronizeLifecyclePause();
        PublishControlStatus();
        NotifyProgress();
    }

    private void SynchronizeLifecyclePause()
    {
        if (IsTerminalFrozen) return;
        bool paused = _pauseReasons != ClientSimulationPauseReason.None;
        AdjustDeveloperPause(ref paused);
        if (paused == IsPaused) return;
        var command = new SetMatchPausedCommand(_matchStateEntity, paused);
        _simulation.ExecuteControlCommand(command);
        if (!command.Accepted) throw new InvalidOperationException("The match rejected its host pause transition.");
        Volatile.Write(ref _paused, paused ? 1 : 0);
        if (!paused) RestartPacingInterval();
        else _measurementActive = false;
    }

    private void InitializeOwnerSnapshot()
    {
        MatchState state = _scenario.GetMatchState();
        if (state.Lifecycle == MatchLifecyclePhase.Paused)
        { _pauseReasons = ClientSimulationPauseReason.Legacy; Volatile.Write(ref _paused, 1); }
        if (_simulation.CurrentTick != SimulationTick.Zero && state.Lifecycle != MatchLifecyclePhase.Running)
        {
            // Re-publish an initialized paused/terminal restore through the existing recorded control path.
            _simulation.ExecuteControlCommand(new SetMatchPausedCommand(_matchStateEntity, true));
            _lastCompletedTick = _simulation.CurrentTick;
        }
        else AdvanceOneTick();
        RestartPacingInterval();
        PublishControlStatus();
    }

    private void RestartPacingInterval()
    {
        long now = _time.GetTimestamp();
        _nextTickAt = checked(now + PacingIntervalTicks());
        _measurementStartedAt = now;
        _measurementBaseTicks = _pacedCompletedTicks;
        _measurementActive = !IsPaused && !IsTerminalFrozen;
    }

    private long PacingIntervalTicks() => Math.Max(1, ToHostTicks(_simulation.Clock.TickDuration));

    private void PublishControlStatus()
    {
        ClientSimulationPauseReason reasons = _pauseReasons;
        if (IsTerminalFrozen) reasons |= ClientSimulationPauseReason.Terminal;
        if (Volatile.Read(ref _failure) is not null) reasons |= ClientSimulationPauseReason.Faulted;
        if (IsStopping) reasons |= ClientSimulationPauseReason.Stopping;
        double? achieved = null;
        if (_measurementActive && reasons == ClientSimulationPauseReason.None && _remainingSteps == 0)
        {
            double seconds = (_time.GetTimestamp() - _measurementStartedAt) / (double)_time.TimestampFrequency;
            if (seconds > 0) achieved = (_pacedCompletedTicks - _measurementBaseTicks) * _simulation.Clock.TickDuration.TotalSeconds / seconds;
        }
        Volatile.Write(ref _controlStatus, new(SessionId, new(DeveloperRevisionDomain.Session, _controlRevision),
            _requestedSpeed, achieved, checked((ulong)Volatile.Read(ref _completedTicks)), _lastCompletedTick,
            _simulation.Clock.TickDuration, reasons, IsPaused, _remainingSteps != 0,
            _requestedSteps, _completedSteps, _remainingSteps, _developerActivated, _menuPauseTransferred));
    }

    partial void InitializeDeveloperSession(DeveloperSessionAccess? access, ref IDeveloperSessionController controller);
    partial void SubmitDeveloperCore(DeveloperSessionAccess permission, DeveloperRequest request, bool transfer, ref ClientDeveloperControlReceipt result);
    partial void ReadDeveloperCompletion(ref ClientDeveloperControlCompletion completion, ref bool found);
    partial void QueueDeveloperAccess(DeveloperSessionAccess permission, ref bool accepted);
    partial void DrainDeveloperAccess();
    partial void DeveloperMessageDequeued(in HostMessage message);
    partial void AddDeveloperOutstanding(ref int count);
    partial void ProcessDeveloperMessage(in HostMessage message, ref bool handled);
    partial void RejectDeveloperMessage(in HostMessage message, ClientSubmissionFailure failure);
    partial void StopDeveloperControls(bool faulted);
    partial void ProcessDeveloperStep(ref bool stepped);
    partial void PauseOwnersChanged();
    partial void AdjustDeveloperPause(ref bool paused);
}
