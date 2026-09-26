using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VerticalSliceReadinessTests
{
    [Fact]
    public void ValidationProfileDoesNotReplaceGameplayDefaults()
    {
        VerticalSliceScenarioSettings gameplay =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Gameplay);
        VerticalSliceScenarioSettings validation =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);

        Assert.Equal(
            SkirmishStartingStock.Standard,
            gameplay.StartingStock);
        Assert.Equal(
            20U,
            gameplay.WestOpponent.ReactionCadenceTicks);
        Assert.Equal(
            0.65,
            gameplay.WestOpponent.Aggression,
            precision: 6);

        Assert.NotEqual(
            gameplay.StartingStock,
            validation.StartingStock);
        Assert.NotEqual(
            gameplay.WestOpponent,
            validation.WestOpponent);
        Assert.NotEqual(
            validation.WestOpponent,
            validation.EastOpponent);
    }

    [Fact]
    public void ValidationAttackerBuildsAttackForceAndEstablishesContact()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                seed: 2026);

        VerticalSliceScenarioSettings settings =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);

        bool progressed =
            scenario.RunUntil(
                current =>
                    current.CountUnits(
                        current.West.Player,
                        UnitIds.MainBattleTank) >=
                    settings.WestOpponent.MinimumAttackUnits &&
                    current.Intelligence.GetContactCount(
                        current.West.Faction) > 0,
                maximumTicks: 50_000,
                TestContext.Current.CancellationToken);

        Assert.True(progressed);
        Assert.NotEqual(
            SkirmishStrategicGoal.RecoverSupply,
            scenario.GetOpponentState(
                scenario.West.Player).ActiveGoal);
    }

    [Fact]
    public void FreshVerticalSliceSessionDoesNotRetainTerminalState()
    {
        VerticalSliceScenarioSettings settings =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);
        VerticalSliceScenario first =
            VerticalSliceScenario.Create(
                settings,
                seed: 2026);

        Assert.True(
            first.Simulation.Entities.DestroyEntity(
                first.East.CommandCore));
        first.Simulation.AdvanceOneTick();

        MatchState completed =
            first.GetMatchState();

        Assert.True(completed.IsTerminal);
        Assert.Equal(
            first.West.Player,
            completed.Winner);

        VerticalSliceScenario restarted =
            VerticalSliceScenario.Create(
                settings,
                seed: 2026);
        MatchState fresh =
            restarted.GetMatchState();

        Assert.Equal(
            MatchStatus.Active,
            fresh.Status);
        Assert.Equal(
            SimulationTick.Zero,
            restarted.Simulation.CurrentTick);
        Assert.True(
            restarted.Simulation.Entities.IsAlive(
                restarted.West.CommandCore));
        Assert.True(
            restarted.Simulation.Entities.IsAlive(
                restarted.East.CommandCore));
    }
}
