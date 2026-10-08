using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchPersistenceTests
{
    [Fact]
    public void ActiveMatchSaveRoundTripsAndContinuesDeterministically()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 2026);

        bool reachedActiveState =
            original.RunUntil(
                scenario =>
                    scenario.Simulation.Entities
                        .GetComponentCount<ProductionRequest>() > 0 ||
                    scenario.Simulation.Entities
                        .GetComponentCount<CargoTransportOrder>() > 0,
                maximumTicks: 5_000,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(
            reachedActiveState);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);
        string document =
            MatchPersistenceSerializer.SerializeSave(
                save);
        MatchSaveData restoredData =
            MatchPersistenceSerializer.DeserializeSave(
                document);

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                restoredData);

        Assert.Equal(
            original.Simulation.CurrentTick,
            restored.Simulation.CurrentTick);
        Assert.Equal(
            original.Simulation.Random.State,
            restored.Simulation.Random.State);
        Assert.Equal(
            original.GetMatchState(),
            restored.GetMatchState());

        MatchAuthoritativeSnapshot expected =
            MatchAuthoritativeSnapshot.Capture(
                original);
        MatchAuthoritativeSnapshot actual =
            MatchAuthoritativeSnapshot.Capture(
                restored);

        Assert.True(
            MatchPersistenceService.Compare(
                expected,
                actual).Compatible);

        original.Simulation.RunTicks(
            250,
            TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(
            250,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            MatchAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            MatchAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void PendingPlayerCommandSurvivesSaveAndRecovery()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 77,
                westComputerControlled: false);

        EntityId unit =
            Assert.Single(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits.Take(1));

        var command =
            new MoveEntitiesCommand(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(
                    900.0f,
                    0.0f,
                    1_500.0f),
                SimulationTick.Zero,
                FormationTemplate.Compact);

        SimulationCommandEnvelope submitted =
            original.Simulation.SubmitCommand(
                command,
                new SimulationTick(200),
                new SimulationCommandSource(
                    original.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));

        Assert.Equal(
            1UL,
            submitted.Sequence);

        original.Simulation.RunTicks(
            100,
            TestContext.Current.CancellationToken);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);

        Assert.Single(
            save.Commands);
        Assert.Equal(
            1,
            original.Simulation.PendingCommandCount);

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                save);

        Assert.Equal(
            1,
            restored.Simulation.PendingCommandCount);

        original.Simulation.RunTicks(
            150,
            TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(
            150,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            MatchAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            MatchAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void ReplayReconstructsRecordedPlayerCommandAndFinalState()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 91,
                westComputerControlled: false);

        EntityId unit =
            original.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        original.Simulation.SubmitCommand(
            new MoveEntitiesCommand(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(
                    850.0f,
                    0.0f,
                    1_420.0f),
                SimulationTick.Zero,
                FormationTemplate.Wedge),
            new SimulationTick(1),
            new SimulationCommandSource(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));

        original.Simulation.RunTicks(
            600,
            TestContext.Current.CancellationToken);

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                original);
        string document =
            MatchPersistenceSerializer.SerializeReplay(
                replay);
        MatchReplayData restoredReplay =
            MatchPersistenceSerializer.DeserializeReplay(
                document);

        Assert.Single(
            restoredReplay.Commands);

        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                restoredReplay);

        Assert.Equal(
            replay.FinalTick,
            playback.Simulation.CurrentTick.Value);
        Assert.Equal(
            replay.FinalRandomState,
            playback.Simulation.Random.State);
        Assert.Equal(
            replay.FinalMatchState,
            playback.GetMatchState());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void CorruptDocumentFailsBeforeRecovery()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 123);

        scenario.Simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        string document =
            MatchPersistenceSerializer.SerializeSave(
                MatchPersistenceService.CaptureSave(
                    scenario));
        string corrupt =
            document.Replace(
                MatchPersistenceSerializer.Magic,
                "FORGELINE_BROKEN",
                StringComparison.Ordinal);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceSerializer
                        .DeserializeSave(
                            corrupt));

        Assert.Equal(
            MatchPersistenceFailureReason.CorruptDocument,
            exception.Reason);
    }

    [Fact]
    public void FormatVersionMismatchFailsSafely()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 124);

        string document =
            MatchPersistenceSerializer.SerializeSave(
                MatchPersistenceService.CaptureSave(
                    scenario));
        string incompatible =
            document.Replace(
                "\"FormatVersion\": 1",
                "\"FormatVersion\": 999",
                StringComparison.Ordinal);

        Assert.NotEqual(
            document,
            incompatible);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceSerializer
                        .DeserializeSave(
                            incompatible));

        Assert.Equal(
            MatchPersistenceFailureReason.IncompatibleVersion,
            exception.Reason);
    }

    [Fact]
    public void TamperedAuthoritativeCheckpointIsRejected()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 125);

        scenario.Simulation.RunTicks(
            160,
            TestContext.Current.CancellationToken);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                scenario);
        MatchSaveData tampered =
            save with
            {
                StateSha256 =
                    new string(
                        '0',
                        save.StateSha256.Length)
            };

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.Restore(
                        tampered));

        Assert.Equal(
            MatchPersistenceFailureReason.CorruptDocument,
            exception.Reason);
    }

    [Fact]
    public void ReplayDivergenceIsRejected()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 126);

        scenario.Simulation.RunTicks(
            180,
            TestContext.Current.CancellationToken);

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);
        MatchReplayData incompatible =
            replay with
            {
                FinalRandomState =
                    unchecked(
                        replay.FinalRandomState + 1)
            };

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.PlayReplay(
                        incompatible));

        Assert.Equal(
            MatchPersistenceFailureReason.RandomStateMismatch,
            exception.Reason);
    }

    [Fact]
    public void ControlCommandsReplayAtTheirOriginalTickBoundary()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 127,
                westComputerControlled: false);

        scenario.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: true));
        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: false));

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);

        Assert.Equal(
            2,
            replay.Commands.Count(
                static command =>
                    command.IsControl));

        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                replay);

        Assert.Equal(
            scenario.GetMatchState(),
            playback.GetMatchState());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void RestoreAndReplayResolvePauseControlsAgainstReconstructedMatchState()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 129,
                westComputerControlled: false);

        scenario.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: true));
        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: false));

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                scenario);
        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);

        // The saved ID is a runtime-local reference, not a stable replay identity.
        RecordedSimulationCommand[] controlsWithoutRuntimeIds =
            save.Commands
                .Select(
                    static command =>
                        command.Kind == ReplayCommandKind.SetMatchPaused
                            ? command with { TargetEntity = default }
                            : command)
                .ToArray();

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                save with { Commands = controlsWithoutRuntimeIds });
        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                replay with { Commands = controlsWithoutRuntimeIds });

        Assert.Equal(
            save.StateSha256,
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Fact]
    public void UnsupportedQueuedCommandPreventsUnsafeSave()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 128);

        scenario.Simulation.SubmitCommand(
            new UnsupportedRecoveryCommand(),
            new SimulationTick(1),
            SimulationCommandSource.None);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.CaptureSave(
                        scenario));

        Assert.Equal(
            MatchPersistenceFailureReason.IncompleteCommandHistory,
            exception.Reason);
    }

    [Fact]
    public void RepeatedSaveLoadKeepsResourceStateStable()
    {
        MatchRuntime current =
            CreateScenario(
                seed: 2027);

        try
        {
            for (int cycle = 0;
                 cycle < 3;
                 cycle++)
            {
                current.Simulation.RunTicks(
            300,
            TestContext.Current.CancellationToken);

                InventoryStoreSnapshot before =
                    current.Inventories.CaptureSnapshot();
                MatchSaveData save =
                    MatchPersistenceService.CaptureSave(
                        current);
                MatchRuntime restored =
                    MatchPersistenceService.Restore(
                        save);

                AssertInventorySnapshotsEqual(
                    before,
                    restored.Inventories.CaptureSnapshot());

                current.Dispose();
                current = restored;
            }
        }
        finally
        {
            current.Dispose();
        }
    }

    private static void AssertInventorySnapshotsEqual(
        InventoryStoreSnapshot expected,
        InventoryStoreSnapshot actual)
    {
        Assert.Equal(
            expected.NextInventoryId,
            actual.NextInventoryId);
        Assert.Equal(
            expected.Metrics,
            actual.Metrics);
        Assert.Equal(
            expected.Inventories.Count,
            actual.Inventories.Count);

        for (int index = 0;
             index < expected.Inventories.Count;
             index++)
        {
            InventoryStateSnapshot expectedInventory =
                expected.Inventories[index];
            InventoryStateSnapshot actualInventory =
                actual.Inventories[index];

            Assert.Equal(
                expectedInventory.InventoryId,
                actualInventory.InventoryId);
            Assert.Equal(
                expectedInventory.TotalCapacity,
                actualInventory.TotalCapacity,
                precision: 9);
            Assert.Equal(
                expectedInventory.TotalQuantity,
                actualInventory.TotalQuantity,
                precision: 9);
            Assert.Equal(
                expectedInventory.Resources.ToArray(),
                actualInventory.Resources.ToArray());
        }
    }

    private static MatchRuntime CreateScenario(
        ulong seed,
        bool westComputerControlled = true)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Validation,
                seed) with
            {
                Participants =
                    CentralDivideScenario
                        .CreateDefaultParticipants(
                            westComputerControlled,
                            eastComputerControlled: true)
            };

        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
    private sealed class UnsupportedRecoveryCommand : ISimulationCommand
    {
        public void Execute(
            SimulationContext context)
        {
            ArgumentNullException.ThrowIfNull(
                context);
        }
    }

}
