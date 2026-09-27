using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishProgressionDiagnosticsTests
{
    [Fact]
    public void DecisionCaptureDoesNotChangeSeededProgression()
    {
        VerticalSliceScenarioSettings settings = VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario observed = VerticalSliceScenario.Create(settings, seed: 2026);
        VerticalSliceScenario control = VerticalSliceScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics diagnostics = Attach(observed, settings);

        observed.Simulation.RunTicks(64, TestContext.Current.CancellationToken);
        control.Simulation.RunTicks(64, TestContext.Current.CancellationToken);

        Assert.NotEmpty(diagnostics.History);
        Assert.Equal(control.GetOpponentState(control.West.Player), observed.GetOpponentState(observed.West.Player));
        Assert.Equal(control.GetOpponentState(control.East.Player), observed.GetOpponentState(observed.East.Player));
        Assert.Equal(control.GetMatchState(), observed.GetMatchState());
        Assert.Equal(control.Simulation.Entities.EntityCount, observed.Simulation.Entities.EntityCount);
        Assert.Equal(
            (ulong)(observed.GetOpponentState(observed.West.Player).DecisionsTaken +
                observed.GetOpponentState(observed.East.Player).DecisionsTaken),
            diagnostics.ObservedDecisions);
        Assert.True(diagnostics.ObservedDecisions < 64UL * 2UL);
        Assert.All(diagnostics.History, entry =>
        {
            Assert.Equal(entry.Units.Count(static unit => unit.Eligible), entry.EligibleAttackers);
            Assert.Equal(entry.Units.Count(static unit => unit.Exclusions == "Scout"), entry.ScoutExclusions);
            Assert.InRange(entry.Units.Count, 0, SkirmishProgressionDiagnostics.MaximumUnitDetails);
        });
    }

    [Fact]
    public void NewObserverDoesNotRetainPreviousSessionDecisions()
    {
        VerticalSliceScenarioSettings settings = VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario first = VerticalSliceScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics firstDiagnostics = Attach(first, settings);
        first.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.NotEmpty(firstDiagnostics.History);

        VerticalSliceScenario second = VerticalSliceScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics secondDiagnostics = Attach(second, settings);
        Assert.Empty(secondDiagnostics.History);
        Assert.Empty(secondDiagnostics.Latest);
        Assert.Empty(secondDiagnostics.FirstEligibilityLosses);
        Assert.Equal(0UL, secondDiagnostics.ObservedDecisions);
        Assert.Equal(0UL, secondDiagnostics.DroppedHistoryEntries);
    }

    private static SkirmishProgressionDiagnostics Attach(
        VerticalSliceScenario scenario,
        VerticalSliceScenarioSettings settings)
    {
        var diagnostics = new SkirmishProgressionDiagnostics(
            scenario.Inventories,
            DirectorateContent.CreateUnitCatalog(),
            new Dictionary<PlayerId, SkirmishOpponentConfiguration>
            {
                [scenario.West.Player] = settings.WestOpponent,
                [scenario.East.Player] = settings.EastOpponent
            });
        scenario.Simulation.RegisterSystem(diagnostics);
        return diagnostics;
    }
}
