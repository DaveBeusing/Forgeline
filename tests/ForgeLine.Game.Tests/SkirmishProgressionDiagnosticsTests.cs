using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishProgressionDiagnosticsTests
{
    [Fact]
    public void DecisionCaptureDoesNotChangeSeededProgression()
    {
        MatchScenarioSettings settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        MatchRuntime observed = CentralDivideScenario.Create(settings, seed: 2026);
        MatchRuntime control = CentralDivideScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics diagnostics = Attach(observed, settings);

        observed.Simulation.RunTicks(64, TestContext.Current.CancellationToken);
        control.Simulation.RunTicks(64, TestContext.Current.CancellationToken);

        Assert.NotEmpty(diagnostics.History);
        Assert.Equal(control.GetOpponentState(control.GetBase(new PlayerId(1)).Player), observed.GetOpponentState(observed.GetBase(new PlayerId(1)).Player));
        Assert.Equal(control.GetOpponentState(control.GetBase(new PlayerId(2)).Player), observed.GetOpponentState(observed.GetBase(new PlayerId(2)).Player));
        Assert.Equal(control.GetMatchState(), observed.GetMatchState());
        Assert.Equal(control.Simulation.Entities.EntityCount, observed.Simulation.Entities.EntityCount);
        Assert.Equal(
            (ulong)(observed.GetOpponentState(observed.GetBase(new PlayerId(1)).Player).DecisionsTaken +
                observed.GetOpponentState(observed.GetBase(new PlayerId(2)).Player).DecisionsTaken),
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
        MatchScenarioSettings settings = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation);
        MatchRuntime first = CentralDivideScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics firstDiagnostics = Attach(first, settings);
        first.Simulation.RunTicks(12, TestContext.Current.CancellationToken);
        Assert.NotEmpty(firstDiagnostics.History);

        MatchRuntime second = CentralDivideScenario.Create(settings, seed: 2026);
        SkirmishProgressionDiagnostics secondDiagnostics = Attach(second, settings);
        Assert.Empty(secondDiagnostics.History);
        Assert.Empty(secondDiagnostics.Latest);
        Assert.Empty(secondDiagnostics.FirstEligibilityLosses);
        Assert.Equal(0UL, secondDiagnostics.ObservedDecisions);
        Assert.Equal(0UL, secondDiagnostics.DroppedHistoryEntries);
    }

    private static SkirmishProgressionDiagnostics Attach(
        MatchRuntime scenario,
        MatchScenarioSettings settings)
    {
        var diagnostics = new SkirmishProgressionDiagnostics(
            scenario.Inventories,
            DirectorateContent.CreateUnitCatalog(),
            new Dictionary<PlayerId, SkirmishOpponentConfiguration>
            {
                [scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player] = settings.OpponentConfigurations[1],
                [scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player] = settings.OpponentConfigurations[2]
            });
        scenario.Simulation.RegisterSystem(diagnostics);
        return diagnostics;
    }
}
