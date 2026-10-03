namespace ForgeLine.Game;

public sealed record GameplayTelemetryMatch(
    int MatchIndex,
    ulong Seed,
    GameplayTelemetrySnapshot Telemetry);

public sealed record GameplayMetricAggregate(
    string Name,
    string Owner,
    string Dimension,
    string Unit,
    int SampleCount,
    double Minimum,
    double Maximum,
    double Mean,
    double StandardDeviation);

public sealed record GameplayMilestoneAggregate(
    string Name,
    string Owner,
    string Dimension,
    int SampleCount,
    ulong MinimumTick,
    ulong MaximumTick,
    double MeanTick);

public sealed record GameplayMetricComparison(
    string Name,
    string Owner,
    string Dimension,
    string Unit,
    double BaselineMean,
    double CurrentMean,
    double AbsoluteDelta,
    double? RelativeDelta);

public sealed record GameplayTelemetryAnalysisResult(
    IReadOnlyList<GameplayMetricAggregate> Metrics,
    IReadOnlyList<GameplayMilestoneAggregate> Milestones);

public static class GameplayTelemetryAnalysis
{
    public static GameplayTelemetryAnalysisResult Aggregate(
        IReadOnlyList<GameplayTelemetryMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        return new GameplayTelemetryAnalysisResult(
            AggregateMetrics(matches),
            AggregateMilestones(matches));
    }

    public static IReadOnlyList<GameplayMetricComparison> Compare(
        GameplayTelemetryAnalysisResult baseline,
        GameplayTelemetryAnalysisResult current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineByKey =
            baseline.Metrics.ToDictionary(
                static metric =>
                    new MetricKey(
                        metric.Name,
                        metric.Owner,
                        metric.Dimension,
                        metric.Unit));

        var comparisons =
            new List<GameplayMetricComparison>();

        for (int index = 0;
             index < current.Metrics.Count;
             index++)
        {
            GameplayMetricAggregate metric =
                current.Metrics[index];
            var key =
                new MetricKey(
                    metric.Name,
                    metric.Owner,
                    metric.Dimension,
                    metric.Unit);

            if (!baselineByKey.TryGetValue(
                    key,
                    out GameplayMetricAggregate? previous))
            {
                continue;
            }

            double delta =
                metric.Mean -
                previous.Mean;
            double? relative =
                Math.Abs(previous.Mean) > double.Epsilon
                    ? delta /
                      previous.Mean
                    : null;

            comparisons.Add(
                new GameplayMetricComparison(
                    metric.Name,
                    metric.Owner,
                    metric.Dimension,
                    metric.Unit,
                    previous.Mean,
                    metric.Mean,
                    delta,
                    relative));
        }

        comparisons.Sort(
            static (left, right) =>
            {
                int name =
                    string.CompareOrdinal(
                        left.Name,
                        right.Name);

                if (name != 0)
                {
                    return name;
                }

                int owner =
                    string.CompareOrdinal(
                        left.Owner,
                        right.Owner);

                if (owner != 0)
                {
                    return owner;
                }

                return string.CompareOrdinal(
                    left.Dimension,
                    right.Dimension);
            });

        return comparisons;
    }

