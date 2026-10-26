using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed partial class DeveloperCommandGateway : IDeveloperActionDispatcher, IDeveloperMenuCatalog, ISimulationTickObserver
{
    public const int MaximumPending = 128;
    public const int MaximumResults = 64;
    public const int MaximumParameterChanges = DeveloperParameterAuditData.MaximumEntries;
    private static readonly HashSet<DeveloperOperation> Supported = [DeveloperOperation.GrantResource, DeveloperOperation.RepairEntity,
        DeveloperOperation.RefillUnit, DeveloperOperation.SpawnUnit, DeveloperOperation.DamageEntity,
        DeveloperOperation.SetParameter, DeveloperOperation.ResetParameter, DeveloperOperation.ResetParameters];
    private readonly MatchRuntime _runtime;
    private readonly DeveloperGameplayTuningProvider? _tuning;
    private DeveloperSessionAccess _access;
    private readonly object _resultGate = new();
    private readonly Queue<DeveloperResult> _results = new();
    private readonly Dictionary<Guid, (DeveloperRequest Request, DeveloperResult Result)> _completed = new();
    private readonly Queue<Guid> _completionOrder = new();
    private readonly Dictionary<Guid, (DeveloperRequest Request, DeveloperResult Receipt)> _pending = new();
    private readonly List<DeveloperParameterAuditData> _parameterChanges = new();
    private DeveloperRequest? _damageRequest;
    private int _ownerThread;
    private SimulationTick _lastCompletedTick;
    private ulong _revision, _historySequence;
    private string _historyHash = "";
    private bool _modified;
    internal DeveloperCommandGateway(MatchRuntime runtime, DeveloperRestartPlan? plan)
    {
        _runtime = runtime; _access = DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, false);
        if (plan is not null)
        {
            _tuning = new(runtime.Simulation, plan, this);
            runtime.Services.ConveyorFlow.BindDeveloperTuning(_tuning);
        }
        runtime.Simulation.RegisterTickObserver(this);
        runtime.Services.CombatRuntime.DeveloperDamageApplied += OnDamageApplied;
    }
    public DeveloperProviderStatus Status => Volatile.Read(ref _access).IsActive
        ? new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None)
        : new(DeveloperProviderAvailability.Unavailable, DeveloperFailureReason.SessionNotActivated);
    public IDeveloperParameterProvider Parameters => _tuning ?? (IDeveloperParameterProvider)MissingDeveloperProvider.Instance;
    public DeveloperStateRevision Revision => new(DeveloperRevisionDomain.Simulation, Volatile.Read(ref _revision));
    public bool DeveloperModified => Volatile.Read(ref _modified) || _tuning?.CaptureContribution().DeveloperModified == true;
    public bool HasAuthoritativeState => _historySequence != 0 || _tuning?.CaptureContribution().InitialOverrides.Parameters.Count > 0;
    public int PendingCount { get { lock (_resultGate) return _pending.Count; } }
    public IReadOnlyList<DeveloperOperationDescriptor> Operations { get; } = Array.AsReadOnly(new[]
    {
        new DeveloperOperationDescriptor(DeveloperOperation.GrantResource, DeveloperTargetKinds.Entity, "Add resource"),
        new DeveloperOperationDescriptor(DeveloperOperation.RepairEntity, DeveloperTargetKinds.Entity, "Repair health"),
        new DeveloperOperationDescriptor(DeveloperOperation.RefillUnit, DeveloperTargetKinds.Entity, "Refill Fuel / Ammunition"),
        new DeveloperOperationDescriptor(DeveloperOperation.SpawnUnit, DeveloperTargetKinds.Player, "Spawn one registered unit"),
        new DeveloperOperationDescriptor(DeveloperOperation.DamageEntity, DeveloperTargetKinds.Entity, "Apply combat damage")
    });
    /// <summary>The transport owner binds its actual execution thread and ephemeral permission. No settings/file creates this binding.</summary>
    public void BindAccess(DeveloperSessionAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (access.Session != _runtime.Simulation.SessionId || !access.Capability.IsCompiled)
            throw new ArgumentException("A compiled host must bind the actual runtime session.", nameof(access));
        _ownerThread = Environment.CurrentManagedThreadId; Volatile.Write(ref _access, access);
    }
    private DeveloperStateRevision RequestRevision(DeveloperRequest request) => request.Operation is
        DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter or DeveloperOperation.ResetParameters
        ? _tuning?.CaptureContribution().Revision ?? new(DeveloperRevisionDomain.Parameters, 0) : Revision;
    private DeveloperExecutionContext Context(DeveloperRequest request) => new(_runtime.Simulation.SessionId, RequestRevision(request),
        player => _runtime.MatchConfiguration.Participants.Any(p => p.Player == player), _runtime.Simulation.Entities.IsAlive);
    private static bool Same(DeveloperRequest a, DeveloperRequest b) => a.Session == b.Session && a.Correlation == b.Correlation &&
        a.Operation == b.Operation && a.ExpectedRevision == b.ExpectedRevision && a.Target == b.Target && a.Parameter == b.Parameter && a.Values.SequenceEqual(b.Values);
    public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        DeveloperResult Reject(DeveloperFailureReason failure) => DeveloperResult.Rejected(request, RequestRevision(request), failure);
        if (!permission.Capability.IsCompiled) return Reject(DeveloperFailureReason.CapabilityUnavailable);
        if (!permission.IsActive || !_access.IsActive) return Reject(DeveloperFailureReason.SessionNotActivated);
        if (_ownerThread != Environment.CurrentManagedThreadId) return Reject(DeveloperFailureReason.InvalidApplyMode);
        if (permission.Session != _runtime.Simulation.SessionId || context.Session != permission.Session || request.Session != permission.Session)
            return Reject(DeveloperFailureReason.StaleSession);
        lock (_resultGate)
        {
            if (_completed.TryGetValue(request.Correlation, out var complete)) return Same(complete.Request, request) ? complete.Result : Reject(DeveloperFailureReason.InvalidValue);
            if (_pending.TryGetValue(request.Correlation, out var pending)) return Same(pending.Request, request) ? pending.Receipt : Reject(DeveloperFailureReason.InvalidValue);
        }
        var failure = DeveloperRequestGate.Check(permission, Context(request), request, Supported);
        if (failure == DeveloperFailureReason.None && context.Revision != RequestRevision(request)) failure = DeveloperFailureReason.StaleRevision;
        if (failure == DeveloperFailureReason.None && _runtime.GetMatchState().IsTerminal) failure = DeveloperFailureReason.TerminalSession;
        if (failure == DeveloperFailureReason.None) failure = ValidateRequest(request);
        if (failure != DeveloperFailureReason.None) return Reject(failure);
        if (_pending.Count >= MaximumPending || _runtime.Simulation.PendingCommandCount >= MaximumPending) return Reject(DeveloperFailureReason.BoundaryFull);
        var receipt = _runtime.GetMatchState().Lifecycle == MatchLifecyclePhase.Paused
            ? DeveloperResult.Pending(request, RequestRevision(request)) : DeveloperResult.Accepted(request, RequestRevision(request));
        var command = new DeveloperSimulationCommand(this, DeveloperCommandPayload.Capture(request), request, tracked: true);
        lock (_resultGate) _pending.Add(request.Correlation, (request, receipt));
        try { _runtime.Simulation.SubmitCommand(command, _runtime.Simulation.CurrentTick.Next()); }
        catch { lock (_resultGate) _pending.Remove(request.Correlation); throw; }
        return receipt;
    }
    public IReadOnlyList<DeveloperResult> CaptureResults() { lock (_resultGate) return Array.AsReadOnly(_results.ToArray()); }
    public IReadOnlyList<DeveloperParameterAuditData> CaptureParameterChanges() { lock (_resultGate) return Array.AsReadOnly(_parameterChanges.ToArray()); }
    private DeveloperFailureReason ValidateRequest(DeveloperRequest request)
    {
        if (request.Operation is DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter or DeveloperOperation.ResetParameters)
        {
            if (_tuning is null) return DeveloperFailureReason.ProviderUnavailable;
            if (request.Operation == DeveloperOperation.ResetParameters)
                return request.Values.Count != 0 ? DeveloperFailureReason.InvalidValue :
                    request.Target.Kind == DeveloperTargetKinds.Global || request.Target.Kind == DeveloperTargetKinds.Player &&
                    _runtime.RuntimeSettings.Participants.Any(p => p.Player == request.Target.Player && p.IsComputerControlled)
                        ? DeveloperFailureReason.None : DeveloperFailureReason.InvalidTarget;
            if (!DeveloperGameplayTuningSchema.TryGet(request.Parameter, out var descriptor)) return DeveloperFailureReason.UnsupportedParameter;
            return DeveloperGameplayTuningSchema.Validate(new(request.Parameter, request.Target,
                request.Operation == DeveloperOperation.SetParameter ? request.Values[0] : descriptor.Contract.Default), _runtime.RuntimeSettings.Participants);
        }
        return TryPlan(request, out _, out var failure) ? DeveloperFailureReason.None : failure;
    }
    internal void Apply(DeveloperSimulationCommand command, SimulationContext context)
    {
        var request = command.Request;
        command.MarkExecution(_access.IsActive);
        lock (_resultGate) if (command.Tracked) _pending.Remove(request.Correlation);
        var failure = command.RecordedActivation == false || !_access.IsActive ? DeveloperFailureReason.SessionNotActivated : DeveloperFailureReason.None;
        if (failure == DeveloperFailureReason.None && (request.Session != _runtime.Simulation.SessionId || !ReferenceEquals(context.Entities, _runtime.Simulation.Entities)))
            failure = DeveloperFailureReason.StaleSession;
        if (failure == DeveloperFailureReason.None && (_ownerThread != Environment.CurrentManagedThreadId || context.Phase != SimulationPhase.InputCommands ||
            context.Tick != _runtime.Simulation.CurrentTick || context.Tick.Value <= _lastCompletedTick.Value)) failure = DeveloperFailureReason.InvalidApplyMode;
        if (failure != DeveloperFailureReason.None) { Complete(request, DeveloperResult.Rejected(request, RequestRevision(request), failure)); return; }
        lock (_resultGate)
            if (_completed.TryGetValue(request.Correlation, out var cached))
            { Publish(Same(cached.Request, request) ? cached.Result : DeveloperResult.Rejected(request, RequestRevision(request), DeveloperFailureReason.InvalidValue)); return; }
        failure = DeveloperRequestGate.Check(_access, Context(request), request, Supported);
        if (failure == DeveloperFailureReason.None && _runtime.GetMatchState().IsTerminal) failure = DeveloperFailureReason.TerminalSession;
        if (failure == DeveloperFailureReason.None) failure = ValidateRequest(request);
        if (failure != DeveloperFailureReason.None) { Complete(request, DeveloperResult.Rejected(request, RequestRevision(request), failure)); return; }
        if (request.Operation is DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter or DeveloperOperation.ResetParameters)
        {
            int auditEntries = _tuning!.CaptureContribution().Values.Count(row => row.Target == request.Target &&
                (request.Operation == DeveloperOperation.ResetParameters || row.Id == request.Parameter));
            lock (_resultGate)
                if (auditEntries > MaximumParameterChanges - _parameterChanges.Count)
                { Complete(request, DeveloperResult.Rejected(request, RequestRevision(request), DeveloperFailureReason.BoundaryFull)); return; }
            var application = _tuning!.ApplyAtTick(context, _access, request);
            if (!application.IsReplay && application.Changes.Count > 0)
            {
                RecordHistory(new { Operation = request.Operation, request.Correlation, Tick = context.Tick.Value,
                    Changes = application.Changes.Select(DeveloperParameterAuditData.FromChange).ToArray() });
                lock (_resultGate) foreach (var change in application.Changes) _parameterChanges.Add(DeveloperParameterAuditData.FromChange(change) with
                    { Sequence = _historySequence, Correlation = request.Correlation });
            }
            Complete(request, application.Result); return;
        }
        if (_damageRequest is not null || _revision == ulong.MaxValue)
        { Complete(request, DeveloperResult.Rejected(request, Revision, DeveloperFailureReason.InvalidApplyMode)); return; }
        ExecuteDomain(request, context);
    }
    private void Complete(DeveloperRequest request, DeveloperResult result)
    {
        lock (_resultGate)
        {
            if (!_completed.ContainsKey(request.Correlation))
            {
                if (_completionOrder.Count == 128) _completed.Remove(_completionOrder.Dequeue());
                _completionOrder.Enqueue(request.Correlation); _completed.Add(request.Correlation, (request, result));
            }
            Publish(result);
        }
    }
    private void Publish(DeveloperResult result)
    { lock (_resultGate) { if (_results.Count == MaximumResults) _results.Dequeue(); _results.Enqueue(result); } }
    private void Applied(DeveloperRequest request, SimulationTick tick, DeveloperAppliedValue[] effects)
    {
        bool changed = effects.Any(e => e.Kind == DeveloperEffectKind.SpawnedEntity || e.Value.Kind == DeveloperValueKind.RealNumber && e.Value.AsDouble() > 0);
        if (changed)
        {
            _revision++; _modified = true;
            RecordHistory(new { request.Operation, request.Correlation, Tick = tick.Value, Revision = _revision,
                Effects = effects.Select(e => new { e.Kind, Target = DeveloperStoredTarget.From(e.Target), e.Parameter, e.Resource, Value = DeveloperStoredValue.From(e.Value) }).ToArray() });
        }
        Complete(request, DeveloperResult.Applied(request, Revision, tick, effects));
    }
    private void RecordHistory<T>(T entry)
    {
        if (_historySequence == ulong.MaxValue) throw new InvalidOperationException("Developer history exhausted.");
        _historySequence++;
        _historyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_historyHash + "\n" + JsonSerializer.Serialize(entry))));
    }
    public void OnTickCompleted(SimulationContext context)
    {
        _lastCompletedTick = context.Tick;
        if (_damageRequest is { } request)
        {
            _damageRequest = null;
            if (context.Entities.IsAlive(request.Target.Entity)) Applied(request, context.Tick, [DeveloperAppliedValue.ForHealth(DeveloperEffectKind.HealthDamage, request.Target.Entity, 0)]);
            else Complete(request, DeveloperResult.Rejected(request, Revision, DeveloperFailureReason.InvalidTarget));
        }
    }
    internal object CaptureAuthoritative()
    {
        var tuning = _tuning?.CaptureContribution();
        return new
        {
            SchemaVersion = 1, SimulationRevision = _revision, DeveloperModified, Sequence = _historySequence, HistorySha256 = _historyHash,
            Initial = tuning is null ? null : DeveloperInitialProfileData.Capture(tuning.InitialOverrides),
            ParametersRevision = tuning?.Revision.Value ?? 0, RecordedTick = tuning?.RecordedTick.Value ?? 0,
            Conveyor = tuning is null ? null : new { tuning.Conveyor.Multiplier, Revision = tuning.Conveyor.Revision.Value, AppliedTick = tuning.Conveyor.AppliedTick.Value },
            Values = tuning?.Values.Select(v => new { v.Id, Target = DeveloperStoredTarget.From(v.Target), Basis = DeveloperStoredValue.From(v.Basis),
                Initial = DeveloperStoredValue.From(v.Initial), Effective = DeveloperStoredValue.From(v.Effective),
                Pending = v.Pending.HasValue ? DeveloperStoredValue.From(v.Pending.Value) : (DeveloperStoredValue?)null, v.ApplyMode }).ToArray()
        };
    }
    internal ISimulationCommand ReconstructCommand(DeveloperCommandPayload payload)
    {
        if (!DeveloperToolsCapability.Current.IsCompiled || !_access.IsActive || _ownerThread != Environment.CurrentManagedThreadId)
            throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion, "An approved developer recovery owner is required.");
        var request = payload.ToRequest(_runtime.Simulation.SessionId);
        bool tracked = payload.ExecutionActivated is null;
        if (tracked)
        {
            lock (_resultGate)
            {
                if (_pending.Count == MaximumPending || !_pending.TryAdd(request.Correlation, (request, DeveloperResult.Pending(request, request.ExpectedRevision))))
                    throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Too many or duplicate pending developer commands.");
            }
        }
        return new DeveloperSimulationCommand(this, payload.Copy(), request, tracked);
    }
    internal void Dispose()
    {
        _runtime.Services.CombatRuntime.DeveloperDamageApplied -= OnDamageApplied;
        Volatile.Write(ref _access, DeveloperToolsCapability.Current.BindSession(_runtime.Simulation.SessionId, false));
        _ownerThread = 0;
    }
}

internal sealed class DeveloperSimulationCommand(DeveloperCommandGateway gateway, DeveloperCommandPayload payload,
    DeveloperRequest request, bool tracked = false) : ISimulationCommand, IDeveloperRecordedCommand
{
    private bool? _activation = payload.ExecutionActivated;
    internal DeveloperRequest Request { get; } = request;
    internal bool Tracked { get; } = tracked;
    internal bool? RecordedActivation => _activation;
    internal void MarkExecution(bool activated) => _activation ??= activated;
    public DeveloperCommandPayload CapturePayload() => payload.Copy() with { ExecutionActivated = _activation };
    public void Execute(SimulationContext context) => gateway.Apply(this, context);
}
