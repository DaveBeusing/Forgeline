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
        using MatchRuntime scenario =
            ClientSessionFactory.Create(
                ClientSessionRequest.NewGame(731),
                scheduler);

        Assert.Equal(
            731UL,
            scenario.RuntimeSettings.Seed);
    }

    [Fact]
    public void ExplicitCompositionUsesHostOwnedSchedulerAndPreservesHeadlessCheckpoint()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed: 745);
        settings = settings with
        {
            Composition = settings.Composition with { Key = "test.client-composition.v1" },
            Participants = settings.Participants.Select(participant => new MatchParticipantConfiguration(
                participant.Player, participant.Faction, participant.StartIndex, false)).ToArray()
        };
        using var scheduler = new JobScheduler();
        using MatchRuntime client = ClientSessionFactory.Create(ClientSessionRequest.NewGame(settings), scheduler);
        using MatchRuntime headless = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        client.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
        headless.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
        Assert.Same(settings.Composition, client.RuntimeSettings.Composition);
        Assert.Same(scheduler, client.Scheduler);
        Assert.False(client.OwnsScheduler);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(headless).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(client).ComputeSha256());
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

            using (MatchRuntime source =
                   CentralDivideScenario.Create(
                       seed: 731))
            {
                source.Simulation.AdvanceOneTick();

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
            using MatchRuntime restored =
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
