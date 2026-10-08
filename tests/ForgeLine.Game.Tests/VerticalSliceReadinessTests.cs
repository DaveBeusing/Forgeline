using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VerticalSliceReadinessTests
{
    [Fact]
    public void HeadlessRuntimeUsesTwoComputerControlledParticipants()
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Validation,
                seed: 2026);

        Assert.Equal(2, runtime.Participants.Count);
        Assert.All(
            runtime.Participants,
            static participant =>
                Assert.True(
                    participant.IsComputerControlled));
        Assert.NotEqual(
            runtime.Participants[0].Player,
            runtime.Participants[1].Player);
        Assert.NotEqual(
            runtime.Participants[0].StartIndex,
            runtime.Participants[1].StartIndex);
    }

    [Fact]
    public void ValidationOpponentBalancesLogisticsAndObjectivePressure()
    {
        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);

        Assert.Equal(
            2,
            settings.OpponentConfigurations[1].MinimumCargoTrucks);
        Assert.Equal(
            2,
            settings.OpponentConfigurations[2].MinimumCargoTrucks);
        Assert.Equal(
            2,
            settings.OpponentConfigurations[1].MinimumSupplyTrucks);
        Assert.Equal(
            2,
            settings.OpponentConfigurations[2].MinimumSupplyTrucks);
        Assert.Equal(
            1,
            settings.OpponentConfigurations[1].MinimumObjectivePressureUnits);
        Assert.Equal(
            2,
            settings.OpponentConfigurations[2].MinimumObjectivePressureUnits);
        Assert.True(
            settings.OpponentConfigurations[1].OffensiveFuelThreshold <
            0.55);
        Assert.True(
            settings.OpponentConfigurations[2].OffensiveFuelThreshold <
            0.55);
    }

    [Fact]
    public void ValidationProfileDoesNotReplaceGameplayDefaults()
    {
        MatchScenarioSettings gameplay =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Gameplay);
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);

        Assert.Equal(
            SkirmishStartingStock.Standard,
            gameplay.StartingStock);
        Assert.Equal(
            20U,
            gameplay.OpponentConfigurations[1].ReactionCadenceTicks);
        Assert.Equal(
            0.65,
            gameplay.OpponentConfigurations[1].Aggression,
            precision: 6);

        Assert.NotEqual(
            gameplay.StartingStock,
            validation.StartingStock);
        Assert.NotEqual(
            gameplay.OpponentConfigurations[1],
            validation.OpponentConfigurations[1]);
        Assert.NotEqual(
            validation.OpponentConfigurations[1],
            validation.OpponentConfigurations[2]);
    }

    [Fact]
    public void ValidationAttackerEstablishesSupplyAndContact()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                seed: 2026);

        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);

        bool progressed =
            scenario.RunUntil(
                current =>
                    current.CountBuildings(
                        current.West.Player,
                        BuildingIds.SupplyDepot) >= 1 &&
                    current.Intelligence.GetContactCount(
                        current.West.Faction) > 0 &&
                    current.GetOpponentState(
                        current.West.Player).ActiveGoal ==
                    SkirmishStrategicGoal.AttackObjective,
                maximumTicks: 50_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            progressed,
            $"Readiness stalled: depots={scenario.CountBuildings(scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player, BuildingIds.SupplyDepot)}; " +
            $"tanks={scenario.CountUnits(scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player, UnitIds.MainBattleTank)}; " +
            $"contacts={scenario.Intelligence.GetContactCount(scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Faction)}; " +
            $"goal={scenario.GetOpponentState(scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).ActiveGoal}.");
        Assert.True(
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).ExpansionSiteCursor > 0,
            "Validation opponent never established a canonical-map expansion.");

        Assert.NotEqual(
            SkirmishStrategicGoal.RecoverSupply,
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).ActiveGoal);
    }

    [Fact]
    public void FreshVerticalSliceSessionDoesNotRetainTerminalState()
    {
        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        MatchRuntime first =
            CentralDivideScenario.Create(
                settings,
                seed: 2026);

        Assert.True(
            first.Simulation.Entities.DestroyEntity(
                first.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
        first.Simulation.AdvanceOneTick();

        MatchState completed =
            first.GetMatchState();

        Assert.True(completed.IsTerminal);
        Assert.Equal(
            first.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
            completed.Winner);

        MatchRuntime restarted =
            CentralDivideScenario.Create(
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
                restarted.GetBase(new PlayerId(1)).CommandCore));
        Assert.True(
            restarted.Simulation.Entities.IsAlive(
                restarted.GetBase(new PlayerId(2)).CommandCore));
    }
}
