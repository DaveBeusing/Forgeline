using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum DeveloperOperation : byte
{
    SetParameter = 1, ResetParameter, ResetParameters, Pause, Resume, SingleStep, SetHostSpeed,
    StartMatch, ApplyPreset, SpawnUnit, GrantResource, RepairEntity, RefillUnit, DamageEntity
}
public enum DeveloperRevisionDomain : byte { Presentation = 1, Parameters, Session, Simulation }
public readonly record struct DeveloperStateRevision(DeveloperRevisionDomain Domain, ulong Value)
{
    public bool IsValid => Enum.IsDefined(Domain);
}
public readonly record struct DeveloperTarget
{
    private DeveloperTarget(DeveloperTargetKinds kind, PlayerId player, EntityId entity)
    { Kind = kind; Player = player; Entity = entity; }
    public DeveloperTargetKinds Kind { get; }
    public PlayerId Player { get; }
    public EntityId Entity { get; }
    public static DeveloperTarget Global => new(DeveloperTargetKinds.Global, default, default);
    public static DeveloperTarget ForPlayer(PlayerId player) => player.IsSpecified
        ? new(DeveloperTargetKinds.Player, player, default) : throw new ArgumentOutOfRangeException(nameof(player));
    public static DeveloperTarget ForEntity(EntityId entity) => entity.IsValid
        ? new(DeveloperTargetKinds.Entity, default, entity) : throw new ArgumentOutOfRangeException(nameof(entity));
    public bool IsValid => Kind switch
    {
        DeveloperTargetKinds.Global => !Player.IsSpecified && !Entity.IsValid,
        DeveloperTargetKinds.Player => Player.IsSpecified && !Entity.IsValid,
        DeveloperTargetKinds.Entity => Entity.IsValid && !Player.IsSpecified,
        _ => false
    };
}

public sealed class DeveloperRequest
{
    public const int MaximumValues = 16;
    public DeveloperRequest(SimulationSessionId session, Guid correlation, DeveloperStateRevision expectedRevision,
        DeveloperOperation operation, DeveloperTarget target, DeveloperParameterId parameter, ReadOnlySpan<DeveloperValue> values)
    {
        if (!session.IsSpecified || correlation == Guid.Empty || !expectedRevision.IsValid || !target.IsValid ||
            values.Length > MaximumValues) throw new ArgumentException("Invalid or oversized developer request.");
        foreach (var value in values) if (!value.IsValid) throw new ArgumentException("Request values must be finite and typed.", nameof(values));
        Session = session; Correlation = correlation; ExpectedRevision = expectedRevision;
        Operation = operation; Target = target; Parameter = parameter; Values = Array.AsReadOnly(values.ToArray());
    }
    public SimulationSessionId Session { get; }
    public Guid Correlation { get; }
    public DeveloperStateRevision ExpectedRevision { get; }
    public DeveloperOperation Operation { get; }
    public DeveloperTarget Target { get; }
    public DeveloperParameterId Parameter { get; }
    public IReadOnlyList<DeveloperValue> Values { get; }
}

public enum DeveloperRequestStatus : byte { Accepted = 1, Pending, Applied, Rejected }
public enum DeveloperFailureReason : byte
{
    None, CapabilityUnavailable, SessionNotActivated, StaleSession, StaleRevision, InvalidTarget,
    UnsupportedOperation, UnsupportedParameter, InvalidValue, InvalidApplyMode, ProviderUnavailable, Cancelled, Failed, BoundaryFull, InvalidInventoryRole, PositionBlocked, TerminalSession
}
public readonly record struct DeveloperEffectiveParameter(DeveloperParameterId Id, DeveloperTarget Target,
    DeveloperValue Value, DeveloperStateRevision Revision);

