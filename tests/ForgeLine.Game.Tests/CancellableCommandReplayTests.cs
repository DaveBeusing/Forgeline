using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CancellableCommandReplayTests
{
    private static MatchRuntime Create() => MatchRuntime.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 940) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) },
        TestContext.Current.CancellationToken);

    [Fact]
    public void MixedLateInputsAndCompletedTickControlsMatchPeriodicAndFinalState()
    {
        using var original = Create();
        var issuer = new PlayerId(1);
        var unit = original.GetBase(issuer).StartingUnits[0];
        var position = original.Simulation.Entities.GetComponent<WorldTransform>(unit).Position;
        original.Simulation.SubmitCommand(new MoveEntitiesCommand(issuer, [unit],
            position + new Vector3(60, 0, 0), SimulationTick.Zero), new(20));
        var checkpoints = new List<MatchReplayCheckpoint> { MatchReplayCheckpoint.Capture(original) };
        for (int tick = 1; tick <= 128; tick++)
        {
            if (tick == 9)
                original.Simulation.SubmitCommand(PlayerLogisticsActionCommand.SetStockPolicy(issuer,
                    original.GetBase(issuer).CommandCore, ResourceIds.Fuel, 100, 200, 400,
                    LogisticsStockPriority.High, true, original.Simulation.CurrentTick), new(10));
            if (tick == 33)
                original.Simulation.SubmitCommand(PlayerTacticalActionCommand.HoldPosition(issuer, [unit],
                    original.Simulation.CurrentTick, original.Intelligence,
                    original.Services.Weapons, original.Services.ArtilleryWeapons), new(34));
            original.Simulation.AdvanceOneTick();
            if (tick == 64)
            {
                original.Simulation.ExecuteControlCommand(new SetMatchPausedCommand(original.BattlefieldRuntime.MatchStateEntity, true));
                original.Simulation.ExecuteControlCommand(new SetMatchPausedCommand(original.BattlefieldRuntime.MatchStateEntity, false));
            }
            if (tick % 16 == 0) checkpoints.Add(MatchReplayCheckpoint.Capture(original));
        }
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(
            MatchPersistenceService.CaptureReplay(original)));
        Assert.Equal(5, replay.Commands.Count);
        var progress = new List<MatchRestorationProgress>();
        using var playback = MatchPersistenceService.PlayReplayCancellable(replay,
            TestContext.Current.CancellationToken, progress.Add, checkpoints: checkpoints);
        Assert.Equal(replay.FinalStateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        Assert.Equal(MatchRestorationPhase.HashVerification, progress[^1].Phase);
        Assert.Equal(new ulong[] { 0, 64, 128 }, progress.Where(p => p.Phase == MatchRestorationPhase.Replay)
            .Select(p => p.CompletedTicks).ToArray());
        original.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        playback.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(original).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Theory]
    [InlineData(MatchRestorationPhase.Configuration)]
    [InlineData(MatchRestorationPhase.ScenarioAssembly)]
    [InlineData(MatchRestorationPhase.Replay)]
    [InlineData(MatchRestorationPhase.HashVerification)]
    public void CancellationAtEveryPhasePreservesSourceAndPermitsIndependentRetry(MatchRestorationPhase phase)
    {
        using var live = Create();
        live.Simulation.RunTicks(128, TestContext.Current.CancellationToken);
        var replay = MatchPersistenceService.CaptureReplay(live);
        string unchanged = MatchPersistenceSerializer.SerializeReplay(replay);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.Throws<OperationCanceledException>(() => MatchPersistenceService.PlayReplayCancellable(replay, stop.Token,
            progress => { if (progress.Phase == phase) stop.Cancel(); }));
        Assert.Equal(unchanged, MatchPersistenceSerializer.SerializeReplay(replay));
        Assert.Equal(replay.FinalStateSha256, MatchAuthoritativeSnapshot.Capture(live).ComputeSha256());
        using var retry = MatchPersistenceService.PlayReplayCancellable(replay, TestContext.Current.CancellationToken);
        Assert.Equal(replay.FinalStateSha256, MatchAuthoritativeSnapshot.Capture(retry).ComputeSha256());
    }

    [Fact]
    public void MidPlaybackStopAndCheckpointDivergenceAreDistinctFromCorruption()
    {
        using var live = Create();
        live.Simulation.RunTicks(64, TestContext.Current.CancellationToken);
        var checkpoint = MatchReplayCheckpoint.Capture(live);
        live.Simulation.RunTicks(64, TestContext.Current.CancellationToken);
        var replay = MatchPersistenceService.CaptureReplay(live);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.Throws<OperationCanceledException>(() => MatchPersistenceService.PlayReplayCancellable(replay, stop.Token,
            p => { if (p.Phase == MatchRestorationPhase.Replay && p.CompletedTicks == 64) stop.Cancel(); }));
        var stateError = Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
            replay, TestContext.Current.CancellationToken, checkpoints: [checkpoint with { StateSha256 = new string('0', 64) }]));
        Assert.Equal(MatchPersistenceFailureReason.StateMismatch, stateError.Reason);
        Assert.Contains("tick 64", stateError.Message);
        var rngError = Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
            replay, TestContext.Current.CancellationToken, checkpoints: [checkpoint with { RandomState = checkpoint.RandomState + 1 }]));
        Assert.Equal(MatchPersistenceFailureReason.RandomStateMismatch, rngError.Reason);
        Assert.Equal(replay.FinalStateSha256, MatchAuthoritativeSnapshot.Capture(live).ComputeSha256());
    }

    [Fact]
    public void InvalidCheckpointInputAndPrecancelledPlaybackNeverResolveContent()
    {
        using var live = Create();
        var replay = MatchPersistenceService.CaptureReplay(live);
        var checkpoint = MatchReplayCheckpoint.Capture(live);
        int resolves = 0;
        MatchComposition Resolve(string key) { resolves++; return ScenarioRegistry.Default.Resolve(key); }
        foreach (var entries in new IReadOnlyList<MatchReplayCheckpoint>[]
        {
            [checkpoint, checkpoint],
            [checkpoint with { Tick = 1 }],
            [checkpoint with { StateSha256 = "partial" }],
            [null!],
            Enumerable.Repeat(checkpoint, MatchReplayCheckpoint.MaximumCount + 1).ToArray()
        })
        {
            var error = Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
                replay, TestContext.Current.CancellationToken, resolveComposition: Resolve, checkpoints: entries));
            Assert.Equal(MatchPersistenceFailureReason.CorruptDocument, error.Reason);
        }
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
                replay, TestContext.Current.CancellationToken, resolveComposition: Resolve,
                checkpoints: [checkpoint with { SchemaVersion = 2 }])).Reason);
        Assert.Throws<OperationCanceledException>(() => MatchPersistenceService.PlayReplayCancellable(replay,
            new CancellationToken(true), resolveComposition: Resolve));
        Assert.Equal(0, resolves);
    }

    [Fact]
    public void OptionalSamplesDoNotReplaceFullFinalIntegrityOrDeveloperPermission()
    {
        using var live = Create();
        live.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        var replay = MatchPersistenceService.CaptureReplay(live);
        var sample = MatchReplayCheckpoint.Capture(live);
        var altered = replay with { FinalStateSha256 = new string('0', 64) };
        Assert.Equal(MatchPersistenceFailureReason.StateMismatch,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
                altered, TestContext.Current.CancellationToken, checkpoints: [sample])).Reason);
        var developer = replay with { DeveloperModified = true };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplayCancellable(
            developer, TestContext.Current.CancellationToken));
    }
}
