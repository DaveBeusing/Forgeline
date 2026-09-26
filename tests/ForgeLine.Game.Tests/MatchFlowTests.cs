using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchFlowTests
{
    private static readonly PlayerId PlayerOne = new(1);
    private static readonly PlayerId PlayerTwo = new(2);

    [Fact]
    public void MatchLoadsBeforeObjectivesAndActivatesAfterObjectiveSetup()
    {
        using MatchFixture fixture = MatchFixture.Create();

        MatchState loading =
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity);

        Assert.Equal(
            MatchStatus.Loading,
            loading.Status);

        fixture.AttachObjectives();

        MatchState active =
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity);

        Assert.Equal(
            MatchStatus.Active,
            active.Status);
        Assert.Equal(
            PlayerId.None,
            active.Winner);
    }

    [Fact]
    public void CommandCoreLossProducesVictoryAndPlayerRelativeDefeat()
    {
        using MatchFixture fixture = MatchFixture.Create();

        fixture.AttachObjectives();
        fixture.RegisterObjectiveSystem();
        fixture.Simulation.AdvanceOneTick();

        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                fixture.CommandCores[PlayerTwo]));

        fixture.Simulation.AdvanceOneTick();

        MatchState completed =
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity);

        Assert.Equal(
            MatchStatus.Victory,
            completed.Status);
        Assert.Equal(
            PlayerOne,
            completed.Winner);
        Assert.Equal(
            PlayerMatchStatus.Victory,
            completed.ForPlayer(PlayerOne));
        Assert.Equal(
            PlayerMatchStatus.Defeat,
            completed.ForPlayer(PlayerTwo));
        Assert.True(
            completed.CompletedAtTick >
            SimulationTick.Zero);
    }

    [Fact]
    public void SimultaneousCommandCoreLossResolvesAsDeterministicDraw()
    {
        using MatchFixture fixture = MatchFixture.Create();

        fixture.AttachObjectives();
        fixture.RegisterObjectiveSystem();

        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                fixture.CommandCores[PlayerOne]));
        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                fixture.CommandCores[PlayerTwo]));

        fixture.Simulation.AdvanceOneTick();

        MatchState completed =
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity);

        Assert.Equal(
            MatchStatus.Draw,
            completed.Status);
        Assert.Equal(
            PlayerId.None,
            completed.Winner);
        Assert.Equal(
            PlayerMatchStatus.Draw,
            completed.ForPlayer(PlayerOne));
        Assert.Equal(
            PlayerMatchStatus.Draw,
            completed.ForPlayer(PlayerTwo));
    }

    [Fact]
    public void EndMatchCommandOnlyEndsACompletedMatch()
    {
        using MatchFixture fixture = MatchFixture.Create();

        fixture.AttachObjectives();
        fixture.RegisterObjectiveSystem();

        var rejected =
            new EndMatchCommand(
                PlayerOne,
                fixture.Runtime.MatchStateEntity,
                fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(
            rejected,
            fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();

        Assert.False(rejected.Accepted);
        Assert.Equal(
            MatchStatus.Active,
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity).Status);

        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                fixture.CommandCores[PlayerTwo]));
        fixture.Simulation.AdvanceOneTick();

        var accepted =
            new EndMatchCommand(
                PlayerOne,
                fixture.Runtime.MatchStateEntity,
                fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(
            accepted,
            fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();

        MatchState ended =
            fixture.Simulation.Entities.GetComponent<MatchState>(
                fixture.Runtime.MatchStateEntity);

        Assert.True(accepted.Accepted);
        Assert.Equal(
            MatchStatus.Ended,
            ended.Status);
        Assert.Equal(
            PlayerOne,
            ended.Winner);
        Assert.NotEqual(
            SimulationTick.Zero,
            ended.CompletedAtTick);
    }

    [Fact]
    public void NewMatchStateDoesNotRetainPreviousTerminalState()
    {
        var firstSimulation =
            new SimulationCoordinator();
        EntityId firstState =
            MatchObjectiveSystem.CreateMatchStateEntity(
                firstSimulation.Entities);
        firstSimulation.Entities.SetComponent(
            firstState,
            new MatchState(
                MatchStatus.Victory,
                PlayerOne,
                new SimulationTick(42)));

        var restartedSimulation =
            new SimulationCoordinator();
        EntityId restartedState =
            MatchObjectiveSystem.CreateMatchStateEntity(
                restartedSimulation.Entities);
        MatchState state =
            restartedSimulation.Entities.GetComponent<MatchState>(
                restartedState);

        Assert.Equal(
            MatchState.Loading,
            state);
        Assert.Equal(
            PlayerId.None,
            state.Winner);
        Assert.Equal(
            SimulationTick.Zero,
            state.CompletedAtTick);
    }

    [Fact]
    public void StartingBasesUsePlayerScopedPowerNetworks()
    {
        SkirmishScenarioHarness harness =
            SkirmishScenarioHarness.Create();

        PowerNetworkMembership west =
            harness.Simulation.Entities.GetComponent<PowerNetworkMembership>(
                harness.West.CommandCore);
        PowerNetworkMembership east =
            harness.Simulation.Entities.GetComponent<PowerNetworkMembership>(
                harness.East.CommandCore);

        Assert.Equal(
            new PowerNetworkId(1),
            west.NetworkId);
        Assert.Equal(
            new PowerNetworkId(2),
            east.NetworkId);
        Assert.NotEqual(
            west.NetworkId,
            east.NetworkId);
    }

    [Fact]
    public void VerticalSliceConfigurationMatchesPrototypeAssignments()
    {
        PrototypeBattlefieldDefinition battlefield =
            PrototypeBattlefieldDefinition.Create();

        MatchConfiguration configuration =
            MatchConfiguration.CreateVerticalSlice(
                battlefield,
                seed: 12345);

        Assert.Equal(
            battlefield.Metadata.Key,
            configuration.MapKey);
        Assert.Equal(
            12345UL,
            configuration.Seed);
        Assert.Collection(
            configuration.Participants,
            west =>
            {
                Assert.Equal(PlayerOne, west.Player);
                Assert.Equal(new FactionId(1), west.Faction);
                Assert.Equal(0, west.StartIndex);
                Assert.False(west.IsComputerControlled);
            },
            east =>
            {
                Assert.Equal(PlayerTwo, east.Player);
                Assert.Equal(new FactionId(2), east.Faction);
                Assert.Equal(1, east.StartIndex);
                Assert.True(east.IsComputerControlled);
            });
    }

    private sealed class MatchFixture : IDisposable
    {
        private MatchFixture(
            SimulationCoordinator simulation,
            PrototypeBattlefieldRuntime runtime,
            Dictionary<PlayerId, EntityId> commandCores)
        {
            Simulation = simulation;
            Runtime = runtime;
            CommandCores = commandCores;
        }

        public SimulationCoordinator Simulation { get; }

        public PrototypeBattlefieldRuntime Runtime { get; }

        public Dictionary<PlayerId, EntityId> CommandCores { get; }

        public static MatchFixture Create()
        {
            PrototypeBattlefieldDefinition definition =
                PrototypeBattlefieldDefinition.Create();
            var simulation =
                new SimulationCoordinator();
            var logistics =
                new LogisticsNetwork();
            PrototypeBattlefieldRuntime runtime =
                PrototypeBattlefieldRuntime.Load(
                    simulation.Entities,
                    definition,
                    PrototypeBattlefieldTerrainFactory.Create(
                        definition),
                    logistics);
            var commandCores =
                new Dictionary<PlayerId, EntityId>();

            for (int index = 0;
                 index < definition.Objectives.Count;
                 index++)
            {
                BattlefieldObjectiveDefinition objective =
                    definition.Objectives[index];
                EntityId commandCore =
                    simulation.Entities.CreateEntity();

                simulation.Entities.AddComponent(
                    commandCore,
                    new CompletedBuilding(
                        BuildingIds.CommandCore,
                        objective.Owner,
                        SimulationTick.Zero));
                simulation.Entities.AddComponent(
                    commandCore,
                    new CommandFacility());

                commandCores.Add(
                    objective.Owner,
                    commandCore);
            }

            return new MatchFixture(
                simulation,
                runtime,
                commandCores);
        }

        public void AttachObjectives()
        {
            _ = Runtime.AttachCommandCoreObjectives(
                Simulation.Entities,
                CommandCores);
        }

        public void RegisterObjectiveSystem()
        {
            Simulation.RegisterSystem(
                new MatchObjectiveSystem(
                    Runtime.MatchStateEntity));
        }

        public void Dispose()
        {
        }
    }
}
