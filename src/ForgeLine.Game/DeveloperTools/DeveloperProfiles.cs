using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Data only: identity and declared requirements cannot grant host permission.</summary>
public sealed class DeveloperPresetMetadata
{
    public const int CurrentSchema = 1;
    public DeveloperPresetMetadata(int schema, string compositionKey, ScenarioIdentity baseIdentity, ulong seed,
        MatchScenarioProfile profile, ReadOnlySpan<MatchParticipantConfiguration> participants, bool requiresDeveloperTools)
    {
        var descriptor = compositionKey switch
        {
            CentralDivideScenario.CompositionKey => ScenarioRegistry.CentralDivideDescriptor,
            SplitBasinBattlefield.CompositionKey => ScenarioRegistry.SplitBasinDescriptor,
            _ => throw new ArgumentException("Unknown canonical scenario.", nameof(compositionKey))
        };
        if (schema != CurrentSchema || baseIdentity != descriptor.Identity || !Enum.IsDefined(profile) || participants.Length != 2)
            throw new ArgumentException("Unsupported preset schema, identity, profile or participant count.");
        var copy = participants.ToArray();
        if (copy.Any(p => p is null || !p.Player.IsSpecified || !p.Faction.IsSpecified || p.StartIndex is < 0 or > 1) ||
            copy.Select(p => p.Player).Distinct().Count() != copy.Length || copy.Select(p => p.StartIndex).Distinct().Count() != copy.Length)
            throw new ArgumentException("Preset participants require distinct players and canonical starts.", nameof(participants));
        Schema = schema; CompositionKey = compositionKey; BaseIdentity = baseIdentity; Seed = seed;
        Profile = profile; Participants = Array.AsReadOnly(copy); RequiresDeveloperTools = requiresDeveloperTools;
    }
    public int Schema { get; }
    public string CompositionKey { get; }
    public ScenarioIdentity BaseIdentity { get; }
    public ulong Seed { get; }
    public MatchScenarioProfile Profile { get; }
    public IReadOnlyList<MatchParticipantConfiguration> Participants { get; }
    public bool RequiresDeveloperTools { get; }
}

public sealed class DeveloperInitialProfile
{
    public const int MaximumParameters = 128;
    public DeveloperInitialProfile(DeveloperPresetMetadata metadata, ReadOnlySpan<DeveloperEffectiveParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (parameters.Length > MaximumParameters) throw new ArgumentOutOfRangeException(nameof(parameters));
        var identities = new HashSet<(DeveloperParameterId, DeveloperTarget)>();
        foreach (var parameter in parameters)
            if (!DeveloperParameterCatalog.TryGet(parameter.Id, out var descriptor) || !descriptor.Accepts(parameter.Value, parameter.Target) ||
                parameter.Revision != new DeveloperStateRevision(DeveloperRevisionDomain.Parameters, 0) ||
                parameter.Target.Kind == DeveloperTargetKinds.Player && !metadata.Participants.Any(p => p.Player == parameter.Target.Player) ||
                !identities.Add((parameter.Id, parameter.Target))) throw new ArgumentException("Invalid initial parameter profile.", nameof(parameters));
        Metadata = metadata; Parameters = Array.AsReadOnly(parameters.ToArray());
    }
    public DeveloperPresetMetadata Metadata { get; }
    public IReadOnlyList<DeveloperEffectiveParameter> Parameters { get; }
}

/// <summary>An applied update/reset observation, not a request or authorization token.</summary>
public sealed record DeveloperParameterChange
{
    public DeveloperParameterChange(ulong sequence, Guid correlation, SimulationTick appliedTick,
        DeveloperParameterId parameter, DeveloperTarget target, DeveloperValue before, DeveloperValue after,
        DeveloperStateRevision revision, bool isReset)
    {
        if (sequence == 0 || correlation == Guid.Empty || revision.Domain != DeveloperRevisionDomain.Parameters || revision.Value == 0 ||
            !DeveloperParameterCatalog.TryGet(parameter, out var descriptor) || !descriptor.Accepts(before, target) || !descriptor.Accepts(after, target)) throw new ArgumentException("Invalid applied parameter change.");
        Sequence = sequence; Correlation = correlation; AppliedTick = appliedTick; Parameter = parameter; Target = target;
        Before = before; After = after; Revision = revision; IsReset = isReset;
    }
    public ulong Sequence { get; }
    public Guid Correlation { get; }
    public SimulationTick AppliedTick { get; }
    public DeveloperParameterId Parameter { get; }
    public DeveloperTarget Target { get; }
    public DeveloperValue Before { get; }
    public DeveloperValue After { get; }
    public DeveloperStateRevision Revision { get; }
    public bool IsReset { get; }
}

/// <summary>Projection of actual applied changes; the runtime owner supplies its persisted ordered history.</summary>
public sealed class DeveloperModificationState
{
    private DeveloperModificationState(ulong sequence, ulong revision, bool modified)
    { LastSequence = sequence; LastRevision = revision; DeveloperModified = modified; }
    public static DeveloperModificationState Unmodified { get; } = new(0, 0, false);
    public ulong LastSequence { get; }
    public ulong LastRevision { get; }
    public bool DeveloperModified { get; }
    public DeveloperModificationState AfterApplied(DeveloperParameterChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (LastSequence == ulong.MaxValue || change.Sequence != LastSequence + 1 || change.Revision.Value <= LastRevision)
            throw new InvalidOperationException("Applied history must be ordered and revisions must advance; reset never erases history.");
        _ = DeveloperParameterCatalog.TryGet(change.Parameter, out var descriptor);
        return new(change.Sequence, change.Revision.Value, DeveloperModified ||
            descriptor.ApplyMode != DeveloperApplyMode.Presentation && change.Before != change.After);
    }
}
