using ForgeLine.Game;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSessionLoadingRecoveryTests
{
    [Fact]
    public void LargeReplayDoesNotBlockTheEventOwner()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.SessionLoading.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using MatchRuntime source = MatchRuntime.Create(
                CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 963), TestContext.Current.CancellationToken);
            source.Simulation.RunTicks(1024, TestContext.Current.CancellationToken);
            MatchSaveData save = MatchPersistenceService.CaptureSave(source);
            string path = Path.Combine(directory, "large.save.json");
            MatchPersistenceSerializer.WriteSave(path, save);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var request = ClientSessionRequest.Load(new LoadGameEntry("large", "LARGE", path, 1024, LoadGameEntryState.Available));
            using var coordinator = new ClientSessionLoadingCoordinator<ClientLoadedSession>((token, report) =>
                ClientSessionFactory.CreateOwned(request, progress =>
                {
                    report(progress);
                    if (progress.Phase == ClientSessionLoadPhase.Replay && progress.CompletedTicks == 0)
                    {
                        entered.Set(); release.Wait(token);
                    }
                }, token), TestContext.Current.CancellationToken);
            int owner = Environment.CurrentManagedThreadId, pumps = 0;
            var views = new List<ForgeLine.Presentation.FrontendSurfaceView>();
            Assert.True(ClientSessionLoadingLoop.Wait(coordinator,
                () =>
                {
                    Assert.Equal(owner, Environment.CurrentManagedThreadId);
                    Assert.True(++pumps < 20000, "Large replay did not complete.");
                    if (pumps >= 20 && entered.IsSet && views.Any(view => view.HasProgress)) release.Set();
                    return true;
                }, () => false, views.Add, () => Thread.Sleep(1)));
            Assert.True(pumps >= 20);
            Assert.Contains(views, view => view.HasProgress && view.Status.Contains("1024", StringComparison.Ordinal));
            Assert.True(coordinator.TryTake(out ClientLoadedSession? loaded));
            using (loaded!) Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(loaded!.Runtime).ComputeSha256());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData((int)ClientSessionLoadError.CorruptSave)]
    [InlineData((int)ClientSessionLoadError.IncompatibleSave)]
    [InlineData((int)ClientSessionLoadError.StateVerification)]
    public void FailedSaveLoadPreservesFileAndAllowsASeparateValidRetry(int expectedCategory)
    {
        var expected = (ClientSessionLoadError)expectedCategory;
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.SessionLoading.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using MatchRuntime source = MatchRuntime.Create(
                CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 960), TestContext.Current.CancellationToken);
            source.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
            MatchSaveData save = MatchPersistenceService.CaptureSave(source);
            string content = expected switch
            {
                ClientSessionLoadError.CorruptSave => "{invalid save",
                ClientSessionLoadError.IncompatibleSave => MatchPersistenceSerializer.SerializeSave(save with { SchemaVersion = 999 }),
                _ => MatchPersistenceSerializer.SerializeSave(save with { Configuration = save.Configuration with { Seed = 961 } })
            };
            string failedPath = Path.Combine(directory, "failed.save.json");
            File.WriteAllText(failedPath, content);
            byte[] unchanged = File.ReadAllBytes(failedPath);
            var failedRequest = ClientSessionRequest.Load(new LoadGameEntry("failed", "FAILED", failedPath, 2, LoadGameEntryState.Available));
            using (var coordinator = new ClientSessionLoadingCoordinator<ClientLoadedSession>(
                (token, report) => ClientSessionFactory.CreateOwned(failedRequest, report, token), TestContext.Current.CancellationToken))
            {
                Assert.Throws<ClientSessionLoadingException>(() => ClientSessionLoadingLoop.Wait(coordinator,
                    () => true, () => false, _ => { }, () => Thread.Sleep(1)));
                Assert.Equal(expected, coordinator.Failure!.Category);
                Assert.False(coordinator.TryTake(out _));
                coordinator.Dispose();
                Assert.False(coordinator.WorkerAlive);
            }
            Assert.Equal(unchanged, File.ReadAllBytes(failedPath));
            string validPath = Path.Combine(directory, "valid.save.json");
            MatchPersistenceSerializer.WriteSave(validPath, save);
            var request = ClientSessionRequest.Load(new LoadGameEntry("valid", "VALID", validPath, 2, LoadGameEntryState.Available));
            using var retry = new ClientSessionLoadingCoordinator<ClientLoadedSession>(
                (token, report) => ClientSessionFactory.CreateOwned(request, report, token), TestContext.Current.CancellationToken);
            Assert.True(ClientSessionLoadingLoop.Wait(retry, () => true, () => false, _ => { }, () => Thread.Sleep(1)));
            Assert.True(retry.TryTake(out ClientLoadedSession? loaded));
            using (loaded!) Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(loaded!.Runtime).ComputeSha256());
            Assert.Equal(unchanged, File.ReadAllBytes(failedPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PausedCheckpointRestoresAndResumesOnLoadingOwner()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.SessionLoading.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using MatchRuntime source = MatchRuntime.Create(
                CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 962), TestContext.Current.CancellationToken);
            source.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
            var pause = new SetMatchPausedCommand(source.BattlefieldRuntime.MatchStateEntity, true);
            source.Simulation.ExecuteControlCommand(pause); Assert.True(pause.Accepted);
            string path = Path.Combine(directory, "paused.save.json");
            MatchPersistenceSerializer.WriteSave(path, MatchPersistenceService.CaptureSave(source));
            byte[] original = File.ReadAllBytes(path);
            var request = ClientSessionRequest.Load(new LoadGameEntry("paused", "PAUSED", path, 2, LoadGameEntryState.Available));
            using var coordinator = new ClientSessionLoadingCoordinator<ClientLoadedSession>(
                (token, report) => ClientSessionFactory.CreateOwned(request, report, token), TestContext.Current.CancellationToken);
            Assert.True(ClientSessionLoadingLoop.Wait(coordinator, () => true, () => false, _ => { }, () => Thread.Sleep(1)));
            Assert.True(coordinator.TryTake(out ClientLoadedSession? loaded));
            using (loaded!) Assert.Equal(MatchLifecyclePhase.Running, loaded!.Runtime.GetMatchState().Lifecycle);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
