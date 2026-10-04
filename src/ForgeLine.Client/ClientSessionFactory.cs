using ForgeLine.Game;
using ForgeLine.Jobs;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal enum ClientSessionRequestKind : byte
{
    NewGame = 1,
    LoadGame = 2
}

internal readonly record struct ClientSessionRequest(
    ClientSessionRequestKind Kind,
    ulong Seed,
    LoadGameEntry? Save)
{
    internal static ClientSessionRequest NewGame(
        ulong seed) =>
        new(
            ClientSessionRequestKind.NewGame,
            seed,
            null);

    internal static ClientSessionRequest Load(
        LoadGameEntry save) =>
        new(
            ClientSessionRequestKind.LoadGame,
            0,
            save);
}

internal static class ClientSessionFactory
{
    internal static VerticalSliceScenario Create(
        ClientSessionRequest request,
        JobScheduler jobScheduler) =>
        request.Kind switch
        {
            ClientSessionRequestKind.NewGame =>
                VerticalSliceScenario.Create(
                    VerticalSliceRuntimeSettings.CreateClient(
                        jobScheduler,
                        seed: request.Seed)),
            ClientSessionRequestKind.LoadGame
                when request.Save is LoadGameEntry save =>
                    RestoreForGameplay(
                        save),
            _ =>
                throw new InvalidOperationException(
                    $"Unsupported client session request {request.Kind}.")
        };

    private static VerticalSliceScenario RestoreForGameplay(
        LoadGameEntry save)
    {
        VerticalSliceScenario scenario =
            ClientSaveCatalog.Restore(
                save);

        if (scenario.GetMatchState().Lifecycle !=
            MatchLifecyclePhase.Paused)
        {
            return scenario;
        }

        try
        {
            var resume =
                new SetMatchPausedCommand(
                    scenario.BattlefieldRuntime.MatchStateEntity,
                    paused: false);
            scenario.Simulation.ExecuteControlCommand(
                resume);

            if (!resume.Accepted)
            {
                throw new InvalidOperationException(
                    "A restored paused match could not be resumed for gameplay.");
            }

            return scenario;
        }
        catch
        {
            scenario.Dispose();
            throw;
        }
    }
}
