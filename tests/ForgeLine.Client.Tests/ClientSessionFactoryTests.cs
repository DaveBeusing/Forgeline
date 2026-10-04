using ForgeLine.Game;
using ForgeLine.Jobs;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSessionFactoryTests
{
    [Fact]
    public void NewGameRequestCarriesSelectedSeed()
    {
        ClientSessionRequest request =
            ClientSessionRequest.NewGame(731);

        Assert.Equal(
            ClientSessionRequestKind.NewGame,
            request.Kind);
        Assert.Equal(731UL, request.Seed);
        Assert.Null(request.Save);
    }

    [Fact]
    public void LoadRequestCarriesSelectedSave()
    {
        var save =
            new LoadGameEntry(
                "slot-a",
                "SLOT A",
                "slot-a.save.json",
                42,
                LoadGameEntryState.Available);

        ClientSessionRequest request =
            ClientSessionRequest.Load(save);

        Assert.Equal(
            ClientSessionRequestKind.LoadGame,
            request.Kind);
        Assert.Equal(save, request.Save);
    }

    [Fact]
    public void NewGameFactoryCreatesScenarioWithRequestedSeed()
    {
        using var scheduler =
            new JobScheduler();
        using VerticalSliceScenario scenario =
            ClientSessionFactory.Create(
                ClientSessionRequest.NewGame(731),
                scheduler);

        Assert.Equal(
            731UL,
            scenario.RuntimeSettings.Seed);
    }

    [Fact]
    public void RestoredPausedSaveResumesBeforeGameplayStarts()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "ForgeLine.Client.Tests",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(
            directory);

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "paused.save.json");

            using (VerticalSliceScenario source =
                   VerticalSliceScenario.Create(
                       seed: 731))
            {
                var pause =
                    new SetMatchPausedCommand(
                        source.BattlefieldRuntime.MatchStateEntity,
                        paused: true);
                source.Simulation.ExecuteControlCommand(
                    pause);

                Assert.True(
                    pause.Accepted);
                Assert.Equal(
                    MatchLifecyclePhase.Paused,
                    source.GetMatchState().Lifecycle);

                MatchPersistenceSerializer.WriteSave(
                    path,
                    MatchPersistenceService.CaptureSave(
                        source));
            }

            var entry =
                new LoadGameEntry(
                    "paused",
                    "PAUSED",
                    path,
                    0,
                    LoadGameEntryState.Available);
            using var scheduler =
                new JobScheduler();
            using VerticalSliceScenario restored =
                ClientSessionFactory.Create(
                    ClientSessionRequest.Load(
                        entry),
                    scheduler);

            Assert.Equal(
                MatchLifecyclePhase.Running,
                restored.GetMatchState().Lifecycle);
        }
        finally
        {
            if (Directory.Exists(
                    directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
    }
}
