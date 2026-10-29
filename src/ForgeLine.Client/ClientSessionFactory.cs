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
    internal string ScenarioKey { get; init; } = CentralDivideScenario.CompositionKey;
    internal ScenarioIdentity? Identity { get; init; }
    internal string? GeneratedManifestSha256 { get; init; }
    internal bool ReloadScenario { get; init; }

    internal static ClientSessionRequest Restart(MatchRuntimeSettings settings) =>
        NewGame(settings with { Scheduler = null, SchedulerOwnership = MatchSchedulerOwnership.None }) with { ReloadScenario = true };

    internal static ClientSessionRequest NewGame(MatchRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return NewGame(settings.Seed, settings.Composition.Key) with { RuntimeSettings = settings, Identity = settings.Composition.Identity, GeneratedManifestSha256 = settings.Composition.Identity?.Generated?.ManifestSha256 };
    }

    internal static ClientSessionRequest NewGame(
        ulong seed, string scenarioKey = CentralDivideScenario.CompositionKey) =>
        new(
            ClientSessionRequestKind.NewGame,
            seed,
            null) { ScenarioKey = scenarioKey };

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
        if (request.RuntimeSettings?.InitialDeveloperProfile is not null && !DeveloperToolsCapability.Current.IsCompiled)
            throw new InvalidOperationException("Developer preset sessions require a Developer build.");
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
                    ResolveNewGame(request,
                        jobScheduler ?? throw new InvalidOperationException("New sessions require an owned scheduler."),
                        resolveComposition) with
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
        CancellationToken cancellationToken,
        Func<string, MatchComposition>? resolveComposition = null,
        ClientGeneratedMapStore? generatedMaps = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GeneratedMapPackage? generated = generatedMaps?.Resolve(request, cancellationToken);
        if (generated is not null)
            resolveComposition = key => key == generated.CompositionKey ? generated.CreateComposition() :
                throw new InvalidOperationException("Generated map composition differs from the requested identity.");
        var decorations = generated is null ? null : ClientGeneratedMapStore.CreateDecorations(generated, cancellationToken);
        var terrainProfile = generated is null ? null : ClientGeneratedMapStore.CreateTerrainProfile(generated, generatedMaps?.Assets);
        cancellationToken.ThrowIfCancellationRequested();
        JobScheduler? scheduler = request.Kind == ClientSessionRequestKind.NewGame ? new JobScheduler() : null;
        MatchRuntime? runtime = null;
        try
        {
            runtime = CreateCancellable(request, scheduler, cancellationToken, reportProgress, resolveComposition);
            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            return new ClientLoadedSession(runtime, scheduler) { GeneratedMap = generated, Decorations = decorations, TerrainProfile = terrainProfile };
        }
        catch
        {
            try { runtime?.Dispose(); }
            finally { scheduler?.Dispose(); }
            throw;
        }
    }

    private static MatchRuntimeSettings ResolveNewGame(ClientSessionRequest request, JobScheduler scheduler, Func<string, MatchComposition>? resolver)
    {
        MatchComposition composition = resolver is not null ? resolver(request.ScenarioKey) :
            request.ReloadScenario ? ScenarioRegistry.Default.Resolve(request.ScenarioKey) :
            request.RuntimeSettings?.Composition ?? ScenarioRegistry.Default.Resolve(request.ScenarioKey);
        if (composition.Key != request.ScenarioKey || (request.Identity is not null && composition.Identity != request.Identity))
            throw new InvalidOperationException("Resolved scenario differs from the requested session identity.");
        MatchRuntimeSettings settings = request.RuntimeSettings ?? CentralDivideScenario.CreateClient(scheduler, request.Seed, composition: composition);
#if FORGELINE_DEVELOPER_TOOLS
        if (settings.InitialDeveloperProfile is { } initial)
        {
            if (!DeveloperToolsCapability.Current.IsCompiled)
                throw new InvalidOperationException("Developer preset capability is unavailable immediately before session composition.");
            var metadata = initial.Metadata;
            if (metadata.CompositionKey != composition.Key || metadata.BaseIdentity != composition.Identity ||
                settings.Seed != metadata.Seed || settings.Scenario.Profile != metadata.Profile ||
                !settings.Participants.SequenceEqual(metadata.Participants))
                throw new InvalidOperationException("Preset base identity, profile, seed or participants changed before composition.");
            MatchRuntimeSettings basis = CentralDivideScenario.CreateClient(scheduler, metadata.Seed, settings.EnableDiagnostics, composition) with
            {
                Scenario = CentralDivideScenario.CreateSettings(metadata.Profile),
                Participants = Array.AsReadOnly(metadata.Participants.ToArray()),
                EnablePhaseTiming = settings.EnablePhaseTiming,
                EnableDebugCapture = settings.EnableDebugCapture,
                EnableSpatialQueryTiming = settings.EnableSpatialQueryTiming,
                InitialEntityCapacity = settings.InitialEntityCapacity,
                InitialDeveloperProfile = null
            };
            DeveloperTuningOverride[] overrides = initial.Parameters.Select(value =>
                new DeveloperTuningOverride(value.Id, value.Target, value.Value)).ToArray();
            if (!DeveloperRestartBuilder.TryBuild(basis, metadata, overrides, out var plan, out var failure))
                throw new InvalidOperationException($"Developer preset failed final session validation ({failure}).");
            settings = plan!.Settings;
        }
#else
        if (settings.InitialDeveloperProfile is not null)
            throw new InvalidOperationException("Developer preset sessions are unavailable in Public builds.");
#endif
        return settings with { Composition = composition };
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
    internal GeneratedMapPackage? GeneratedMap { get; init; }
    internal ForgeLine.Presentation.IChunkDecorationProvider? Decorations { get; init; }
    internal ForgeLine.Presentation.TerrainPresentationProfile? TerrainProfile { get; init; }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Runtime.Dispose(); }
        finally { scheduler?.Dispose(); }
    }
}
