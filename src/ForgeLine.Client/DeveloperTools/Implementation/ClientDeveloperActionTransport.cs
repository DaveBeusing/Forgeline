using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal sealed partial class ClientSimulationHost
{
    private readonly Queue<DeveloperResult> _runtimeDeveloperResults = new();

    partial void BindDeveloperRuntime() => _scenario.Services.DeveloperActions.BindAccess(
        _boundAccess ?? DeveloperToolsCapability.Current.BindSession(SessionId, false));

    internal DeveloperResult SubmitRuntimeDeveloper(DeveloperSessionAccess access, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(access); ArgumentNullException.ThrowIfNull(request);
        lock (_boundaryGate)
        {
            var revision = RuntimeDeveloperRevision(request.ExpectedRevision.Domain);
            var failure = !access.Capability.IsCompiled ? DeveloperFailureReason.CapabilityUnavailable :
                access.Session != SessionId || request.Session != SessionId ? DeveloperFailureReason.StaleSession :
                !ReferenceEquals(access, _boundAccess) || !IsAuthorized(access) ? DeveloperFailureReason.SessionNotActivated :
                _disposed || IsStopping ? DeveloperFailureReason.Cancelled :
                _failure is not null ? DeveloperFailureReason.Failed :
                IsTerminalFrozen ? DeveloperFailureReason.TerminalSession :
                request.ExpectedRevision != revision ? DeveloperFailureReason.StaleRevision :
                OutstandingHostMessages >= _capacity || _pendingCorrelations.Contains(request.Correlation) ? DeveloperFailureReason.BoundaryFull : DeveloperFailureReason.None;
            if (failure != DeveloperFailureReason.None) return DeveloperResult.Rejected(request, revision, failure);
            _pendingCorrelations.Add(request.Correlation);
            _messages.Enqueue(new(HostMessageKind.DeveloperAction, AllocateHostSequence(), SessionId, null,
                PlayerId.None, SimulationTick.Zero, EntityId.Invalid, access, request));
            _queuedCount++; _signal.Set();
            return DeveloperResult.Accepted(request, revision);
        }
    }

    internal DeveloperStateRevision RuntimeDeveloperRevision(DeveloperRevisionDomain domain) =>
        domain == DeveloperRevisionDomain.Parameters && _scenario.Services.DeveloperParameters is DeveloperGameplayTuningProvider tuning
            ? tuning.CaptureContribution().Revision : _scenario.Services.DeveloperActions is DeveloperCommandGateway gateway
                ? gateway.Revision : new(DeveloperRevisionDomain.Simulation, 0);

    internal IReadOnlyList<DeveloperResult> CaptureRuntimeDeveloperResults()
    {
        lock (_boundaryGate)
            return Array.AsReadOnly(_runtimeDeveloperResults.Concat(_scenario.Services.DeveloperActions.CaptureResults())
                .GroupBy(result => result.Correlation).Select(group => group.Last()).TakeLast(64).ToArray());
    }

    private void ProcessRuntimeDeveloper(in HostMessage message)
    {
        var request = message.DeveloperRequest!;
        DeveloperResult result;
        if (!ReferenceEquals(message.DeveloperPermission, _boundAccess) || !IsAuthorized(_boundAccess))
            result = DeveloperResult.Rejected(request, RuntimeDeveloperRevision(request.ExpectedRevision.Domain), DeveloperFailureReason.SessionNotActivated);
        else
        {
            var context = new DeveloperExecutionContext(SessionId, RuntimeDeveloperRevision(request.ExpectedRevision.Domain),
                player => _scenario.MatchConfiguration.Participants.Any(p => p.Player == player), _simulation.Entities.IsAlive);
            result = _scenario.Services.DeveloperActions.Submit(_boundAccess!, context, request);
        }
        CompleteRuntimeDeveloper(message, result);
    }

    private void CompleteRuntimeDeveloper(in HostMessage message, DeveloperResult result)
    {
        lock (_boundaryGate)
        {
            _developerInFlight--; _pendingCorrelations.Remove(message.DeveloperRequest!.Correlation);
            if (_runtimeDeveloperResults.Count == 64) _runtimeDeveloperResults.Dequeue();
            _runtimeDeveloperResults.Enqueue(result);
        }
        NotifyProgress();
    }
}
