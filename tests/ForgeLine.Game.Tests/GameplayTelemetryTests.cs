using System.Text.Json;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GameplayTelemetryTests
{
    [Fact]
    public void ObservationDoesNotMutateAuthoritativeState()
    {
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation),
                seed: 2026);
        var telemetry =
            new GameplayTelemetryCollector(
                scenario);

        MatchState matchBefore =
            scenario.GetMatchState();
        int entitiesBefore =
            scenario.Simulation.Entities.EntityCount;
        ulong tickBefore =
            scenario.Simulation.CurrentTick.Value;
        var extractionBefore =
            scenario.Extraction.Metrics;
        var productionBefore =
            scenario.Production.Metrics;
        var cargoBefore =
            scenario.CargoTransport.Metrics;
        var supplyBefore =
            scenario.BattlefieldSupply.Metrics;

        telemetry.Observe();
        _ = telemetry.Capture();

        Assert.Equal(
            matchBefore,
            scenario.GetMatchState());
        Assert.Equal(
            entitiesBefore,
            scenario.Simulation.Entities.EntityCount);
        Assert.Equal(
            tickBefore,
            scenario.Simulation.CurrentTick.Value);
        Assert.Equal(
            extractionBefore,
            scenario.Extraction.Metrics);
        Assert.Equal(
            productionBefore,
            scenario.Production.Metrics);
        Assert.Equal(
            cargoBefore,
            scenario.CargoTransport.Metrics);
        Assert.Equal(
            supplyBefore,
            scenario.BattlefieldSupply.Metrics);
    }

    [Fact]
    public void ResourceIncomeCrossChecksExtractionMetrics()
    {
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation),
                seed: 2026);
        var telemetry =
            new GameplayTelemetryCollector(
                scenario);

        RunTicks(
            scenario,
            telemetry,
            600);

        GameplayTelemetrySnapshot snapshot =
            telemetry.Capture();
        double measuredExtraction =
            snapshot.Metrics
                .Where(
                    static metric =>
                        metric.Name ==
                            GameplayMetricNames.ResourceIncomeQuantity &&
                        metric.Owner ==
                            "match")
                .Sum(
                    static metric =>
                        metric.Value);

        Assert.Equal(
            scenario.Extraction.Metrics.TotalExtractedQuantity,
            measuredExtraction,
            precision: 6);
    }

    [Fact]
    public void SeededTelemetryIsReproducible()
    {
        GameplayTelemetrySnapshot first =
            CaptureSeededTelemetry(
                seed: 91,
                ticks: 400);
        GameplayTelemetrySnapshot second =
            CaptureSeededTelemetry(
                seed: 91,
                ticks: 400);

        Assert.Equal(
            first.ObservedTicks,
            second.ObservedTicks);
        Assert.Equal(
            first.MatchResult,
            second.MatchResult);
        Assert.Equal(
            first.Metrics.ToArray(),
            second.Metrics.ToArray());
        Assert.Equal(
            first.Milestones.ToArray(),
            second.Milestones.ToArray());
        Assert.Equal(
            first.Debug,
            second.Debug);
    }

    [Fact]
    public void SnapshotRoundTripsThroughJson()
    {
        GameplayTelemetrySnapshot snapshot =
            CaptureSeededTelemetry(
                seed: 121,
                ticks: 160);
        string json =
            JsonSerializer.Serialize(
                snapshot);
        GameplayTelemetrySnapshot? restored =
            JsonSerializer.Deserialize<GameplayTelemetrySnapshot>(
                json);

        Assert.NotNull(restored);
        Assert.Equal(
            snapshot.SchemaVersion,
            restored.SchemaVersion);
        Assert.Equal(
            snapshot.ObservedTicks,
            restored.ObservedTicks);
        Assert.Equal(
            snapshot.MatchResult,
            restored.MatchResult);
        Assert.Equal(
            snapshot.Metrics.ToArray(),
            restored.Metrics.ToArray());
        Assert.Equal(
            snapshot.Milestones.ToArray(),
            restored.Milestones.ToArray());
    }

    [Fact]
    public void BatchAggregationAndComparisonUseStableMetricKeys()
    {
        GameplayTelemetrySnapshot baselineSnapshot =
            CreateSyntheticSnapshot(
                resourceIncome: 100.0,
                durationTicks: 1_000);
        GameplayTelemetrySnapshot currentSnapshot =
            CreateSyntheticSnapshot(
                resourceIncome: 125.0,
                durationTicks: 900);

        var baselineMatches =
            new[]
            {
                new GameplayTelemetryMatch(
                    1,
                    10,
                    baselineSnapshot),
                new GameplayTelemetryMatch(
                    2,
                    11,
                    baselineSnapshot)
            };
        var currentMatches =
            new[]
            {
                new GameplayTelemetryMatch(
                    1,
                    20,
                    currentSnapshot),
                new GameplayTelemetryMatch(
                    2,
                    21,
                    currentSnapshot)
            };

        GameplayTelemetryAnalysisResult baseline =
            GameplayTelemetryAnalysis.Aggregate(
                baselineMatches);
        GameplayTelemetryAnalysisResult current =
            GameplayTelemetryAnalysis.Aggregate(
                currentMatches);
        IReadOnlyList<GameplayMetricComparison> comparison =
            GameplayTelemetryAnalysis.Compare(
                baseline,
                current);

        GameplayMetricAggregate resource =
            Assert.Single(
                current.Metrics,
                static metric =>
                    metric.Name ==
                    GameplayMetricNames.ResourceIncomeQuantity);
        Assert.Equal(
            2,
            resource.SampleCount);
        Assert.Equal(
            125.0,
            resource.Mean,
            precision: 6);

        GameplayMetricComparison delta =
            Assert.Single(
                comparison,
                static metric =>
                    metric.Name ==
                    GameplayMetricNames.ResourceIncomeQuantity);
        Assert.Equal(
            25.0,
            delta.AbsoluteDelta,
            precision: 6);
        Assert.Equal(
            0.25,
            delta.RelativeDelta!.Value,
            precision: 6);
    }

    [Fact]
    public void FreshMatchBatchUsesIndependentSeedsAndAggregatesSamples()
    {
        var matches =
            new List<GameplayTelemetryMatch>();

        const ulong startingSeed = 700;

        for (int matchIndex = 0;
             matchIndex < 2;
             matchIndex++)
        {
            ulong seed =
                startingSeed +
                (ulong)matchIndex;

            using VerticalSliceScenario scenario =
                VerticalSliceScenario.Create(
                    VerticalSliceScenarioSettings.Create(
                        VerticalSliceScenarioProfile.Validation),
                    seed);
            var telemetry =
                new GameplayTelemetryCollector(
                    scenario);

            RunTicks(
                scenario,
                telemetry,
                120);

            matches.Add(
                new GameplayTelemetryMatch(
                    matchIndex + 1,
                    seed,
                    telemetry.Capture()));
        }

        Assert.Equal(
            startingSeed,
            matches[0].Seed);
        Assert.Equal(
            startingSeed + 1,
            matches[1].Seed);
        Assert.Equal(
            120UL,
            matches[0].Telemetry.ObservedTicks);
        Assert.Equal(
            120UL,
            matches[1].Telemetry.ObservedTicks);

        GameplayTelemetryAnalysisResult analysis =
            GameplayTelemetryAnalysis.Aggregate(
                matches);

        GameplayMetricAggregate duration =
            Assert.Single(
                analysis.Metrics,
                static metric =>
                    metric.Name ==
                        GameplayMetricNames.MatchDurationTicks &&
                    metric.Owner ==
                        "match");

        Assert.Equal(
            2,
            duration.SampleCount);
        Assert.Equal(
            120.0,
            duration.Mean,
            precision: 6);
    }

    [Fact]
    public void LongRunGameplayMetricsRemainFiniteAndNonNegative()
    {
        GameplayTelemetrySnapshot snapshot =
            CaptureSeededTelemetry(
                seed: 2026,
                ticks: 2_000);

        Assert.Equal(
            2_000UL,
            snapshot.ObservedTicks);
        Assert.NotEmpty(
            snapshot.Metrics);

        foreach (GameplayMetric metric in snapshot.Metrics)
        {
            Assert.True(
                double.IsFinite(
                    metric.Value),
                $"Metric '{metric.Name}' was not finite.");
            Assert.True(
                metric.Value >= 0.0,
                $"Metric '{metric.Name}' was negative.");
        }
    }

    private static GameplayTelemetrySnapshot CaptureSeededTelemetry(
        ulong seed,
        int ticks)
    {
        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Validation),
                seed);
        var telemetry =
            new GameplayTelemetryCollector(
                scenario);

        RunTicks(
            scenario,
            telemetry,
            ticks);

        return telemetry.Capture();
    }

    private static void RunTicks(
        VerticalSliceScenario scenario,
        GameplayTelemetryCollector telemetry,
        int ticks)
    {
        for (int index = 0;
             index < ticks;
             index++)
        {
            scenario.Simulation.AdvanceOneTick();
            telemetry.Observe();
        }
    }

    private static GameplayTelemetrySnapshot CreateSyntheticSnapshot(
        double resourceIncome,
        double durationTicks) =>
        new(
            GameplayTelemetryCollector.CurrentSchemaVersion,
            (ulong)durationTicks,
            20,
            new GameplayMatchResult(
                "Active",
                "Running",
                "None",
                "None",
                0,
                0,
                0,
                0,
                0,
                1),
            [
                new GameplayMetric(
                    GameplayMetricNames.ResourceIncomeQuantity,
                    "match",
                    "resource.ferrous_ore",
                    resourceIncome,
                    "quantity"),
                new GameplayMetric(
                    GameplayMetricNames.MatchDurationTicks,
                    "match",
                    string.Empty,
                    durationTicks,
                    "ticks")
            ],
            [],
            new GameplayTelemetryDebugSummary(
                new GameplaySupplyDebugSummary(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0.0,
                    0.0),
                new GameplayProductionDebugSummary(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0),
                [],
                []));
}