public sealed class DeveloperResult
{
    private DeveloperResult(DeveloperRequest request, DeveloperRequestStatus status, DeveloperStateRevision revision,
        SimulationTick? tick, DeveloperFailureReason failure, ReadOnlySpan<DeveloperAppliedValue> values)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!revision.IsValid || !Enum.IsDefined(status) || !Enum.IsDefined(failure) ||
            (status == DeveloperRequestStatus.Applied) != tick.HasValue ||
            (status == DeveloperRequestStatus.Rejected) != (failure != DeveloperFailureReason.None) || values.Length > DeveloperRequest.MaximumValues ||
            status != DeveloperRequestStatus.Applied && !values.IsEmpty) throw new ArgumentException("Inconsistent developer result.");
        foreach (var value in values)
            if (!value.IsValid)
                throw new ArgumentException("Invalid effective parameter.", nameof(values));
        Session = request.Session; Correlation = request.Correlation; Status = status; Revision = revision;
        AppliedTick = tick; Failure = failure; AffectedValues = Array.AsReadOnly(values.ToArray());
    }
    public SimulationSessionId Session { get; }
    public Guid Correlation { get; }
    public DeveloperRequestStatus Status { get; }
    public DeveloperStateRevision Revision { get; }
    public SimulationTick? AppliedTick { get; }
    public DeveloperFailureReason Failure { get; }
    public IReadOnlyList<DeveloperAppliedValue> AffectedValues { get; }
    public static DeveloperResult Accepted(DeveloperRequest request, DeveloperStateRevision revision) => new(request, DeveloperRequestStatus.Accepted, revision, null, DeveloperFailureReason.None, []);
    public static DeveloperResult Pending(DeveloperRequest request, DeveloperStateRevision revision) => new(request, DeveloperRequestStatus.Pending, revision, null, DeveloperFailureReason.None, []);
    public static DeveloperResult Applied(DeveloperRequest request, DeveloperStateRevision revision, SimulationTick tick, ReadOnlySpan<DeveloperAppliedValue> values) => new(request, DeveloperRequestStatus.Applied, revision, tick, DeveloperFailureReason.None, values);
    public static DeveloperResult Rejected(DeveloperRequest request, DeveloperStateRevision revision, DeveloperFailureReason failure) => new(request, DeveloperRequestStatus.Rejected, revision, null, failure, []);
}

/// <summary>Host-owned current facts; execute-time callers must recapture them on the command boundary.</summary>
public sealed record DeveloperExecutionContext(SimulationSessionId Session, DeveloperStateRevision Revision,
    Func<PlayerId, bool> IsPlayerPresent, Func<EntityId, bool> IsEntityAlive);

public static class DeveloperRequestGate
{
    /// <summary>Mandatory both before enqueue and at execution; this never authorizes a handler absent from its registry.</summary>
    public static DeveloperFailureReason Check(DeveloperSessionAccess permission, DeveloperExecutionContext context,
        DeveloperRequest request, IReadOnlySet<DeveloperOperation> supported)
    {
        ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(supported);
        if (!permission.Capability.IsCompiled) return DeveloperFailureReason.CapabilityUnavailable;
        if (!permission.IsActive) return DeveloperFailureReason.SessionNotActivated;
        if (!context.Session.IsSpecified || request.Session != context.Session || permission.Session != context.Session) return DeveloperFailureReason.StaleSession;
        if (request.ExpectedRevision != context.Revision) return DeveloperFailureReason.StaleRevision;
        if (!Enum.IsDefined(request.Operation) || !supported.Contains(request.Operation)) return DeveloperFailureReason.UnsupportedOperation;
        if (!request.Target.IsValid || request.Target.Kind == DeveloperTargetKinds.Entity && !context.IsEntityAlive(request.Target.Entity) ||
            request.Target.Kind == DeveloperTargetKinds.Player && !context.IsPlayerPresent(request.Target.Player)) return DeveloperFailureReason.InvalidTarget;
        if (request.Operation is DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter)
        {
            if (!DeveloperParameterCatalog.TryGet(request.Parameter, out var descriptor)) return DeveloperFailureReason.UnsupportedParameter;
            if (request.ExpectedRevision.Domain != (descriptor.ApplyMode == DeveloperApplyMode.Presentation ? DeveloperRevisionDomain.Presentation : DeveloperRevisionDomain.Parameters)) return DeveloperFailureReason.InvalidApplyMode;
            if (request.Operation == DeveloperOperation.SetParameter && (request.Values.Count != 1 || !descriptor.Accepts(request.Values[0], request.Target)) ||
                request.Operation == DeveloperOperation.ResetParameter && (request.Values.Count != 0 || !descriptor.Accepts(descriptor.Default, request.Target))) return DeveloperFailureReason.InvalidValue;
        }
        else if (request.Parameter != DeveloperParameterId.None) return DeveloperFailureReason.UnsupportedParameter;
        DeveloperRevisionDomain expectedDomain = request.Operation switch
        {
            DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter => request.ExpectedRevision.Domain,
            DeveloperOperation.ResetParameters => DeveloperRevisionDomain.Parameters,
            DeveloperOperation.SpawnUnit or DeveloperOperation.GrantResource => DeveloperRevisionDomain.Simulation,
            _ => DeveloperRevisionDomain.Session
        };
        if (request.ExpectedRevision.Domain != expectedDomain) return DeveloperFailureReason.InvalidApplyMode;
        return DeveloperFailureReason.None;
    }
}
