namespace ForgeLine.Game;

public sealed record PacingMeasurement(string Name, string Owner, string Availability, double? Value, string Unit, string Evidence);
public sealed record PacingDistribution(string Name, string Owner, string Unit, int Reached, int Unreached, int Unavailable,
    double? Minimum, double? Median, double? P90, double? Maximum, double? Mean);
public sealed record PacingWarning(string Code, string Metric, string Owner, string Message);

public sealed record PacingThresholds
{
    public double FirstContactMaximumSeconds { get; init; } = 600;
    public double SupplyPressureMaximumSeconds { get; init; } = 120;
    public double NoProgressMaximumSeconds { get; init; } = 300;
    public double FactoryUptimeMinimumRatio { get; init; } = .1;
    public double MinimumObservationSeconds { get; init; } = 60;
    public double VictoryMinimumSeconds { get; init; } = 2700;
    public double VictoryMaximumSeconds { get; init; } = 5400;
    public void Validate()
    {
        foreach (double value in new[] { FirstContactMaximumSeconds, SupplyPressureMaximumSeconds,
            NoProgressMaximumSeconds, MinimumObservationSeconds, VictoryMinimumSeconds, VictoryMaximumSeconds })
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentException("Pacing time thresholds must be finite and positive.");
        if (!double.IsFinite(FactoryUptimeMinimumRatio) || FactoryUptimeMinimumRatio < 0 || FactoryUptimeMinimumRatio > 1 ||
            VictoryMinimumSeconds > VictoryMaximumSeconds) throw new ArgumentException("Invalid pacing threshold range.");
    }
}

public static class MatchPacingAnalysis
{
    public static IReadOnlyList<PacingMeasurement> Measure(GameplayTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = new List<PacingMeasurement>();
        var observation = snapshot.Decisions;
        bool complete = observation is { ObservedTicks: > 0, MissedTicks: 0, TruncatedTicks: 0, StartedAtTick: 0 };
        double rate = snapshot.TickRate;
        if (rate <= 0) throw new ArgumentException("Pacing requires a positive logical tick rate.", nameof(snapshot));
        foreach (var player in observation?.Players ?? [])
        {
            string owner = $"player:{player.Player}";
            Milestone("scout.first_produced", owner, snapshot.Milestones.FirstOrDefault(m =>
                m.Name == GameplayMilestoneNames.FirstScoutProduced && m.Owner == owner)?.Tick, observation!.ScoutAvailable);
            Milestone("contact.first_authorized", owner, Pacing(GameplayPacingNames.AuthorizedContact, owner));
            Milestone("expansion.first_strategic", owner, Pacing(GameplayPacingNames.StrategicExpansion, owner));
            Milestone("supply.first_chain_disruption", owner, Pacing(GameplayPacingNames.ChainDisruption, owner));
            Milestone("supply.first_chain_recovery", owner, Pacing(GameplayPacingNames.ChainRecovery, owner));
            Milestone("research.first_completed", owner, Pacing(GameplayPacingNames.ResearchCompleted, owner), observation.ResearchAvailable);
            Milestone("technology.first_unlock", owner, player.FirstTechnologyUnlockTick, observation.TechnologyUnlockAvailable);
            Milestone("supply.first_pressure", owner, player.FirstSupplyShortageTick, player.SupplyUnitTicks > 0);
            Metric("factory.productive_uptime", owner, player.FactoryTicks == 0 ? null : (double)player.FactoryRunningTicks / player.FactoryTicks,
                "ratio", "Running unit-production facility ticks / all observed unit-production facility ticks.");
            Metric("resources.shortfall", owner, player.ResourceFacilityTicks == 0 ? null : player.ResourceShortfallFacilityTicks / rate,
                "facility_seconds", "Processing and unit-production NoInput facility time; no missing resource is inferred.");
            Metric("supply.longest_pressure", owner, player.SupplyUnitTicks == 0 ? null : player.LongestSupplyShortageTicks / rate,
                "seconds", "Continuous observed unit shortage; not proof of zero physical throughput.");
            Metric("front.centroid_travel", owner, player.FrontTravelMeters, "meters",
                "Combat-unit centroid travel only across equal full-generation membership; not territorial front position.");
        }
        Milestone("combat.first_damage", "match", Pacing(GameplayPacingNames.CombatDamage, "match"));
        bool victory = snapshot.MatchResult.Outcome == "Victory";
        Milestone("victory", "match", victory ? snapshot.MatchResult.CompletedAtTick : null);
        Metric("activity.longest_no_progress", "match", observation is null ? null : observation.LongestNoProgressTicks / rate,
            "seconds", "No new milestone, processing output, combat damage or stable-membership front motion; intent is unknown.");
        return rows.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Owner, StringComparer.Ordinal).ToArray();

