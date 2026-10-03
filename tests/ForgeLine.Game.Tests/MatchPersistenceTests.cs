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
        using VerticalSliceScenario original =
            CreateScenario(
                seed: 2026);

        bool reachedActiveState =
            original.RunUntil(
                scenario =>
                    scenario.Simulation.Entities
                        .GetComponentCount<ProductionRequest>() > 0 ||
                    scenario.Simulation.Entities
                        .GetComponentCount<CargoTransportOrder>() > 0,
                maximumTicks: 5_000);

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

        using VerticalSliceScenario restored =
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

        VerticalSliceAuthoritativeSnapshot expected =
            VerticalSliceAuthoritativeSnapshot.Capture(
                original);
        VerticalSliceAuthoritativeSnapshot actual =
            VerticalSliceAuthoritativeSnapshot.Capture(
                restored);

        Assert.True(
            MatchPersistenceService.Compare(
                expected,
                actual).Compatible);

        original.Simulation.RunTicks(
            250);
        restored.Simulation.RunTicks(
            250);

        Assert.Equal(
            VerticalSliceAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            VerticalSliceAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void PendingPlayerCommandSurvivesSaveAndRecovery()
    {
        using VerticalSliceScenario original =
            CreateScenario(
                seed: 77,
                westComputerControlled: false);

        EntityId unit =
            Assert.Single(
                original.West.StartingUnits.Take(1));

        var command =
            new MoveEntitiesCommand(
                original.West.Player,
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
                    original.West.Player.Value));

        Assert.Equal(
            1UL,
            submitted.Sequence);

        original.Simulation.RunTicks(
            100);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);

        Assert.Single(
            save.Commands);
        Assert.Equal(
            1,
            original.Simulation.PendingCommandCount);

        using VerticalSliceScenario restored =
            MatchPersistenceService.Restore(
                save);

        Assert.Equal(
            1,
            restored.Simulation.PendingCommandCount);

        original.Simulation.RunTicks(
            150);
        restored.Simulation.RunTicks(
            150);

        Assert.Equal(
            VerticalSliceAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            VerticalSliceAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void ReplayReconstructsRecordedPlayerCommandAndFinalState()
    {
        using VerticalSliceScenario original =
            CreateScenario(
                seed: 91,
                westComputerControlled: false);

        EntityId unit =
            original.West.StartingUnits[0];

        original.Simulation.SubmitCommand(
            new MoveEntitiesCommand(
                original.West.Player,
                [unit],
                new Vector3(
                    850.0f,
                    0.0f,
                    1_420.0f),
                SimulationTick.Zero,
                FormationTemplate.Wedge),
            new SimulationTick(1),
            new SimulationCommandSource(
                original.West.Player.Value));

        original.Simulation.RunTicks(
            600);

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

        using VerticalSliceScenario playback =
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
            VerticalSliceAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void CorruptDocumentFailsBeforeRecovery()
    {
        using VerticalSliceScenario scenario =
            CreateScenario(
                seed: 123);

        scenario.Simulation.RunTicks(
            40);

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
        using VerticalSliceScenario scenario =
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
        using VerticalSliceScenario scenario =
            CreateScenario(
                seed: 125);

        scenario.Simulation.RunTicks(
            160);

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
        using VerticalSliceScenario scenario =
            CreateScenario(
                seed: 126);

        scenario.Simulation.RunTicks(
            180);

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
        using VerticalSliceScenario scenario =
            CreateScenario(
                seed: 127,
                westComputerControlled: false);

        scenario.Simulation.RunTicks(
            20);

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

        using VerticalSliceScenario playback =
            MatchPersistenceService.PlayReplay(
                replay);

        Assert.Equal(
            scenario.GetMatchState(),
            playback.GetMatchState());
        Assert.Equal(
            replay.FinalStateSha256,
            VerticalSliceAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void UnsupportedQueuedCommandPreventsUnsafeSave()
    {
        using VerticalSliceScenario scenario =
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
        VerticalSliceScenario current =
            CreateScenario(
                seed: 2027);

        try
        {
            for (int cycle = 0;
                 cycle < 3;
                 cycle++)
            {
                current.Simulation.RunTicks(
                    300);

                InventoryStoreSnapshot before =
                    current.Inventories.CaptureSnapshot();
                MatchSaveData save =
                    MatchPersistenceService.CaptureSave(
                        current);
                VerticalSliceScenario restored =
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

    private static VerticalSliceScenario CreateScenario(
        ulong seed,
        bool westComputerControlled = true)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Validation,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings
                        .CreateDefaultParticipants(
                            westComputerControlled,
                            eastComputerControlled: true)
            };

        return VerticalSliceScenario.Create(
            runtime);
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
