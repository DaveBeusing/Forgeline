using System.Collections.ObjectModel;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum OpponentDifficultyId : byte { Easy = 1, Standard = 2, Hard = 3 }

public sealed record OpponentDifficultyProfile(
    OpponentDifficultyId Id, string Key, string DisplayName, SkirmishOpponentConfiguration Configuration);

// Named policies contain only existing command-selection parameters. No economy/sensor/stat authority.
public static class OpponentDifficultyProfiles
{
    private static readonly OpponentDifficultyProfile[] Profiles =
    [
        new(OpponentDifficultyId.Easy, "skirmish.easy.v1", "Easy", new()
        {
            ReactionCadenceTicks = 40, Aggression = .55, ExpansionReadinessThreshold = .65,
            MaximumAttackUnits = 6, MaximumQueuedUnitsPerFacility = 1, ArtilleryCadenceTicks = 160,
            Raids = new() { MaximumUnits = 2, MaximumContacts = 8, CooldownTicks = 1200, SearchTicks = 160 }
        }),
        new(OpponentDifficultyId.Standard, "skirmish.standard.v1", "Standard", new()),
        new(OpponentDifficultyId.Hard, "skirmish.hard.v1", "Hard", new()
        {
            ReactionCadenceTicks = 10, Aggression = .75, ExpansionReadinessThreshold = .50,
            MaximumAttackUnits = 16, MaximumQueuedUnitsPerFacility = 3, ArtilleryCadenceTicks = 40,
            Raids = new() { MaximumContacts = 32, CooldownTicks = 400, SearchTicks = 40 }
        })
    ];

    public static IReadOnlyList<OpponentDifficultyProfile> All { get; } = Array.AsReadOnly(Profiles);

    static OpponentDifficultyProfiles()
    {
        foreach (var profile in Profiles) profile.Configuration.Validate();
    }

    public static OpponentDifficultyProfile Get(OpponentDifficultyId id) =>
        Profiles.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));

    public static string Identify(SkirmishOpponentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Profiles.FirstOrDefault(p => p.Configuration == configuration)?.Key ?? "custom";
    }

    public static MatchScenarioSettings Apply(MatchScenarioSettings settings,
        IReadOnlyDictionary<ulong, OpponentDifficultyId> assignments)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(assignments);
        settings.Validate();
        var configurations = settings.OpponentConfigurations.ToDictionary(p => p.Key, p => p.Value);
        foreach (var pair in assignments.OrderBy(p => p.Key))
        {
            if (!configurations.ContainsKey(pair.Key))
                throw new ArgumentException("Difficulty assignment must name a configured participant.", nameof(assignments));
            configurations[pair.Key] = Get(pair.Value).Configuration;
        }
        var result = settings with
        {
            OpponentConfigurations = new ReadOnlyDictionary<ulong, SkirmishOpponentConfiguration>(configurations)
        };
        result.Validate();
        return result;
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OpponentDecisionCause>))]
public enum OpponentDecisionCause : byte
{
    CadenceWait, EconomyRecovery, Bootstrap, CurrentThreat, Expansion,
    SupplyRecovery, Reconnaissance, ObjectivePressure, OffensiveAdmission
}

// Optional qualification projection. No hostile identities/coordinates, hidden inventory or gameplay authority.
public sealed record OpponentDecisionDiagnostic(
    ulong Player, string ProfileKey, ulong ObservedTick, ulong LastDecisionTick, ulong NextDecisionTick,
    int DecisionsTaken, OpponentDecisionCause Cause,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SkirmishStrategicGoal>))]
    SkirmishStrategicGoal Goal,
    SkirmishOffensiveAdmissionReason OffensiveAdmission, SkirmishSupplyRequirement SupplyRequirement,
    SkirmishRaidReason RaidAdmission, int ContactBudget)
{
    internal static OpponentDecisionDiagnostic From(SkirmishOpponentDebugReadModel observation,
        SkirmishOpponentConfiguration configuration, SimulationTick observedTick)
    {
        OpponentDecisionCause cause = observation.LastDecisionTick != observedTick ? OpponentDecisionCause.CadenceWait :
            observation.StrategicState switch
            {
                SkirmishStrategicState.Recovering => OpponentDecisionCause.EconomyRecovery,
                SkirmishStrategicState.Bootstrap => OpponentDecisionCause.Bootstrap,
                SkirmishStrategicState.Defending => OpponentDecisionCause.CurrentThreat,
                SkirmishStrategicState.Expanding => OpponentDecisionCause.Expansion,
                SkirmishStrategicState.Resupplying => OpponentDecisionCause.SupplyRecovery,
                SkirmishStrategicState.Attacking => OpponentDecisionCause.ObjectivePressure,
                _ => observation.ActiveGoal == SkirmishStrategicGoal.Scout
                    ? OpponentDecisionCause.Reconnaissance : OpponentDecisionCause.OffensiveAdmission
            };
        ulong last = observation.LastDecisionTick.Value;
        ulong next = ulong.MaxValue - last < configuration.ReactionCadenceTicks ? ulong.MaxValue :
            last + configuration.ReactionCadenceTicks;
        return new(observation.Player.Value, OpponentDifficultyProfiles.Identify(configuration),
            observedTick.Value, last, next, observation.DecisionsTaken, cause, observation.ActiveGoal,
            observation.OffensiveAdmission.Reason, observation.SupplyRequirement, observation.Raid.AdmissionReason,
            configuration.Raids.MaximumContacts);
    }
}
