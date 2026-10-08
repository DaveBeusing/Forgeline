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
    internal MatchRuntimeSettings? RuntimeSettings { get; init; }

    internal static ClientSessionRequest NewGame(MatchRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return NewGame(settings.Seed) with { RuntimeSettings = settings };
    }

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
    internal static MatchRuntime Create(
        ClientSessionRequest request,
        JobScheduler jobScheduler,
        Func<string, MatchComposition>? resolveComposition = null) =>
        request.Kind switch
        {
            ClientSessionRequestKind.NewGame =>
                MatchRuntime.Create(
                    (request.RuntimeSettings ?? CentralDivideScenario.CreateClient(
                        jobScheduler,
                        seed: request.Seed)) with
                    {
                        Scheduler = jobScheduler,
                        SchedulerOwnership = MatchSchedulerOwnership.Host
                    }),
            ClientSessionRequestKind.LoadGame
                when request.Save is LoadGameEntry save =>
                    RestoreForGameplay(
                        save, resolveComposition),
            _ =>
                throw new InvalidOperationException(
                    $"Unsupported client session request {request.Kind}.")
        };

    private static MatchRuntime RestoreForGameplay(
        LoadGameEntry save,
        Func<string, MatchComposition>? resolveComposition)
    {
        MatchRuntime scenario =
            ClientSaveCatalog.Restore(
                save, resolveComposition);

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