        ulong? Pacing(string name, string owner) => snapshot.Pacing?.FirstOrDefault(p => p.Name == name && p.Owner == owner)?.Tick;
        void Milestone(string name, string owner, ulong? tick, bool supported = true)
        {
            bool available = complete && supported;
            rows.Add(new(name, owner, !available ? "Unavailable" : tick.HasValue ? "Reached" : "Unreached",
                available && tick.HasValue ? tick.Value / rate : null, "seconds",
                !complete ? "Incomplete observation window or absent pacing capture." : !supported ? "Feature or sample population unavailable." :
                    tick.HasValue ? "First completed-tick observation." : "Not observed within this run; not tick zero."));
        }
        void Metric(string name, string owner, double? value, string unit, string evidence) =>
            rows.Add(new(name, owner, complete && value.HasValue ? "Reached" : "Unavailable",
                complete ? value : null, unit, evidence));
    }

    public static IReadOnlyList<PacingDistribution> Aggregate(IReadOnlyList<IReadOnlyList<PacingMeasurement>> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.SelectMany(r => r).Select(r => (r.Name, r.Owner, r.Unit)).Distinct()
            .OrderBy(k => k.Name, StringComparer.Ordinal).ThenBy(k => k.Owner, StringComparer.Ordinal)
            .Select(key =>
            {
                var observations = runs.Select(r => r.SingleOrDefault(v => v.Name == key.Name && v.Owner == key.Owner && v.Unit == key.Unit)).ToArray();
                var values = observations.Where(v => v?.Availability == "Reached" && v.Value.HasValue)
                    .Select(v => v!.Value!.Value).Order().ToArray();
                int unreached = observations.Count(v => v?.Availability == "Unreached");
                return new PacingDistribution(key.Name, key.Owner, key.Unit, values.Length, unreached, runs.Count - values.Length - unreached,
                    values.Length == 0 ? null : values[0], Quantile(.5), Quantile(.9), values.Length == 0 ? null : values[^1],
                    values.Length == 0 ? null : values.Average());
                double? Quantile(double p)
                {
                    if (values.Length == 0) return null;
                    double index = (values.Length - 1) * p;
                    int low = (int)index, high = (int)Math.Ceiling(index);
                    return values[low] + (values[high] - values[low]) * (index - low);
                }
            }).ToArray();
    }

    public static IReadOnlyList<PacingWarning> Warnings(IReadOnlyList<PacingMeasurement> rows, double observedSeconds,
        PacingThresholds thresholds, bool gameplayProfile)
    {
        ArgumentNullException.ThrowIfNull(thresholds); thresholds.Validate();
        var result = new List<PacingWarning>();
        foreach (var row in rows)
        {
            if (row.Availability == "Unavailable") continue;
            bool beyondHorizon(double limit) => row.Value is { } value ? value > limit : observedSeconds >= limit;
            if (row.Name == "contact.first_authorized" && beyondHorizon(thresholds.FirstContactMaximumSeconds))
                Warn("late_contact", "Authorized contact is delayed or absent beyond the configured observation horizon.");
            if (row.Name == "supply.longest_pressure" && row.Value >= thresholds.SupplyPressureMaximumSeconds)
                Warn("sustained_supply_pressure", "Unit shortage persisted; inspect real deliveries and consumption.");
            if (row.Name == "activity.longest_no_progress" && row.Value >= thresholds.NoProgressMaximumSeconds)
                Warn("quiet_window", "Long interval without observed progress signals; this does not establish player inactivity.");
            if (row.Name == "factory.productive_uptime" && observedSeconds >= thresholds.MinimumObservationSeconds && row.Value < thresholds.FactoryUptimeMinimumRatio)
                Warn("low_factory_uptime", "Low productive factory share; inspect demand, inputs, power and intentional idle time.");
            if (gameplayProfile && row.Name == "victory" && (beyondHorizon(thresholds.VictoryMaximumSeconds) || row.Value < thresholds.VictoryMinimumSeconds))
                Warn("victory_design_window", "Result falls outside, or has not reached, the aspirational duration window; no target is guaranteed.");
            void Warn(string code, string message) => result.Add(new(code, row.Name, row.Owner, message));
        }
        return result;
    }
}
