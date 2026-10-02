using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CombinedArmsCombatScenarioTests
{
    [Fact]
    public void ScoutIdentificationEnablesArtilleryStrikeBeyondArtilleryVision()
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed: 5701) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        using VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtime,
                TestContext.Current.CancellationToken);

        WorldTransform westCore =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.West.CommandCore);
        WorldTransform eastCore =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.East.CommandCore);

        Vector3 direction =
            eastCore.Position -
            westCore.Position;
        direction.Y = 0.0f;
        direction =
            Vector3.Normalize(
                direction);

        Vector3 artilleryPosition =
            westCore.Position +
            direction * 20.0f;
        Vector3 scoutPosition =
            westCore.Position +
            direction * 50.0f;
        Vector3 enemyPosition =
            westCore.Position +
            direction * 280.0f;

        EntityId artillery =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.MobileArtillery],
                artilleryPosition,
                scenario.West.Player);
        EntityId enemy =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.RifleSquad],
                enemyPosition,
                scenario.East.Player);

        scenario.Simulation.RunTicks(
            6,
            TestContext.Current.CancellationToken);

        FactionId westFaction =
            new((uint)scenario.West.Player.Value);
        IntelligenceContactKey contactKey =
            IntelligenceContactKey.FromEntity(
                enemy);

        bool identifiedWithoutScout =
            scenario.Intelligence.TryResolveCurrentlyIdentifiedEntity(
                westFaction,
                contactKey,
                out _);

        Assert.False(identifiedWithoutScout);

        _ =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.ScoutVehicle],
                scoutPosition,
                scenario.West.Player);

        scenario.Simulation.RunTicks(
            6,
            TestContext.Current.CancellationToken);

        Assert.True(
            scenario.Intelligence.TryResolveCurrentlyIdentifiedEntity(
                westFaction,
                contactKey,
                out EntityId identified));
        Assert.Equal(enemy, identified);

        HealthState initialHealth =
            scenario.Simulation.Entities
                .GetComponent<HealthState>(
                    enemy);

        PlayerTacticalActionCommand mission =
            PlayerTacticalActionCommand.FireMissionContact(
                scenario.West.Player,
                [artillery],
                contactKey,
                requestedRounds: 1,
                scenario.Simulation.CurrentTick,
                scenario.Intelligence,
                scenario.Services.Weapons,
                scenario.Services.ArtilleryWeapons);

        scenario.Simulation.SubmitCommand(
            mission,
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.RunTicks(
            100,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            mission.AcceptedTargetCount);

        bool damaged =
            !scenario.Simulation.Entities.IsAlive(
                enemy) ||
            scenario.Simulation.Entities
                .GetComponent<HealthState>(
                    enemy)
                .Current <
            initialHealth.Current;

        Assert.True(
            damaged,
            "Reconnaissance identification did not produce an effective artillery engagement.");
    }
}
