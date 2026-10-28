using ForgeLine.Combat;
using ForgeLine.Core;
using System.Text.Json;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchPacingBenchmarksTests
{
    [Fact]
    public void TechnologyUnlockUsesTheActualCapabilityTimestampRatherThanResearchInference()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 38), TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        runtime.Simulation.AdvanceOneTick();
        runtime.Simulation.Entities.AddComponent(runtime.Simulation.Entities.CreateEntity(),
            new TechnologyCapabilityUnlock(new(1), TechnologyCapabilityIds.FieldEngineering,
                TechnologyIds.IndustrialStandardization, runtime.Simulation.CurrentTick));
        telemetry.Observe();
        var rows = MatchPacingAnalysis.Measure(telemetry.Capture());
        Assert.Equal(.05, Assert.Single(rows, r => r.Name == "technology.first_unlock" && r.Owner == "player:1").Value);
        Assert.Equal("Unreached", Assert.Single(rows, r => r.Name == "research.first_completed" && r.Owner == "player:1").Availability);
    }

    [Fact]
    public void ExistingTelemetryDoesNotEnableOfflineDecisionObservation()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 39), TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(runtime);
        runtime.Simulation.AdvanceOneTick(); telemetry.Observe();
        Assert.Null(telemetry.Capture().Decisions);
        Assert.DoesNotContain("Decisions", JsonSerializer.Serialize(telemetry.Capture()));
    }

    [Fact]
    public void StartingStructuresOutsideHomeAreNotCompletedExpansionEvidence()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 40), TestContext.Current.CancellationToken);
        var core = runtime.GetBase(new(1)).CommandCore;
        var transform = runtime.Simulation.Entities.GetComponent<WorldTransform>(core);
        var area = runtime.Battlefield.Starts[0].BuildArea;
        runtime.Simulation.Entities.SetComponent(core, transform with { Position = transform.Position with { X = area.Maximum.X + 10 } });
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        runtime.Simulation.AdvanceOneTick(); telemetry.Observe();
        Assert.Null(Assert.Single(telemetry.Capture().Pacing!, p => p.Owner == "player:1" && p.Name == GameplayPacingNames.StrategicExpansion).Tick);
    }

    [Fact]
    public void UnreachedAndUnavailableAreNullableAndExcludedFromDistributions()
    {
        var snapshot = Capture(42, 4, withoutFront: true);
        var rows = MatchPacingAnalysis.Measure(snapshot);
        var scout = Assert.Single(rows, r => r.Name == "scout.first_produced" && r.Owner == "player:1");
        Assert.Equal("Unreached", scout.Availability); Assert.Null(scout.Value);
        var unsupported = MatchPacingAnalysis.Measure(snapshot with { Decisions = snapshot.Decisions! with { ScoutAvailable = false } });
        Assert.Equal("Unavailable", Assert.Single(unsupported, r => r.Name == scout.Name && r.Owner == scout.Owner).Availability);
        var aggregates = MatchPacingAnalysis.Aggregate([rows, unsupported]);
        var distribution = Assert.Single(aggregates, r => r.Name == scout.Name && r.Owner == scout.Owner);
        Assert.Equal(0, distribution.Reached); Assert.Equal(1, distribution.Unreached); Assert.Equal(1, distribution.Unavailable);
        Assert.Null(distribution.Mean); Assert.Null(distribution.P90);
        Assert.All(snapshot.Decisions!.Players, p => Assert.Null(p.FrontTravelMeters));
    }

    [Fact]
    public void SeededCompletedTickObservationsAreStableAndDoNotChangeGameplay()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 52);
        using var observed = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        using var plain = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(observed, captureDecisionBenchmarks: true);
        for (int i = 0; i < 40; i++)
        { observed.Simulation.AdvanceOneTick(); telemetry.Observe(); plain.Simulation.AdvanceOneTick(); }
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(plain).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(observed).ComputeSha256());
        Assert.Equal(JsonSerializer.Serialize(telemetry.Capture().Decisions), JsonSerializer.Serialize(Capture(52, 40).Decisions));
        string before = JsonSerializer.Serialize(telemetry.Capture().Decisions);
        telemetry.Observe(); // Host pause/repeated read: no completed tick passed.
        Assert.Equal(before, JsonSerializer.Serialize(telemetry.Capture().Decisions));
    }

    [Fact]
    public void DelayedContactAndSustainedSupplyPressureProduceAdvisoryWarnings()
    {
        var snapshot = Capture(62, 2);
        var decisions = snapshot.Decisions!;
        snapshot = snapshot with
        {
            Decisions = decisions with { ObservedTicks = 20000, EndTick = 20000,
                Players = decisions.Players.Select(p => p with { SupplyUnitTicks = 20000, LongestSupplyShortageTicks = 3000 }).ToArray() },
            Pacing = snapshot.Pacing!.Select(p => p.Name == GameplayPacingNames.AuthorizedContact ? p with { Tick = 15000 } : p).ToArray()
        };
        var warnings = MatchPacingAnalysis.Warnings(MatchPacingAnalysis.Measure(snapshot), 1000, new(), true);
        Assert.Contains(warnings, w => w.Code == "late_contact");
        Assert.Contains(warnings, w => w.Code == "sustained_supply_pressure");
        var shortHorizon = MatchPacingAnalysis.Warnings(MatchPacingAnalysis.Measure(Capture(62, 2)), .1, new(), true);
        Assert.DoesNotContain(shortHorizon, w => w.Code == "late_contact");
    }

    [Fact]
    public void GapsAndBudgetExhaustionDoNotInventHealthySamplesOrInactivity()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 63), TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken); telemetry.Observe();
        Assert.Equal(2UL, telemetry.Capture().Decisions!.MissedTicks);
        Assert.All(MatchPacingAnalysis.Measure(telemetry.Capture()), r => Assert.Equal("Unavailable", r.Availability));
        var entities = runtime.Simulation.Entities;
        for (int i = 0; i < 4100; i++) entities.AddComponent(entities.CreateEntity(), new ControllableEntity(new(1), ControllableEntityCategory.Unit));
        runtime.Simulation.AdvanceOneTick(); telemetry.Observe();
        Assert.Equal(1UL, telemetry.Capture().Decisions!.TruncatedTicks);
        Assert.Empty(MatchPacingAnalysis.Warnings(MatchPacingAnalysis.Measure(telemetry.Capture()), 10000, new(), true));
    }

    [Fact]
    public void RealCompletedTickSupplyStatesAccumulatePressureWithoutMutatingThem()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 64), TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        var entity = runtime.Simulation.Entities.CreateEntity();
        runtime.Simulation.Entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
        for (int i = 0; i < 4; i++)
        {
            runtime.Simulation.AdvanceOneTick();
            var state = new UnitSupplyState(0, 0, BattlefieldSupplyStatus.Unsupplied, runtime.Simulation.CurrentTick);
            if (runtime.Simulation.Entities.HasComponent<UnitSupplyState>(entity)) runtime.Simulation.Entities.SetComponent(entity, state);
            else runtime.Simulation.Entities.AddComponent(entity, state);
            telemetry.Observe();
            Assert.Equal(state, runtime.Simulation.Entities.GetComponent<UnitSupplyState>(entity));
        }
        var observation = Assert.Single(telemetry.Capture().Decisions!.Players, p => p.Player == 1);
        Assert.Equal(4UL, observation.LongestSupplyShortageTicks); Assert.Equal(1UL, observation.FirstSupplyShortageTick);
    }

    [Fact]
    public void DistributionQuantilesPreserveCensoringAndThresholdsRejectInvalidValues()
    {
        IReadOnlyList<PacingMeasurement> a = [new("contact", "player:1", "Reached", 10, "seconds", "")];
        IReadOnlyList<PacingMeasurement> b = [new("contact", "player:1", "Reached", 30, "seconds", "")];
        IReadOnlyList<PacingMeasurement> c = [new("contact", "player:1", "Unreached", null, "seconds", "")];
        var summary = Assert.Single(MatchPacingAnalysis.Aggregate([a, b, c]));
        Assert.Equal(20, summary.Median); Assert.Equal(28, summary.P90); Assert.Equal(1, summary.Unreached);
        Assert.Throws<ArgumentException>(() => new PacingThresholds { FirstContactMaximumSeconds = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new PacingThresholds { FactoryUptimeMinimumRatio = 2 }.Validate());
    }

    [Fact]
    public void SimulationFailureRetainsOnlyThePreviousCompletedObservation()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 37), TestContext.Current.CancellationToken);
        runtime.Simulation.RegisterSystem(new FaultOnSecondTick());
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        runtime.Simulation.AdvanceOneTick(); telemetry.Observe();
        string previous = JsonSerializer.Serialize(telemetry.CaptureDecisionObservation());
        Assert.Throws<InvalidOperationException>(() => runtime.Simulation.AdvanceOneTick());
        Assert.Equal(previous, JsonSerializer.Serialize(telemetry.CaptureDecisionObservation()));
    }

    private sealed class FaultOnSecondTick : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.SnapshotEvents;
        public void Execute(SimulationContext context)
        { if (context.Tick.Value == 2) throw new InvalidOperationException("Injected simulation failure."); }
    }

    private static GameplayTelemetrySnapshot Capture(ulong seed, int ticks, bool withoutFront = false)
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed), TestContext.Current.CancellationToken);
        if (withoutFront)
        {
            var members = new List<EntityId>();
            foreach (var entity in runtime.Simulation.Entities.Query<Combatant>()) members.Add(entity);
            foreach (var entity in members) runtime.Simulation.Entities.RemoveComponent<Combatant>(entity);
        }
        var telemetry = new GameplayTelemetryCollector(runtime, captureDecisionBenchmarks: true);
        for (int i = 0; i < ticks; i++) { runtime.Simulation.AdvanceOneTick(); telemetry.Observe(); }
        return telemetry.Capture();
    }
}
