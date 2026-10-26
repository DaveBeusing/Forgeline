using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Finite, explicit wire data. Reading it never supplies execution capability.</summary>
public readonly record struct DeveloperStoredValue(DeveloperValueKind Kind, double Number)
{
    public static DeveloperStoredValue From(DeveloperValue value) => new(value.Kind, value.Kind switch
    {
        DeveloperValueKind.Boolean => value.AsBoolean() ? 1 : 0,
        DeveloperValueKind.SignedNumber => value.AsInt32(), DeveloperValueKind.UnsignedNumber => value.AsUInt32(),
        DeveloperValueKind.RealNumber => value.AsDouble(), _ => throw new ArgumentException("Invalid developer value.")
    });
    public DeveloperValue ToValue()
    {
        if (!double.IsFinite(Number)) throw new ArgumentException("Nonfinite developer wire value.");
        return Kind switch
        {
            DeveloperValueKind.Boolean when Number is 0 or 1 => DeveloperValue.FromBoolean(Number != 0),
            DeveloperValueKind.SignedNumber when Number == Math.Truncate(Number) && Number is >= int.MinValue and <= int.MaxValue => DeveloperValue.FromInt32((int)Number),
            DeveloperValueKind.UnsignedNumber when Number == Math.Truncate(Number) && Number is >= uint.MinValue and <= uint.MaxValue => DeveloperValue.FromUInt32((uint)Number),
            DeveloperValueKind.RealNumber => DeveloperValue.FromDouble(Number),
            _ => throw new ArgumentException("Invalid developer wire kind or range.")
        };
    }
}
public readonly record struct DeveloperStoredTarget(DeveloperTargetKinds Kind, ulong Player, EntityId Entity)
{
    public static DeveloperStoredTarget From(DeveloperTarget target) => new(target.Kind, target.Player.Value, target.Entity);
    public DeveloperTarget ToTarget() => Kind switch
    {
        DeveloperTargetKinds.Global when Player == 0 && !Entity.IsValid => DeveloperTarget.Global,
        DeveloperTargetKinds.Player when Player != 0 && !Entity.IsValid => DeveloperTarget.ForPlayer(new(Player)),
        DeveloperTargetKinds.Entity when Player == 0 && Entity.IsValid => DeveloperTarget.ForEntity(Entity),
        _ => throw new ArgumentException("Invalid developer wire target.")
    };
}
public sealed record DeveloperCommandPayload
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public ulong Session { get; init; }
    public Guid Correlation { get; init; }
    public DeveloperStateRevision ExpectedRevision { get; init; }
    public DeveloperOperation Operation { get; init; }
    public DeveloperStoredTarget Target { get; init; }
    public DeveloperParameterId Parameter { get; init; }
    public DeveloperStoredValue[] Values { get; init; } = [];
    // Null means still queued. The approved recovery host reproduces the recorded execution gate, not a file-granted permission.
    public bool? ExecutionActivated { get; init; }
    public static DeveloperCommandPayload Capture(DeveloperRequest request) => new()
    {
        Session = request.Session.Value, Correlation = request.Correlation, ExpectedRevision = request.ExpectedRevision,
        Operation = request.Operation, Target = DeveloperStoredTarget.From(request.Target), Parameter = request.Parameter,
        Values = request.Values.Select(DeveloperStoredValue.From).ToArray()
    };
    public DeveloperRequest ToRequest(SimulationSessionId? reconstructedSession = null)
    {
        if (SchemaVersion != CurrentSchema || Session == 0 || Values is null || Values.Length > DeveloperRequest.MaximumValues ||
            !Enum.IsDefined(Operation)) throw new ArgumentException("Invalid developer command payload.");
        return new(reconstructedSession ?? new(Session), Correlation, ExpectedRevision, Operation, Target.ToTarget(), Parameter,
            Values.Select(v => v.ToValue()).ToArray());
    }
    internal DeveloperCommandPayload Copy() => this with { Values = Values.ToArray() };
}
public sealed record DeveloperInitialParameterData(DeveloperParameterId Id, DeveloperStoredTarget Target, DeveloperStoredValue Value);
public sealed record DeveloperInitialProfileData
{
    public int SchemaVersion { get; init; } = 1;
    public string CompositionKey { get; init; } = "";
    public ScenarioIdentity BaseIdentity { get; init; } = null!;
    public ulong Seed { get; init; }
    public MatchScenarioProfile Profile { get; init; }
    public MatchParticipantConfiguration[] Participants { get; init; } = [];
    public bool RequiresDeveloperTools { get; init; }
    public DeveloperInitialParameterData[] Parameters { get; init; } = [];
    public static DeveloperInitialProfileData Capture(DeveloperInitialProfile profile) => new()
    {
        CompositionKey = profile.Metadata.CompositionKey, BaseIdentity = profile.Metadata.BaseIdentity, Seed = profile.Metadata.Seed,
        Profile = profile.Metadata.Profile, Participants = profile.Metadata.Participants.ToArray(), RequiresDeveloperTools = profile.Metadata.RequiresDeveloperTools,
        Parameters = profile.Parameters.Select(p => new DeveloperInitialParameterData(p.Id, DeveloperStoredTarget.From(p.Target), DeveloperStoredValue.From(p.Value))).ToArray()
    };
    public DeveloperInitialProfile ToProfile()
    {
        if (SchemaVersion != 1 || Parameters is null || Parameters.Length > DeveloperInitialProfile.MaximumParameters || Participants is null)
            throw new ArgumentException("Invalid initial developer profile.");
        var metadata = new DeveloperPresetMetadata(1, CompositionKey, BaseIdentity, Seed, Profile, Participants, RequiresDeveloperTools);
        return new(metadata, Parameters.Select(p => new DeveloperEffectiveParameter(p.Id, p.Target.ToTarget(), p.Value.ToValue(),
            new(DeveloperRevisionDomain.Parameters, 0))).ToArray());
    }
}
internal interface IDeveloperRecordedCommand
{
    DeveloperCommandPayload CapturePayload();
}
