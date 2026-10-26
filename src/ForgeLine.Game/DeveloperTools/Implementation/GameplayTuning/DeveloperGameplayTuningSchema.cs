using System.Collections.ObjectModel;

namespace ForgeLine.Game;

public readonly record struct DeveloperTuningOverride(DeveloperParameterId Id, DeveloperTarget Target, DeveloperValue Value);
public sealed record DeveloperTuningDescriptor(string DisplayName, DeveloperParameterDescriptor Contract);

/// <summary>The executable editor subset of the shared metadata registry. No alternative request protocol.</summary>
public static class DeveloperGameplayTuningSchema
{
    public static IReadOnlyList<DeveloperTuningDescriptor> Entries { get; } = Create();
    private static readonly Dictionary<DeveloperParameterId, DeveloperTuningDescriptor> ById = Entries.ToDictionary(e => e.Contract.Id);
    public static bool TryGet(DeveloperParameterId id, out DeveloperTuningDescriptor descriptor) => ById.TryGetValue(id, out descriptor!);
    public static DeveloperFailureReason Validate(DeveloperTuningOverride value, IReadOnlyList<MatchParticipantConfiguration> participants)
    {
        if (!TryGet(value.Id, out var entry)) return DeveloperFailureReason.UnsupportedParameter;
        if (!value.Target.IsValid || value.Target.Kind == DeveloperTargetKinds.Entity ||
            (entry.Contract.Targets & value.Target.Kind) == 0 || value.Target.Kind == DeveloperTargetKinds.Player &&
            !participants.Any(p => p.Player == value.Target.Player && p.IsComputerControlled)) return DeveloperFailureReason.InvalidTarget;
        if (!entry.Contract.Accepts(value.Value, value.Target)) return DeveloperFailureReason.InvalidValue;
        if (value.Id == DeveloperParameterId.ConveyorThroughputMultiplier && value.Value.AsDouble() * 4 != Math.Truncate(value.Value.AsDouble() * 4))
            return DeveloperFailureReason.InvalidValue;
        return DeveloperFailureReason.None;
    }
    internal static MatchScenarioSettings Copy(MatchScenarioSettings source) => source with
    {
        OpponentConfigurations = new ReadOnlyDictionary<ulong, SkirmishOpponentConfiguration>(source.OpponentConfigurations
            .ToDictionary(p => p.Key, p => p.Value with { Raids = p.Value.Raids with { } }))
    };
    internal static DeveloperValue Read(MatchScenarioSettings settings, DeveloperParameterId id, DeveloperTarget target, double multiplier = 1)
    {
        if (id == DeveloperParameterId.ConveyorThroughputMultiplier) return DeveloperValue.FromDouble(multiplier);
        if (id == DeveloperParameterId.DistributionRetryDelay) return DeveloperValue.FromUInt32(checked((uint)settings.DistributionRetryDelayTicks));
        if (id == DeveloperParameterId.DistributionMaximumAttempts) return DeveloperValue.FromUInt32(settings.DistributionMaximumTransportAttempts);
        if (id == DeveloperParameterId.DistributionFairnessAging) return DeveloperValue.FromUInt32(checked((uint)settings.DistributionFairnessAgingTicks));
        var policy = settings.OpponentConfigurations[target.Player.Value];
        return id switch
        {
            DeveloperParameterId.OpponentAggression => DeveloperValue.FromDouble(policy.Aggression),
            DeveloperParameterId.OpponentExpansionReadiness => DeveloperValue.FromDouble(policy.ExpansionReadinessThreshold),
            DeveloperParameterId.OpponentOffensiveReadiness => DeveloperValue.FromDouble(policy.OffensiveReadinessThreshold),
            DeveloperParameterId.OpponentRetreat => DeveloperValue.FromDouble(policy.RetreatThreshold),
            DeveloperParameterId.OpponentResupply => DeveloperValue.FromDouble(policy.ResupplyThreshold),
            DeveloperParameterId.OpponentOffensiveFuel => DeveloperValue.FromDouble(policy.OffensiveFuelThreshold),
            DeveloperParameterId.OpponentReactionCadence => DeveloperValue.FromUInt32(policy.ReactionCadenceTicks),
            DeveloperParameterId.RaidEnabled => DeveloperValue.FromBoolean(policy.Raids.Enabled),
            DeveloperParameterId.RaidCooldownTicks => DeveloperValue.FromUInt32(policy.Raids.CooldownTicks),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
    internal static DeveloperTuningOverride[] Capture(MatchRuntimeSettings settings, double multiplier)
    {
        var values = new List<DeveloperTuningOverride>();
        foreach (var entry in Entries)
        {
            if (entry.Contract.Targets == DeveloperTargetKinds.Global)
                values.Add(new(entry.Contract.Id, DeveloperTarget.Global, Read(settings.Scenario, entry.Contract.Id, DeveloperTarget.Global, multiplier)));
            else foreach (var player in settings.Participants.Where(p => p.IsComputerControlled).OrderBy(p => p.Player.Value))
            {
                var target = DeveloperTarget.ForPlayer(player.Player);
                values.Add(new(entry.Contract.Id, target, Read(settings.Scenario, entry.Contract.Id, target)));
            }
        }
        if (values.Count > DeveloperInitialProfile.MaximumParameters) throw new InvalidOperationException("Tuning profile exceeds its bound.");
        return values.OrderBy(v => v.Id).ThenBy(v => v.Target.Player.Value).ToArray();
    }
    private static ReadOnlyCollection<DeveloperTuningDescriptor> Create()
    {
        var entries = new List<DeveloperTuningDescriptor>();
        void Add(DeveloperParameterId id, string label, double? maximum = null, double? step = null)
        {
            if (!DeveloperParameterCatalog.TryGet(id, out var source)) throw new InvalidOperationException("Missing shared parameter contract.");
            DeveloperValue Convert(double value) => source.Kind == DeveloperValueKind.UnsignedNumber
                ? DeveloperValue.FromUInt32(checked((uint)value)) : DeveloperValue.FromDouble(value);
            var contract = new DeveloperParameterDescriptor(id, source.Key, source.Category, source.Unit, source.Default,
                source.Minimum, maximum.HasValue ? Convert(maximum.Value) : source.Maximum,
                step.HasValue ? Convert(step.Value) : source.Step, source.Targets, source.ApplyMode);
            entries.Add(new(label, contract));
        }
        Add(DeveloperParameterId.ConveyorThroughputMultiplier, "Conveyor throughput", step: .25);
        Add(DeveloperParameterId.OpponentAggression, "Aggression");
        Add(DeveloperParameterId.OpponentExpansionReadiness, "Expansion readiness");
        Add(DeveloperParameterId.OpponentOffensiveReadiness, "Offensive readiness");
        Add(DeveloperParameterId.OpponentOffensiveFuel, "Offensive fuel reserve");
        Add(DeveloperParameterId.OpponentRetreat, "Retreat threshold");
        Add(DeveloperParameterId.OpponentResupply, "Resupply threshold");
        Add(DeveloperParameterId.OpponentReactionCadence, "Reaction cadence", 1200);
        Add(DeveloperParameterId.RaidEnabled, "Raids enabled");
        Add(DeveloperParameterId.RaidCooldownTicks, "Raid cooldown", 12000);
        Add(DeveloperParameterId.DistributionRetryDelay, "Distribution retry delay", 1200);
        Add(DeveloperParameterId.DistributionMaximumAttempts, "Transport attempts", 32);
        Add(DeveloperParameterId.DistributionFairnessAging, "Distribution fairness aging", 12000);
        return entries.AsReadOnly();
    }
}
