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
        Assert.Equal(731, request.Seed);
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
            731,
            scenario.RuntimeSettings.RandomSeed);
    }
}
