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
        CreateCancellable(request, jobScheduler, CancellationToken.None, null, resolveComposition);

    internal static MatchRuntime CreateCancellable(
        ClientSessionRequest request,
        JobScheduler? jobScheduler,
        CancellationToken cancellationToken,
        Action<ClientSessionLoadProgress>? reportProgress = null,
        Func<string, MatchComposition>? resolveComposition = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(new(ClientSessionLoadPhase.Configuration));
        if (request.Kind == ClientSessionRequestKind.NewGame)
        {
            ArgumentNullException.ThrowIfNull(jobScheduler);
            reportProgress?.Invoke(new(ClientSessionLoadPhase.ScenarioAssembly));
        }
        return
        request.Kind switch
        {
            ClientSessionRequestKind.NewGame =>
                MatchRuntime.Create(
                    (request.RuntimeSettings ?? CentralDivideScenario.CreateClient(
                        jobScheduler ?? throw new InvalidOperationException("New sessions require an owned scheduler."),
                        seed: request.Seed)) with
                    {
                        Scheduler = jobScheduler,
                        SchedulerOwnership = MatchSchedulerOwnership.Host
                    }, cancellationToken),
            ClientSessionRequestKind.LoadGame
                when request.Save is LoadGameEntry save =>
                    RestoreForGameplay(
                        save, resolveComposition, reportProgress, cancellationToken),
            _ =>
                throw new InvalidOperationException(
                    $"Unsupported client session request {request.Kind}.")
        };
    }

    internal static ClientLoadedSession CreateOwned(
        ClientSessionRequest request, Action<ClientSessionLoadProgress> reportProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JobScheduler? scheduler = request.Kind == ClientSessionRequestKind.NewGame ? new JobScheduler() : null;
        MatchRuntime? runtime = null;
        try
        {
            runtime = CreateCancellable(request, scheduler, cancellationToken, reportProgress);
            cancellationToken.ThrowIfCancellationRequested();
            return new ClientLoadedSession(runtime, scheduler);
        }
        catch
        {
            try { runtime?.Dispose(); }
            finally { scheduler?.Dispose(); }
            throw;
        }
    }

    private static MatchRuntime RestoreForGameplay(
        LoadGameEntry save,
        Func<string, MatchComposition>? resolveComposition,
        Action<ClientSessionLoadProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        MatchRuntime scenario =
            ClientSaveCatalog.RestoreCancellable(
                save, cancellationToken, progress => reportProgress?.Invoke(new(
                    (ClientSessionLoadPhase)progress.Phase, progress.CompletedTicks, progress.TotalTicks)),
                resolveComposition);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scenario.GetMatchState().Lifecycle != MatchLifecyclePhase.Paused) return scenario;
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

internal sealed class ClientLoadedSession(MatchRuntime runtime, JobScheduler? scheduler) : IDisposable
{
    private int _disposed;
    internal MatchRuntime Runtime { get; } = runtime;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Runtime.Dispose(); }
        finally { scheduler?.Dispose(); }
    }
}
