using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishProgressionHistoryTests
{
    [Fact]
    public void HistoryRetainsOpeningAndBoundedTailWhenTransitionsOverflowCapacity()
    {
        VerticalSliceScenarioSettings settings = VerticalSliceScenarioSettings.Create(VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario scenario = VerticalSliceScenario.Create(settings, seed: 2026);
        scenario.Simulation.RegisterSystem(new DecisionTransitionFixture());
        var diagnostics = new SkirmishProgressionDiagnostics(
            scenario.Inventories, DirectorateContent.CreateUnitCatalog(),
            new Dictionary<PlayerId, SkirmishOpponentConfiguration>
            {
                [scenario.West.Player] = settings.WestOpponent,
                [scenario.East.Player] = settings.EastOpponent
            });
        scenario.Simulation.RegisterSystem(diagnostics);
        scenario.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        SkirmishDecisionDiagnostic[] opening = diagnostics.History.ToArray();
        Assert.Equal(32, opening.Length);

        scenario.Simulation.RunTicks(144, TestContext.Current.CancellationToken);

        Assert.Equal(320UL, diagnostics.ObservedDecisions);
        Assert.Equal(SkirmishProgressionDiagnostics.MaximumHistoryEntries, diagnostics.History.Count);
        Assert.Equal(320UL - (ulong)diagnostics.History.Count, diagnostics.DroppedHistoryEntries);
        Assert.Equal(opening, diagnostics.History.Take(opening.Length).ToArray());
        Assert.Equal(160UL, diagnostics.History[^1].Tick);
        Assert.All(diagnostics.Latest, entry => Assert.Equal(160UL, entry.Tick));
        Assert.All(diagnostics.History, entry =>
        {
            Assert.InRange(entry.Economy.Industry.Count, 0, 32);
            Assert.InRange(entry.Economy.Extractors.Count, 0, 32);
        });
    }

    private sealed class DecisionTransitionFixture : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.AiDecisions;

        public void Execute(SimulationContext context)
        {
            foreach (EntityId entity in context.Entities.Query<SkirmishOpponentState>(QueryIterationOrder.StableByEntityIndex))
            {
                SkirmishOpponentState state = context.Entities.GetComponent<SkirmishOpponentState>(entity);
                context.Entities.SetComponent(entity, state with
                {
                    LastDecisionTick = context.Tick,
                    ActiveGoal = context.Tick.Value % 2 == 0
                        ? SkirmishStrategicGoal.Scout
                        : SkirmishStrategicGoal.PrepareOffensive
                });
            }
        }
    }
}
