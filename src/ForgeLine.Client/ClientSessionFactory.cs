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
    int Seed,
    LoadGameEntry? Save)
{
    internal static ClientSessionRequest NewGame(
        int seed) =>
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
        IJobScheduler jobScheduler) =>
        request.Kind switch
        {
            ClientSessionRequestKind.NewGame =>
                VerticalSliceScenario.Create(
                    VerticalSliceRuntimeSettings.CreateClient(
                        jobScheduler,
                        seed: request.Seed)),
            ClientSessionRequestKind.LoadGame
                when request.Save is LoadGameEntry save =>
                    ClientSaveCatalog.Restore(save),
            _ =>
                throw new InvalidOperationException(
                    $"Unsupported client session request {request.Kind}.")
        };
}
