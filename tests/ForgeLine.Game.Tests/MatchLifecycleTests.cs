using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchLifecycleTests
{
    private static readonly PlayerId WestPlayer = new(1);
    private static readonly PlayerId EastPlayer = new(2);

    [Fact]
    public void InvalidLifecycleTransitionsAreRejectedWithoutStateMutation()
    {
        var simulation =
            new SimulationCoordinator();
        EntityId stateEntity =
            MatchObjectiveSystem.CreateMatchStateEntity(
                simulation.Entities);

        Assert.Throws<InvalidOperationException>(
            () =>
                MatchObjectiveSystem.ActivateMatch(
                    simulation.Entities,
                    stateEntity));

        Assert.False(
            MatchLifecycle.TrySetPaused(
                simulation.Entities,
                stateEntity,
                paused: true,
                simulation.CurrentTick));
        Assert.Equal(
            MatchState.Initializing,
            simulation.Entities.GetComponent<MatchState>(
                stateEntity));

        MatchObjectiveSystem.MarkMatchReady(
            simulation.Entities,
            stateEntity);

        Assert.Throws<InvalidOperationException>(
            () =>
                MatchObjectiveSystem.MarkMatchReady(
                    simulation.Entities,
                    stateEntity));

        MatchState ready =
            simulation.Entities.GetComponent<MatchState>(
                stateEntity);

        Assert.Equal(
            MatchLifecyclePhase.Ready,
            ready.Lifecycle);
        Assert.Equal(
            1U,
            ready.TransitionCount);
    }

    [Fact]
    public void SurrenderFlowsThroughActionDispatcherCommandGatewayAndResultBuffer()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 9876);
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity);

        scenario.Simulation.RegisterTickObserver(
            gateway);

        Assert.True(
            PlayerActionRequestDispatcher.TryDispatch(
                PlayerActionRequest.Surrender(),
                EastPlayer,
                gateway,
                scenario.Simulation.CurrentTick,
                out PlayerCommandSubmissionReceipt receipt));
        Assert.True(
            receipt.Accepted);
        Assert.Equal(
            PlayerCommandKind.Surrender,
            receipt.Kind);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            receipt.CorrelationId,
            result.CorrelationId);
        Assert.Equal(
            PlayerCommandKind.Surrender,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);

        MatchState state =
            scenario.GetMatchState();

        Assert.Equal(
            MatchLifecyclePhase.Ending,
            state.Lifecycle);
        Assert.Equal(
            MatchOutcome.Victory,
            state.Outcome);
        Assert.Equal(
            MatchTerminationReason.Surrender,
            state.TerminationReason);
        Assert.Equal(
            WestPlayer,
            state.Winner);
        Assert.Equal(
            EastPlayer,
            state.DefeatedPlayer);
        Assert.True(
            scenario.Simulation.Entities.IsAlive(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
    }

    [Fact]
    public void EndMatchFinalizationPreservesAuthoritativeTerminalReason()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 2468);

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
        scenario.Simulation.AdvanceOneTick();

        MatchState ending =
            scenario.GetMatchState();

        Assert.Equal(
            MatchLifecyclePhase.Ending,
            ending.Lifecycle);
        Assert.Equal(
            MatchTerminationReason.CommandCoreDestroyed,
            ending.TerminationReason);

        var finalize =
            new EndMatchCommand(
                WestPlayer,
                scenario.BattlefieldRuntime.MatchStateEntity,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.ExecuteControlCommand(
            finalize);

        MatchState completed =
            scenario.GetMatchState();

        Assert.True(
            finalize.Accepted);
        Assert.Equal(
            MatchLifecyclePhase.Completed,
            completed.Lifecycle);
        Assert.Equal(
            MatchStatus.Ended,
            completed.Status);
        Assert.Equal(
            MatchOutcome.Victory,
            completed.Outcome);
        Assert.Equal(
            MatchTerminationReason.CommandCoreDestroyed,
            completed.TerminationReason);
        Assert.Equal(
            WestPlayer,
            completed.Winner);
        Assert.Equal(
            EastPlayer,
            completed.DefeatedPlayer);
        Assert.Equal(
            ending.CompletedAtTick,
            completed.CompletedAtTick);
        Assert.Equal(
            scenario.Simulation.CurrentTick,
            completed.FinalizedAtTick);
    }

    [Fact]
    public void RestartCreatesFreshSessionAndWorldWithoutTerminalLeakage()
    {
        SimulationSessionId completedSession;

        using (MatchRuntime first =
               CentralDivideScenario.Create(
                   seed: 1234))
        {
            completedSession =
                first.Simulation.SessionId;

            Assert.True(
                first.Simulation.Entities.DestroyEntity(
                    first.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
            first.Simulation.AdvanceOneTick();

            var finalize =
                new EndMatchCommand(
                    WestPlayer,
                    first.BattlefieldRuntime.MatchStateEntity,
                    first.Simulation.CurrentTick);
            first.Simulation.ExecuteControlCommand(
                finalize);

            Assert.True(
                finalize.Accepted);
            Assert.Equal(
                MatchLifecyclePhase.Completed,
                first.GetMatchState().Lifecycle);
        }

        using MatchRuntime restarted =
            CentralDivideScenario.Create(
                seed: 1234);

        MatchState fresh =
            restarted.GetMatchState();

        Assert.NotEqual(
            completedSession,
            restarted.Simulation.SessionId);
        Assert.Equal(
            SimulationTick.Zero,
            restarted.Simulation.CurrentTick);
        Assert.Equal(
            MatchLifecyclePhase.Running,
            fresh.Lifecycle);
        Assert.Equal(
            MatchOutcome.None,
            fresh.Outcome);
        Assert.Equal(
            MatchTerminationReason.None,
            fresh.TerminationReason);
        Assert.Equal(
            PlayerId.None,
            fresh.Winner);
        Assert.Equal(
            PlayerId.None,
            fresh.DefeatedPlayer);
        Assert.Equal(
            SimulationTick.Zero,
            fresh.CompletedAtTick);
        Assert.Equal(
            SimulationTick.Zero,
            fresh.FinalizedAtTick);
        Assert.True(
            restarted.Simulation.Entities.IsAlive(
                restarted.GetBase(new PlayerId(1)).CommandCore));
        Assert.True(
            restarted.Simulation.Entities.IsAlive(
                restarted.GetBase(new PlayerId(2)).CommandCore));
        Assert.Equal(
            0,
            restarted.Simulation.PendingCommandCount);
    }

    [Fact]
    public void InvalidMatchConfigurationRejectsDuplicatePlayersAndStarts()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new MatchConfiguration(
                    "prototype",
                    seed: 1,
                    [
                        new MatchParticipantConfiguration(
                            WestPlayer,
                            new FactionId(1),
                            startIndex: 0,
                            isComputerControlled: false),
                        new MatchParticipantConfiguration(
                            WestPlayer,
                            new FactionId(2),
                            startIndex: 1,
                            isComputerControlled: true)
                    ]));

        Assert.Throws<ArgumentException>(
            () =>
                new MatchConfiguration(
                    "prototype",
                    seed: 1,
                    [
                        new MatchParticipantConfiguration(
                            WestPlayer,
                            new FactionId(1),
                            startIndex: 0,
                            isComputerControlled: false),
                        new MatchParticipantConfiguration(
                            EastPlayer,
                            new FactionId(2),
                            startIndex: 0,
                            isComputerControlled: true)
                    ]));
    }

    [Fact]
    public void LifecycleDiagnosticsCaptureReasonWinnerAndTransitionHistory()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 321);

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
        scenario.Simulation.AdvanceOneTick();

        MatchLifecycleDiagnosticsSnapshot diagnostics =
            MatchLifecycleDiagnosticsSnapshot.Capture(
                scenario.GetMatchState());

        Assert.Equal(
            MatchLifecyclePhase.Ending,
            diagnostics.Lifecycle);
        Assert.Equal(
            MatchOutcome.Victory,
            diagnostics.Outcome);
        Assert.Equal(
            MatchTerminationReason.CommandCoreDestroyed,
            diagnostics.TerminationReason);
        Assert.Equal(
            WestPlayer,
            diagnostics.Winner);
        Assert.Equal(
            EastPlayer,
            diagnostics.DefeatedPlayer);
        Assert.Equal(
            3U,
            diagnostics.TransitionCount);
        Assert.Equal(
            scenario.Simulation.CurrentTick,
            diagnostics.CompletedAtTick);
    }
}
