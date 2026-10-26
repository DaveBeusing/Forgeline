using System.Collections.ObjectModel;

namespace ForgeLine.Game;

/// <summary>A validated proposal. Creating this object does not replace or modify a session.</summary>
public sealed class DeveloperRestartPlan
{
    internal DeveloperRestartPlan(MatchRuntimeSettings basis, MatchRuntimeSettings settings, DeveloperInitialProfile initial)
    { BaseSettings = basis; Settings = settings; InitialOverrides = initial; }
    public MatchRuntimeSettings BaseSettings { get; }
    public MatchRuntimeSettings Settings { get; }
    public DeveloperInitialProfile InitialOverrides { get; }
}

public static class DeveloperRestartBuilder
{
    public static DeveloperRestartPlan ForExisting(MatchRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var metadata = new DeveloperPresetMetadata(1, settings.Composition.Key, settings.Composition.Identity ?? throw new InvalidOperationException("Canonical scenario identity required."), settings.Seed,
            settings.Scenario.Profile, settings.Participants.ToArray(), false);
        if (!TryBuild(settings, metadata, [], out var plan, out var failure))
            throw new InvalidOperationException($"Cannot initialize tuning: {failure}.");
        return plan!;
    }
    public static bool TryBuild(MatchRuntimeSettings basis, DeveloperPresetMetadata metadata,
        ReadOnlySpan<DeveloperTuningOverride> overrides, out DeveloperRestartPlan? plan, out DeveloperFailureReason failure)
    {
        ArgumentNullException.ThrowIfNull(basis); ArgumentNullException.ThrowIfNull(metadata);
        plan = null; failure = DeveloperFailureReason.InvalidValue;
        if (overrides.Length > DeveloperInitialProfile.MaximumParameters) return false;
        if (metadata.CompositionKey != basis.Composition.Key || metadata.BaseIdentity != basis.Composition.Identity ||
            metadata.Profile != basis.Scenario.Profile) return false;
        var keys = new HashSet<(DeveloperParameterId, DeveloperTarget)>();
        foreach (var value in overrides)
        {
            failure = DeveloperGameplayTuningSchema.Validate(value, metadata.Participants);
            if (failure != DeveloperFailureReason.None) return false;
            if (!keys.Add((value.Id, value.Target))) { failure = DeveloperFailureReason.InvalidValue; return false; }
        }
        try
        {
            var baseSettings = basis with { InitialDeveloperProfile = null, Scenario = DeveloperGameplayTuningSchema.Copy(basis.Scenario), Seed = metadata.Seed,
                Participants = Array.AsReadOnly(metadata.Participants.ToArray()) };
            var scenario = DeveloperGameplayTuningSchema.Copy(baseSettings.Scenario);
            var opponents = scenario.OpponentConfigurations.ToDictionary(p => p.Key, p => p.Value with { Raids = p.Value.Raids with { } });
            foreach (var value in overrides)
            {
                if (value.Id == DeveloperParameterId.ConveyorThroughputMultiplier) continue;
                if (value.Target.Kind == DeveloperTargetKinds.Global)
                {
                    uint number = value.Value.AsUInt32();
                    scenario = value.Id switch
                    {
                        DeveloperParameterId.DistributionRetryDelay => scenario with { DistributionRetryDelayTicks = number },
                        DeveloperParameterId.DistributionMaximumAttempts => scenario with { DistributionMaximumTransportAttempts = number },
                        DeveloperParameterId.DistributionFairnessAging => scenario with { DistributionFairnessAgingTicks = number },
                        _ => throw new InvalidOperationException("Unsupported global policy.")
                    };
                    continue;
                }
                if (!opponents.TryGetValue(value.Target.Player.Value, out var policy))
                { failure = DeveloperFailureReason.InvalidTarget; return false; }
                policy = value.Id switch
                {
                    DeveloperParameterId.OpponentAggression => policy with { Aggression = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentExpansionReadiness => policy with { ExpansionReadinessThreshold = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentOffensiveReadiness => policy with { OffensiveReadinessThreshold = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentOffensiveFuel => policy with { OffensiveFuelThreshold = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentRetreat => policy with { RetreatThreshold = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentResupply => policy with { ResupplyThreshold = value.Value.AsDouble() },
                    DeveloperParameterId.OpponentReactionCadence => policy with { ReactionCadenceTicks = value.Value.AsUInt32() },
                    DeveloperParameterId.RaidEnabled => policy with { Raids = policy.Raids with { Enabled = value.Value.AsBoolean() } },
                    DeveloperParameterId.RaidCooldownTicks => policy with { Raids = policy.Raids with { CooldownTicks = value.Value.AsUInt32() } },
                    _ => throw new InvalidOperationException("Unsupported opponent policy.")
                };
                opponents[value.Target.Player.Value] = policy;
            }
            scenario = scenario with { OpponentConfigurations = new ReadOnlyDictionary<ulong, SkirmishOpponentConfiguration>(opponents) };
            var settings = baseSettings with { Scenario = scenario };
            settings.Validate();
            var profileMetadata = new DeveloperPresetMetadata(1, metadata.CompositionKey, metadata.BaseIdentity, metadata.Seed,
                metadata.Profile, metadata.Participants.ToArray(), metadata.RequiresDeveloperTools || overrides.Length != 0);
            var values = overrides.ToArray().OrderBy(v => v.Id).ThenBy(v => v.Target.Player.Value)
                .Select(v => new DeveloperEffectiveParameter(v.Id, v.Target, v.Value, new(DeveloperRevisionDomain.Parameters, 0))).ToArray();
            var initial = new DeveloperInitialProfile(profileMetadata, values);
            settings = settings with { InitialDeveloperProfile = initial };
            plan = new(baseSettings, settings, initial);
            failure = DeveloperFailureReason.None; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { failure = DeveloperFailureReason.InvalidValue; return false; }
    }
}