    private static IReadOnlyList<GameplayMetricAggregate> AggregateMetrics(
        IReadOnlyList<GameplayTelemetryMatch> matches)
    {
        var groups =
            new Dictionary<MetricKey, List<double>>();

        for (int matchIndex = 0;
             matchIndex < matches.Count;
             matchIndex++)
        {
            IReadOnlyList<GameplayMetric> metrics =
                matches[matchIndex].Telemetry.Metrics;

            for (int metricIndex = 0;
                 metricIndex < metrics.Count;
                 metricIndex++)
            {
                GameplayMetric metric =
                    metrics[metricIndex];
                var key =
                    new MetricKey(
                        metric.Name,
                        metric.Owner,
                        metric.Dimension,
                        metric.Unit);

                if (!groups.TryGetValue(
                        key,
                        out List<double>? values))
                {
                    values = new List<double>();
                    groups.Add(
                        key,
                        values);
                }

                values.Add(
                    metric.Value);
            }
        }

        var result =
            new List<GameplayMetricAggregate>(
                groups.Count);

        foreach ((MetricKey key, List<double> values) in groups)
        {
            double sum = 0.0;
            double minimum =
                double.PositiveInfinity;
            double maximum =
                double.NegativeInfinity;

            for (int index = 0;
                 index < values.Count;
                 index++)
            {
                double value =
                    values[index];
                sum +=
                    value;
                minimum =
                    Math.Min(
                        minimum,
                        value);
                maximum =
                    Math.Max(
                        maximum,
                        value);
            }

            double mean =
                values.Count > 0
                    ? sum /
                      values.Count
                    : 0.0;
            double variance = 0.0;

            for (int index = 0;
                 index < values.Count;
                 index++)
            {
                double difference =
                    values[index] -
                    mean;
                variance +=
                    difference *
                    difference;
            }

            double standardDeviation =
                values.Count > 0
                    ? Math.Sqrt(
                        variance /
                        values.Count)
                    : 0.0;

            result.Add(
                new GameplayMetricAggregate(
                    key.Name,
                    key.Owner,
                    key.Dimension,
                    key.Unit,
                    values.Count,
                    values.Count > 0
                        ? minimum
                        : 0.0,
                    values.Count > 0
                        ? maximum
                        : 0.0,
                    mean,
                    standardDeviation));
        }

        result.Sort(
            static (left, right) =>
            {
                int name =
                    string.CompareOrdinal(
                        left.Name,
                        right.Name);

                if (name != 0)
                {
                    return name;
                }

                int owner =
                    string.CompareOrdinal(
                        left.Owner,
                        right.Owner);

                return owner != 0
                    ? owner
                    : string.CompareOrdinal(
                        left.Dimension,
                        right.Dimension);
            });

        return result;
    }

    private static IReadOnlyList<GameplayMilestoneAggregate>
        AggregateMilestones(
            IReadOnlyList<GameplayTelemetryMatch> matches)
    {
        var groups =
            new Dictionary<MilestoneKey, List<ulong>>();

        for (int matchIndex = 0;
             matchIndex < matches.Count;
             matchIndex++)
        {
            IReadOnlyList<GameplayMilestone> milestones =
                matches[matchIndex].Telemetry.Milestones;

            for (int milestoneIndex = 0;
                 milestoneIndex < milestones.Count;
                 milestoneIndex++)
            {
                GameplayMilestone milestone =
                    milestones[milestoneIndex];
                var key =
                    new MilestoneKey(
                        milestone.Name,
                        milestone.Owner,
                        milestone.Dimension);

                if (!groups.TryGetValue(
                        key,
                        out List<ulong>? values))
                {
                    values = new List<ulong>();
                    groups.Add(
                        key,
                        values);
                }

                values.Add(
                    milestone.Tick);
            }
        }

        var result =
            new List<GameplayMilestoneAggregate>(
                groups.Count);

        foreach ((MilestoneKey key, List<ulong> values) in groups)
        {
            ulong minimum =
                ulong.MaxValue;
            ulong maximum = 0;
            double sum = 0.0;

            for (int index = 0;
                 index < values.Count;
                 index++)
            {
                ulong value =
                    values[index];
                minimum =
                    Math.Min(
                        minimum,
                        value);
                maximum =
                    Math.Max(
                        maximum,
                        value);
                sum +=
                    value;
            }

            result.Add(
                new GameplayMilestoneAggregate(
                    key.Name,
                    key.Owner,
                    key.Dimension,
                    values.Count,
                    values.Count > 0
                        ? minimum
                        : 0,
                    maximum,
                    values.Count > 0
                        ? sum /
                          values.Count
                        : 0.0));
        }

        result.Sort(
            static (left, right) =>
            {
                int name =
                    string.CompareOrdinal(
                        left.Name,
                        right.Name);

                if (name != 0)
                {
                    return name;
                }

                int owner =
                    string.CompareOrdinal(
                        left.Owner,
                        right.Owner);

                return owner != 0
                    ? owner
                    : string.CompareOrdinal(
                        left.Dimension,
                        right.Dimension);
            });

        return result;
    }

    private readonly record struct MetricKey(
        string Name,
        string Owner,
        string Dimension,
        string Unit);

    private readonly record struct MilestoneKey(
        string Name,
        string Owner,
        string Dimension);
}
