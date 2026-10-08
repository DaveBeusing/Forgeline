using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CancellableMatchRestorationTests
{
    private static MatchRuntime Create() => MatchRuntime.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 940),
        TestContext.Current.CancellationToken);

    [Fact]
    public void CompletedTickProgressPreservesCheckpointAndDeterministicContinuation()
    {
        using MatchRuntime original = Create();
        original.Simulation.RunTicks(1024, TestContext.Current.CancellationToken);
        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        var progress = new List<MatchRestorationProgress>();
        using MatchRuntime restored = MatchPersistenceService.RestoreCancellable(save,
            TestContext.Current.CancellationToken, progress.Add);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        MatchRestorationProgress[] replay = progress.Where(p => p.Phase == MatchRestorationPhase.Replay).ToArray();
        Assert.Equal(0UL, replay[0].CompletedTicks);
        Assert.Equal(save.SavedTick, replay[^1].CompletedTicks);
        Assert.All(replay, p => Assert.Equal(save.SavedTick, p.TotalTicks));
        Assert.InRange(replay.Length, 2, 18);
        Assert.True(replay.Zip(replay.Skip(1)).All(pair => pair.First.CompletedTicks < pair.Second.CompletedTicks));
        Assert.Equal(MatchRestorationPhase.HashVerification, progress[^1].Phase);
        original.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(original).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
    }

    [Theory]
    [InlineData(MatchRestorationPhase.Configuration)]
    [InlineData(MatchRestorationPhase.ScenarioAssembly)]
    [InlineData(MatchRestorationPhase.Replay)]
    [InlineData(MatchRestorationPhase.HashVerification)]
    public void CancellationAtEachPhaseReturnsCancellationWithoutChangingSave(MatchRestorationPhase phase)
    {
        using MatchRuntime original = Create();
        original.Simulation.RunTicks(128, TestContext.Current.CancellationToken);
        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        string unchanged = MatchPersistenceSerializer.SerializeSave(save);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.Throws<OperationCanceledException>(() => MatchPersistenceService.RestoreCancellable(save,
            cancellation.Token, progress => { if (progress.Phase == phase) cancellation.Cancel(); }));
        Assert.Equal(unchanged, MatchPersistenceSerializer.SerializeSave(save));
        using MatchRuntime retry = MatchPersistenceService.RestoreCancellable(save, TestContext.Current.CancellationToken);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(retry).ComputeSha256());
    }

    [Fact]
    public void VerificationStillRejectsDifferentReconstruction()
    {
        using MatchRuntime original = Create();
        original.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        var changed = save with { Configuration = save.Configuration with { Seed = save.Configuration.Seed + 1 } };
        MatchPersistenceException error = Assert.Throws<MatchPersistenceException>(() =>
            MatchPersistenceService.RestoreCancellable(changed, TestContext.Current.CancellationToken));
        Assert.Contains(error.Reason, new[] { MatchPersistenceFailureReason.StateMismatch,
            MatchPersistenceFailureReason.RandomStateMismatch });
    }
}
