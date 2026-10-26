using System.Collections.Concurrent;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal sealed partial class ClientSimulationHost
{
    private static readonly HashSet<DeveloperOperation> SessionOperations =
        [DeveloperOperation.Pause, DeveloperOperation.Resume];
    private readonly ConcurrentQueue<ClientDeveloperControlCompletion> _developerCompletions = new();
    private readonly HashSet<Guid> _pendingCorrelations = [];
    private DeveloperSessionAccess? _boundAccess, _pendingAccess;
    private int _developerInFlight, _developerCompletionCount;

    private sealed class SessionController(ClientSimulationHost host) : IDeveloperSessionController
    {
        public DeveloperProviderStatus Status => new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None);
        public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context,
            DeveloperRequest request) => host.SubmitDeveloper(permission, context, request).Result;
    }

    partial void InitializeDeveloperSession(DeveloperSessionAccess? access, ref IDeveloperSessionController controller)
    {
        _boundAccess = access;
        _developerActivated = IsAuthorized(access);
        controller = new SessionController(this);
    }

    private bool IsAuthorized(DeveloperSessionAccess? access) => access is not null &&
        ReferenceEquals(access.Capability, DeveloperToolsCapability.Current) && access.Capability.IsCompiled &&
        access.IsActive && access.Session == SessionId;

    private DeveloperFailureReason CheckDeveloper(DeveloperSessionAccess permission, DeveloperRequest request,
        DeveloperStateRevision revision)
    {
        var failure = DeveloperRequestGate.Check(permission, new(SessionId, revision, _ => false, _ => false), request, SessionOperations);
        if (failure != DeveloperFailureReason.None) return failure;
        if (!ReferenceEquals(permission, _boundAccess) || !IsAuthorized(permission)) return DeveloperFailureReason.SessionNotActivated;
        if (request.Target != DeveloperTarget.Global || request.Values.Count != 0) return DeveloperFailureReason.InvalidValue;
        return DeveloperFailureReason.None;
    }

    partial void SubmitDeveloperCore(DeveloperSessionAccess permission, DeveloperRequest request, bool transfer,
        ref ClientDeveloperControlReceipt result)
    {
        lock (_boundaryGate)
        {
            var revision = CaptureControlStatus().Revision;
            var failure = CheckDeveloper(permission, request, revision);
            var detail = MapFailure(failure);
            if (failure == DeveloperFailureReason.None)
            {
                detail = _failure is not null ? ClientDeveloperControlFailure.Faulted :
                    _disposed || IsStopping ? ClientDeveloperControlFailure.Stopping :
                    IsTerminalFrozen ? ClientDeveloperControlFailure.Terminal :
                    OutstandingHostMessages >= _capacity ? ClientDeveloperControlFailure.BoundaryFull :
                    _pendingCorrelations.Contains(request.Correlation) ? ClientDeveloperControlFailure.InvalidRequest : ClientDeveloperControlFailure.None;
                failure = FoundationFailure(detail);
            }
            if (failure != DeveloperFailureReason.None)
            {
                result = new(DeveloperResult.Rejected(request, revision, failure), detail);
                return;
            }
            ulong sequence = AllocateHostSequence();
            _pendingCorrelations.Add(request.Correlation);
            _messages.Enqueue(new(transfer ? HostMessageKind.DeveloperMenuTransfer : HostMessageKind.DeveloperControl,
                sequence, request.Session, null, PlayerId.None, SimulationTick.Zero, EntityId.Invalid, permission, request));
            _queuedCount++;
            _signal.Set();
            result = new(DeveloperResult.Accepted(request, revision), ClientDeveloperControlFailure.None, sequence);
        }
    }

    partial void QueueDeveloperAccess(DeveloperSessionAccess permission, ref bool accepted)
    {
        lock (_boundaryGate)
        {
            if (_disposed || IsStopping || permission.Session != SessionId ||
                !ReferenceEquals(permission.Capability, DeveloperToolsCapability.Current)) return;
            _pendingAccess = permission;
            _signal.Set();
            accepted = true;
        }
    }

    partial void DrainDeveloperAccess()
    {
        DeveloperSessionAccess? access;
        lock (_boundaryGate)
        {
            access = _pendingAccess; _pendingAccess = null;
            if (access is null || ReferenceEquals(access, _boundAccess)) return;
            _boundAccess = access;
        }
        _developerActivated = IsAuthorized(access);
        _controlRevision = checked(_controlRevision + 1);
        if (!_developerActivated)
        {
            var reasons = _pauseReasons & ~ClientSimulationPauseReason.Developer;
            if (_menuPauseTransferred) reasons |= ClientSimulationPauseReason.Menu;
            _menuPauseTransferred = false;
            ApplyPauseReasons(reasons);
        }
        PublishControlStatus();
    }

    partial void AddDeveloperOutstanding(ref int count) => count += _developerInFlight + _developerCompletionCount;
    partial void DeveloperMessageDequeued(in HostMessage message)
    { if (message.DeveloperRequest is not null) _developerInFlight++; }

    partial void ReadDeveloperCompletion(ref ClientDeveloperControlCompletion completion, ref bool found)
    {
        lock (_boundaryGate)
        {
            if (!_developerCompletions.TryDequeue(out completion)) return;
            _developerCompletionCount--;
            _pendingCorrelations.Remove(completion.Result.Correlation);
            found = true;
        }
    }

    partial void ProcessDeveloperMessage(in HostMessage message, ref bool handled)
    {
        if (message.DeveloperRequest is not { } request) return;
        handled = true;
        DeveloperFailureReason failure;
        lock (_boundaryGate) failure = CheckDeveloper(message.DeveloperPermission!, request, CurrentControlRevision);
        var detail = MapFailure(failure);
        if (failure == DeveloperFailureReason.None)
            detail = IsStopping ? ClientDeveloperControlFailure.Stopping : IsTerminalFrozen ? ClientDeveloperControlFailure.Terminal : ClientDeveloperControlFailure.None;
        if (detail != ClientDeveloperControlFailure.None)
        { CompleteDeveloper(message, detail, failure); return; }
        try
        {
            bool pause = request.Operation == DeveloperOperation.Pause;
            var reasons = _pauseReasons;
            if (message.Kind == HostMessageKind.DeveloperMenuTransfer)
            {
                if (pause)
                {
                    if (!_menuPauseTransferred && (reasons & ClientSimulationPauseReason.Menu) == 0)
                    { CompleteDeveloper(message, ClientDeveloperControlFailure.MenuPauseRequired); return; }
                    _menuPauseTransferred = true;
                    reasons &= ~ClientSimulationPauseReason.Menu;
                }
                else
                {
                    _menuPauseTransferred = false;
                    reasons |= ClientSimulationPauseReason.Menu;
                }
            }
            reasons = pause ? reasons | ClientSimulationPauseReason.Developer : reasons & ~ClientSimulationPauseReason.Developer;
            ApplyPauseReasons(reasons);
            CompleteDeveloper(message, ClientDeveloperControlFailure.None);
        }
        catch { CompleteDeveloper(message, ClientDeveloperControlFailure.Faulted); throw; }
    }

    private DeveloperStateRevision CurrentControlRevision => new(DeveloperRevisionDomain.Session, _controlRevision);

    private void CompleteDeveloper(in HostMessage message, ClientDeveloperControlFailure detail,
        DeveloperFailureReason failure = DeveloperFailureReason.None)
    {
        var request = message.DeveloperRequest!;
        var result = detail == ClientDeveloperControlFailure.None
            ? DeveloperResult.Applied(request, CurrentControlRevision, _lastCompletedTick,
                [DeveloperAppliedValue.ForSession(DeveloperEffectKind.Paused, DeveloperValue.FromBoolean(IsPaused))])
            : DeveloperResult.Rejected(request, CurrentControlRevision, failure != DeveloperFailureReason.None ? failure : FoundationFailure(detail));
        lock (_boundaryGate)
        {
            _developerInFlight--; _developerCompletionCount++;
            _developerCompletions.Enqueue(new(result, detail, 0, 0, _lastCompletedTick, message.Sequence));
        }
        NotifyProgress();
    }

    partial void RejectDeveloperMessage(in HostMessage message, ClientSubmissionFailure failure)
    {
        if (message.DeveloperRequest is not null) CompleteDeveloper(message,
            failure == ClientSubmissionFailure.Faulted ? ClientDeveloperControlFailure.Faulted : ClientDeveloperControlFailure.Stopping);
    }

    private static ClientDeveloperControlFailure MapFailure(DeveloperFailureReason failure) => failure switch
    {
        DeveloperFailureReason.None => ClientDeveloperControlFailure.None,
        DeveloperFailureReason.CapabilityUnavailable => ClientDeveloperControlFailure.CapabilityUnavailable,
        DeveloperFailureReason.SessionNotActivated => ClientDeveloperControlFailure.NotActivated,
        DeveloperFailureReason.StaleSession => ClientDeveloperControlFailure.OldSession,
        DeveloperFailureReason.StaleRevision => ClientDeveloperControlFailure.StaleRevision,
        DeveloperFailureReason.UnsupportedOperation => ClientDeveloperControlFailure.UnsupportedOperation,
        _ => ClientDeveloperControlFailure.InvalidRequest
    };

    private static DeveloperFailureReason FoundationFailure(ClientDeveloperControlFailure failure) => failure switch
    {
        ClientDeveloperControlFailure.None => DeveloperFailureReason.None,
        ClientDeveloperControlFailure.CapabilityUnavailable => DeveloperFailureReason.CapabilityUnavailable,
        ClientDeveloperControlFailure.NotActivated => DeveloperFailureReason.SessionNotActivated,
        ClientDeveloperControlFailure.OldSession => DeveloperFailureReason.StaleSession,
        ClientDeveloperControlFailure.StaleRevision => DeveloperFailureReason.StaleRevision,
        ClientDeveloperControlFailure.UnsupportedOperation => DeveloperFailureReason.UnsupportedOperation,
        ClientDeveloperControlFailure.InvalidRequest => DeveloperFailureReason.InvalidValue,
        ClientDeveloperControlFailure.BoundaryFull => DeveloperFailureReason.ProviderUnavailable,
        ClientDeveloperControlFailure.Faulted => DeveloperFailureReason.Failed,
        _ => DeveloperFailureReason.Cancelled
    };
}
