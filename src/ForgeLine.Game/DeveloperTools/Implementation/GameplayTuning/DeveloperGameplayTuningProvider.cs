using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Simulation-thread apply handler and copied observations; submission remains owned by the central dispatcher.</summary>
public sealed class DeveloperGameplayTuningProvider : IDeveloperParameterProvider
{
    private static readonly HashSet<DeveloperOperation> Supported = [DeveloperOperation.SetParameter, DeveloperOperation.ResetParameter, DeveloperOperation.ResetParameters];
    private readonly SimulationCoordinator _simulation;
    private readonly DeveloperRestartPlan _initial;
    private readonly IDeveloperActionDispatcher? _dispatcher;
    private readonly Dictionary<Guid, (DeveloperRequest Request, DeveloperTuningApplication Application)> _completed = new();
    private readonly Queue<Guid> _completionOrder = new();
    private readonly object _completionGate = new();
    private DeveloperTuningContribution _state;
    private SimulationTick _lastConveyorReadTick;
    private ConveyorSimulationSystem? _conveyorOwner;

    public DeveloperGameplayTuningProvider(SimulationCoordinator simulation, DeveloperRestartPlan initial,
        IDeveloperActionDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(simulation); ArgumentNullException.ThrowIfNull(initial);
        if (!DeveloperToolsCapability.Current.IsCompiled) throw new InvalidOperationException("Developer build required.");
        if (simulation.CurrentTick != SimulationTick.Zero && initial.InitialOverrides.Parameters.Count != 0)
            throw new InvalidOperationException("Initial overrides must be bound before the first tick.");
        _simulation = simulation; _initial = initial; _dispatcher = dispatcher; _lastConveyorReadTick = simulation.CurrentTick;
        double multiplier = initial.InitialOverrides.Parameters.FirstOrDefault(p => p.Id == DeveloperParameterId.ConveyorThroughputMultiplier) is var live && live.Value.IsValid
            ? live.Value.AsDouble() : 1;
        var basis = DeveloperGameplayTuningSchema.Capture(initial.BaseSettings, 1).ToDictionary(v => (v.Id, v.Target), v => v.Value);
        var effective = DeveloperGameplayTuningSchema.Capture(initial.Settings, multiplier);
        var rows = effective.Select(v => new DeveloperTuningObservation(v.Id, v.Target, basis[(v.Id, v.Target)], v.Value,
            v.Value, null, DeveloperGameplayTuningSchema.Entries.Single(e => e.Contract.Id == v.Id).Contract.ApplyMode)).ToArray();
        bool modified = rows.Any(v => v.Initial != v.Basis);
        _state = new(initial.InitialOverrides, new(DeveloperRevisionDomain.Parameters, 0), SimulationTick.Zero,
            new(multiplier, new(DeveloperRevisionDomain.Parameters, 0), SimulationTick.Zero), modified, rows);
    }
    public SimulationSessionId Session => _simulation.SessionId;
    public DeveloperProviderStatus Status => _dispatcher?.Status ?? DeveloperProviderStatus.Missing;
    public IReadOnlyList<DeveloperParameterDescriptor> Descriptors { get; } = Array.AsReadOnly(DeveloperGameplayTuningSchema.Entries.Select(e => e.Contract).ToArray());
    public DeveloperTuningContribution CaptureContribution() => Volatile.Read(ref _state);
    public DeveloperParameterReadResult Capture()
    {
        var state = CaptureContribution();
        var status = Status;
        return status.Availability == DeveloperProviderAvailability.Available
            ? new(status, state.Values.Select(v => new DeveloperEffectiveParameter(v.Id, v.Target, v.Effective, state.Revision)).ToArray())
            : new(status, []);
    }
    public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        var state = CaptureContribution();
        var basic = !permission.Capability.IsCompiled ? DeveloperFailureReason.CapabilityUnavailable :
            !permission.IsActive ? DeveloperFailureReason.SessionNotActivated :
            permission.Session != Session || request.Session != Session || context.Session != Session ? DeveloperFailureReason.StaleSession : DeveloperFailureReason.None;
        if (basic != DeveloperFailureReason.None) return DeveloperResult.Rejected(request, state.Revision, basic);
        lock (_completionGate)
            if (_completed.TryGetValue(request.Correlation, out var cached))
                return Same(cached.Request, request) ? cached.Application.Result : DeveloperResult.Rejected(request, state.Revision, DeveloperFailureReason.InvalidValue);
        state = CaptureContribution();
        var own = ExecutionContext(state);
        var failure = DeveloperRequestGate.Check(permission, own, request, Supported);
        if (failure == DeveloperFailureReason.None && context.Revision != state.Revision) failure = DeveloperFailureReason.StaleRevision;
        if (failure == DeveloperFailureReason.None) failure = ValidateRequest(request);
        if (failure != DeveloperFailureReason.None) return DeveloperResult.Rejected(request, state.Revision, failure);
        return _dispatcher is not null && Status.Availability == DeveloperProviderAvailability.Available
            ? _dispatcher.Submit(permission, own, request)
            : DeveloperResult.Rejected(request, state.Revision, DeveloperFailureReason.ProviderUnavailable);
    }
    internal void BindConveyor(ConveyorSimulationSystem system)
    {
        if (_conveyorOwner is not null && !ReferenceEquals(system, _conveyorOwner)) throw new InvalidOperationException("Tuning already owns a conveyor hook.");
        _conveyorOwner = system;
    }
    internal DeveloperConveyorTuning ReadConveyor(SimulationContext context)
    {
        if (!ReferenceEquals(context.Entities, _simulation.Entities) || context.Tick != _simulation.CurrentTick)
            throw new InvalidOperationException("Conveyor tuning belongs to another runtime.");
        _lastConveyorReadTick = context.Tick;
        return CaptureContribution().Conveyor;
    }
    private DeveloperExecutionContext ExecutionContext(DeveloperTuningContribution state) =>
        new(Session, state.Revision, player => _initial.Settings.Participants.Any(p => p.Player == player), _ => false);
    private DeveloperFailureReason ValidateRequest(DeveloperRequest request)
    {
        if (request.Operation == DeveloperOperation.ResetParameters)
        {
            if (request.Parameter != DeveloperParameterId.None || request.Values.Count != 0) return DeveloperFailureReason.InvalidValue;
            return request.Target.Kind == DeveloperTargetKinds.Global || request.Target.Kind == DeveloperTargetKinds.Player &&
                _initial.Settings.Participants.Any(p => p.Player == request.Target.Player && p.IsComputerControlled)
                ? DeveloperFailureReason.None : DeveloperFailureReason.InvalidTarget;
        }
        if (!DeveloperGameplayTuningSchema.TryGet(request.Parameter, out var descriptor)) return DeveloperFailureReason.UnsupportedParameter;
        var value = request.Operation == DeveloperOperation.SetParameter ? request.Values[0] : descriptor.Contract.Default;
        return DeveloperGameplayTuningSchema.Validate(new(request.Parameter, request.Target, value), _initial.Settings.Participants);
    }
    private static bool Same(DeveloperRequest left, DeveloperRequest right) => left.Session == right.Session &&
        left.Correlation == right.Correlation && left.ExpectedRevision == right.ExpectedRevision && left.Operation == right.Operation &&
        left.Target == right.Target && left.Parameter == right.Parameter && left.Values.SequenceEqual(right.Values);
    private DeveloperTuningApplication Reject(DeveloperRequest request, DeveloperFailureReason failure) =>
        new(DeveloperResult.Rejected(request, CaptureContribution().Revision, failure), CaptureContribution(), []);

    /// <summary>Central command execution calls this in InputCommands. No queue, save codec or global history is owned here.</summary>
    public DeveloperTuningApplication ApplyAtTick(SimulationContext context, DeveloperSessionAccess permission, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(request);
        if (!permission.Capability.IsCompiled) return Reject(request, DeveloperFailureReason.CapabilityUnavailable);
        if (!permission.IsActive) return Reject(request, DeveloperFailureReason.SessionNotActivated);
        if (permission.Session != Session || request.Session != Session || !ReferenceEquals(context.Entities, _simulation.Entities))
            return Reject(request, DeveloperFailureReason.StaleSession);
        lock (_completionGate)
            if (_completed.TryGetValue(request.Correlation, out var old))
                return Same(old.Request, request) ? new(old.Application.Result, CaptureContribution(), [], isReplay: true) : Reject(request, DeveloperFailureReason.InvalidValue);
        if (context.Phase != SimulationPhase.InputCommands || context.Tick != _simulation.CurrentTick ||
            context.Tick.Value <= _lastConveyorReadTick.Value || _conveyorOwner is null)
            return Reject(request, DeveloperFailureReason.InvalidApplyMode);
        var prior = CaptureContribution();
        var failure = DeveloperRequestGate.Check(permission, ExecutionContext(prior), request, Supported);
        if (failure == DeveloperFailureReason.None) failure = ValidateRequest(request);
        if (failure != DeveloperFailureReason.None) return Reject(request, failure);
        if (prior.Revision.Value == ulong.MaxValue) return Reject(request, DeveloperFailureReason.Failed);
        var revision = new DeveloperStateRevision(DeveloperRevisionDomain.Parameters, prior.Revision.Value + 1);
        var rows = prior.Values.ToArray();
        var changes = new List<DeveloperTuningChange>();
        var effects = new List<DeveloperAppliedValue>();
        var conveyor = prior.Conveyor;
        bool modified = prior.DeveloperModified;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.Target != request.Target || request.Operation != DeveloperOperation.ResetParameters && row.Id != request.Parameter) continue;
            bool reset = request.Operation != DeveloperOperation.SetParameter;
            var desired = reset ? row.Initial : request.Values[0];
            var next = row.ApplyMode == DeveloperApplyMode.NextTick
                ? row with { Effective = desired }
                : row with { Pending = desired == row.Initial ? null : desired };
            rows[i] = next;
            if (row.ApplyMode == DeveloperApplyMode.NextTick)
            {
                conveyor = new(desired.AsDouble(), revision, context.Tick);
                modified |= desired != row.Effective;
                if (desired != row.Effective) effects.Add(DeveloperAppliedValue.ForParameter(row.Id, row.Target, desired));
            }
            changes.Add(new(row.Id, row.Target, row.Effective, next.Effective, row.Pending, next.Pending, reset, context.Tick, revision));
        }
        if (changes.Count == 0 || changes.Count > DeveloperRequest.MaximumValues) return Reject(request, DeveloperFailureReason.InvalidTarget);
        var pending = rows.Where(r => r.Pending.HasValue).Select(r => new DeveloperTuningOverride(r.Id, r.Target, r.Pending!.Value)).ToArray();
        if (!DeveloperRestartBuilder.TryBuild(_initial.Settings, _initial.InitialOverrides.Metadata, pending, out _, out failure)) return Reject(request, failure);
        var nextState = new DeveloperTuningContribution(prior.InitialOverrides, revision, context.Tick, conveyor, modified, rows);
        bool waiting = changes.Any(c => c.PendingAfter.HasValue);
        var result = waiting ? DeveloperResult.Pending(request, revision) : DeveloperResult.Applied(request, revision, context.Tick, effects.ToArray());
        var application = new DeveloperTuningApplication(result, nextState, changes.ToArray());
        lock (_completionGate)
        {
            if (_completionOrder.Count == 128) _completed.Remove(_completionOrder.Dequeue());
            _completed.Add(request.Correlation, (request, application)); _completionOrder.Enqueue(request.Correlation);
            Volatile.Write(ref _state, nextState);
        }
        return application;
    }
    public bool TryCreateRestartPlan(DeveloperPresetMetadata metadata, out DeveloperRestartPlan? plan, out DeveloperFailureReason failure)
    {
        var state = CaptureContribution();
        var overrides = state.Values.Where(v => (v.Pending ?? v.Effective) != v.Basis)
            .Select(v => new DeveloperTuningOverride(v.Id, v.Target, v.Pending ?? v.Effective)).ToArray();
        return DeveloperRestartBuilder.TryBuild(_initial.BaseSettings, metadata, overrides, out plan, out failure);
    }
}
