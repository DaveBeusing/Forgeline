namespace ForgeLine.Game;

/// <summary>Explicit metadata registry, not a runtime parameter handler.</summary>
public static class DeveloperParameterCatalog
{
    public static IReadOnlyList<DeveloperParameterDescriptor> Descriptors { get; } = Build();
    private static readonly Dictionary<DeveloperParameterId, DeveloperParameterDescriptor> ById =
        Descriptors.ToDictionary(d => d.Id);
    public static bool TryGet(DeveloperParameterId id, out DeveloperParameterDescriptor descriptor) => ById.TryGetValue(id, out descriptor!);

    private static System.Collections.ObjectModel.ReadOnlyCollection<DeveloperParameterDescriptor> Build()
    {
        var entries = new List<DeveloperParameterDescriptor>();
        void Add(DeveloperParameterId id, string key, string category, string unit, DeveloperValue value,
            DeveloperValue min, DeveloperValue max, DeveloperValue step, DeveloperTargetKinds targets = DeveloperTargetKinds.Player,
            DeveloperApplyMode mode = DeveloperApplyMode.NextMatch) => entries.Add(new(id, key, category, unit, value, min, max, step, targets, mode));
        void Fraction(DeveloperParameterId id, string key, double value) => Add(id, key, "Opponent", "ratio",
            DeveloperValue.FromDouble(value), DeveloperValue.FromDouble(0), DeveloperValue.FromDouble(1), DeveloperValue.FromDouble(.01));
        void Ticks(DeveloperParameterId id, string key, uint value, uint max = uint.MaxValue, DeveloperTargetKinds targets = DeveloperTargetKinds.Player) => Add(id, key, "Timing", "ticks",
            DeveloperValue.FromUInt32(value), DeveloperValue.FromUInt32(1), DeveloperValue.FromUInt32(max), DeveloperValue.FromUInt32(1), targets);
        void Count(DeveloperParameterId id, string key, int value, int min, int max) => Add(id, key, "Raids", "units",
            DeveloperValue.FromInt32(value), DeveloperValue.FromInt32(min), DeveloperValue.FromInt32(max), DeveloperValue.FromInt32(1));
        void Range(DeveloperParameterId id, string key, float value) => Add(id, key, "Raids", "metres",
            DeveloperValue.FromDouble(value), DeveloperValue.FromDouble(float.Epsilon), DeveloperValue.FromDouble(float.MaxValue), DeveloperValue.FromDouble(1));
        var opponent = new SkirmishOpponentConfiguration();
        var raid = opponent.Raids;
        Add(DeveloperParameterId.ConveyorThroughputMultiplier, "conveyor.throughput_multiplier", "Conveyors", "multiplier",
            DeveloperValue.FromDouble(1), DeveloperValue.FromDouble(.25), DeveloperValue.FromDouble(4), DeveloperValue.FromDouble(.05), DeveloperTargetKinds.Global, DeveloperApplyMode.NextTick);
        Fraction(DeveloperParameterId.OpponentAggression, "opponent.aggression", opponent.Aggression);
        Fraction(DeveloperParameterId.OpponentExpansionReadiness, "opponent.expansion_readiness", opponent.ExpansionReadinessThreshold);
        Fraction(DeveloperParameterId.OpponentOffensiveReadiness, "opponent.offensive_readiness", opponent.OffensiveReadinessThreshold);
        Fraction(DeveloperParameterId.OpponentRetreat, "opponent.retreat_threshold", opponent.RetreatThreshold);
        Fraction(DeveloperParameterId.OpponentResupply, "opponent.resupply_threshold", opponent.ResupplyThreshold);
        Fraction(DeveloperParameterId.OpponentOffensiveFuel, "opponent.offensive_fuel_threshold", opponent.OffensiveFuelThreshold);
        Ticks(DeveloperParameterId.OpponentReactionCadence, "opponent.reaction_cadence_ticks", opponent.ReactionCadenceTicks);
        Add(DeveloperParameterId.RaidEnabled, "opponent.raids.enabled", "Raids", "boolean", DeveloperValue.FromBoolean(raid.Enabled),
            DeveloperValue.FromBoolean(false), DeveloperValue.FromBoolean(true), DeveloperValue.FromBoolean(true));
        Count(DeveloperParameterId.RaidMinimumUnits, "opponent.raids.minimum_units", raid.MinimumUnits, 2, 3);
        Count(DeveloperParameterId.RaidMaximumUnits, "opponent.raids.maximum_units", raid.MaximumUnits, 2, 3);
        Count(DeveloperParameterId.RaidHomeDefenders, "opponent.raids.home_defenders", raid.HomeDefenders, 1, int.MaxValue);
        Count(DeveloperParameterId.RaidMaximumContacts, "opponent.raids.maximum_contacts", raid.MaximumContacts, 1, 32);
        Ticks(DeveloperParameterId.RaidActiveTicks, "opponent.raids.active_ticks", raid.ActiveTicks, 1200);
        Ticks(DeveloperParameterId.RaidCooldownTicks, "opponent.raids.cooldown_ticks", raid.CooldownTicks);
        Ticks(DeveloperParameterId.RaidSearchTicks, "opponent.raids.search_ticks", raid.SearchTicks);
        Fraction(DeveloperParameterId.RaidReadiness, "opponent.raids.readiness_threshold", raid.ReadinessThreshold);
        Add(DeveloperParameterId.RaidFuelMargin, "opponent.raids.fuel_margin", "Raids", "multiplier",
            DeveloperValue.FromDouble(raid.FuelMargin), DeveloperValue.FromDouble(1), DeveloperValue.FromDouble(double.MaxValue), DeveloperValue.FromDouble(.05));
        Fraction(DeveloperParameterId.RaidFuelReserve, "opponent.raids.fuel_reserve_fraction", raid.FuelReserveFraction);
        Count(DeveloperParameterId.RaidMinimumShots, "opponent.raids.minimum_shots", raid.MinimumShots, 3, int.MaxValue);
        Count(DeveloperParameterId.RaidMaximumKnownThreat, "opponent.raids.maximum_known_threat", raid.MaximumKnownThreat, 0, int.MaxValue);
        Range(DeveloperParameterId.RaidHomeRadius, "opponent.raids.home_radius_metres", raid.HomeRadiusMeters);
        Range(DeveloperParameterId.RaidMaximumDistance, "opponent.raids.maximum_distance_metres", raid.MaximumDistanceMeters);
        Range(DeveloperParameterId.RaidPursuitLeash, "opponent.raids.pursuit_leash_metres", raid.PursuitLeashMeters);
        Range(DeveloperParameterId.RaidThreatRadius, "opponent.raids.threat_radius_metres", raid.ThreatRadiusMeters);
        Ticks(DeveloperParameterId.DistributionRetryDelay, "distribution.retry_delay_ticks", 20, targets: DeveloperTargetKinds.Global);
        Ticks(DeveloperParameterId.DistributionMaximumAttempts, "distribution.maximum_transport_attempts", 4, targets: DeveloperTargetKinds.Global);
        Ticks(DeveloperParameterId.DistributionFairnessAging, "distribution.fairness_aging_ticks", 200, targets: DeveloperTargetKinds.Global);
        if (entries.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidOperationException("Duplicate developer parameter keys.");
        return entries.AsReadOnly();
    }
}
