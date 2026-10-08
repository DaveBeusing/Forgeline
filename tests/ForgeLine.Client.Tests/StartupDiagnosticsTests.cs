using System.Diagnostics;
using ForgeLine.Game;
using ForgeLine.Jobs;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class StartupDiagnosticsTests
{
    private static StartupDiagnostics Create() =>
        new(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);

    [Fact]
    public void InstrumentationPreservesAuthoritativeSnapshot()
    {
        static string Capture(StartupDiagnostics diagnostics)
        {
            MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Gameplay, seed: 745);
            settings = settings with
            {
                Participants = settings.Participants.Select(participant =>
                    new MatchParticipantConfiguration(participant.Player,
                        participant.Faction, participant.StartIndex, false)).ToArray()
            };
            using var scheduler = new JobScheduler();
            diagnostics.BeginSession();
            using MatchRuntime runtime = diagnostics.Measure(StartupPhase.SessionReconstruction,
                () => ClientSessionFactory.Create(ClientSessionRequest.NewGame(settings), scheduler), 1);
            diagnostics.Mark(StartupPhase.SessionRuntimeReady, 1);
            runtime.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
            diagnostics.GameplayPresented();
            diagnostics.Finish();
            return MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        }

        Assert.Equal(Capture(StartupDiagnostics.Disabled), Capture(Create()));
    }

    [Fact]
    public void ErrorAfterReadinessRemainsAVisibleRunFailure()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        diagnostics.Begin(StartupPhase.Run);
        diagnostics.Mark(StartupPhase.MainMenuFirstFrame);
        diagnostics.MenuInteractive();
        diagnostics.Finish(new InvalidOperationException("late failure"));
        Assert.Equal(StartupReadiness.Failed, diagnostics.ApplicationReadiness);
        Assert.True(diagnostics.Has(StartupPhase.ApplicationReady));
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.Run &&
            e.Kind == StartupEventKind.Failed && e.Detail == nameof(InvalidOperationException));
    }

    [Fact]
    public void OccludedOrUnpresentedViewsCannotClaimFirstFrames()
    {
        var diagnostics = Create();
        Assert.False(ClientFrontendRenderHost.ObservePresentation(diagnostics, 0, 0,
            FrontendSurfaceKind.MainMenu, false));
        diagnostics.MenuInteractive();
        Assert.Empty(diagnostics.Snapshot());
        Assert.Equal(StartupReadiness.Pending, diagnostics.ApplicationReadiness);
        Assert.True(ClientFrontendRenderHost.ObservePresentation(diagnostics, 0, 1,
            FrontendSurfaceKind.StudioSplash, false));
        Assert.False(diagnostics.Has(StartupPhase.StudioSplashFirstFrame));
        Assert.True(ClientFrontendRenderHost.ObservePresentation(diagnostics, 1, 2,
            FrontendSurfaceKind.StudioSplash, true));
        Assert.True(diagnostics.Has(StartupPhase.StudioSplashFirstFrame));
        Assert.True(ClientFrontendRenderHost.ObservePresentation(diagnostics, 2, 3,
            FrontendSurfaceKind.MainMenu, false));
        diagnostics.MenuInteractive();
        Assert.Equal(StartupReadiness.Ready, diagnostics.ApplicationReadiness);
    }

    [Fact]
    public void MenuReadinessRequiresPresentationAndIsSeparateFromSessionReadiness()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        diagnostics.Mark(StartupPhase.FrontendReady);
        diagnostics.MenuInteractive();
        Assert.Equal(StartupReadiness.Pending, diagnostics.ApplicationReadiness);
        diagnostics.Mark(StartupPhase.MainMenuFirstFrame);
        diagnostics.MenuInteractive();
        diagnostics.MenuInteractive();
        Assert.Equal(StartupReadiness.Ready, diagnostics.ApplicationReadiness);
        Assert.Equal(StartupReadiness.Pending, diagnostics.SessionReadiness);
        StartupEvent[] events = diagnostics.Snapshot();
        Assert.Single(events, e => e.Phase == StartupPhase.ApplicationReady);
        Assert.True(Array.FindIndex(events, e => e.Phase == StartupPhase.MainMenuFirstFrame) <
            Array.FindIndex(events, e => e.Phase == StartupPhase.MainMenuInteractive));
    }

    [Fact]
    public void SessionReconstructionDoesNotClaimPresentedGameplay()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        diagnostics.BeginSession();
        diagnostics.Measure(StartupPhase.SessionReconstruction, () => 42, 1);
        diagnostics.Mark(StartupPhase.SessionRuntimeReady, 1);
        Assert.Equal(StartupReadiness.Pending, diagnostics.SessionReadiness);
        diagnostics.GameplayPresented();
        diagnostics.GameplayPresented();
        diagnostics.Finish();
        Assert.Equal(StartupReadiness.Ready, diagnostics.SessionReadiness);
        Assert.Equal(StartupReadiness.Ready, diagnostics.ApplicationReadiness);
        StartupEvent frame = Assert.Single(diagnostics.Snapshot(), e => e.Phase == StartupPhase.FirstGameplayFrame);
        Assert.Equal(1, frame.SessionId);
        Assert.Single(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SessionReady);
    }

    [Fact]
    public void FailedPhaseHasDurationAndCannotProduceReady()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        var error = new InvalidOperationException("test failure");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            diagnostics.Measure<int>(StartupPhase.RuntimeAssets, () => throw error)));
        diagnostics.Finish(error);
        Assert.Equal(StartupReadiness.Failed, diagnostics.ApplicationReadiness);
        StartupEvent failed = Assert.Single(diagnostics.Snapshot(),
            e => e.Phase == StartupPhase.RuntimeAssets && e.Kind == StartupEventKind.Failed);
        Assert.True(failed.DurationMilliseconds >= 0);
        Assert.DoesNotContain(diagnostics.Snapshot(), e => e.Phase == StartupPhase.ApplicationReady);
    }

    [Fact]
    public void ClosingBeforeReadinessCancelsOpenPhases()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        diagnostics.BeginSession();
        diagnostics.Begin(StartupPhase.SessionReconstruction, 1);
        diagnostics.Finish();
        diagnostics.GameplayPresented();
        Assert.Equal(StartupReadiness.Cancelled, diagnostics.ApplicationReadiness);
        Assert.Equal(StartupReadiness.Cancelled, diagnostics.SessionReadiness);
        Assert.DoesNotContain(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SessionReady);
        Assert.Contains(diagnostics.Snapshot(), e => e.Phase == StartupPhase.SessionReconstruction &&
            e.Kind == StartupEventKind.Cancelled && e.SessionId == 1);
    }

    [Fact]
    public void CancellationIsNotFailureOrSuccess()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.Launch);
        var error = new OperationCanceledException();
        Assert.Throws<OperationCanceledException>(() =>
            diagnostics.Measure<int>(StartupPhase.Settings, () => throw error));
        diagnostics.Finish(error);
        Assert.Equal(StartupReadiness.Cancelled, diagnostics.ApplicationReadiness);
        Assert.Contains(diagnostics.Snapshot(), e => e.Kind == StartupEventKind.Cancelled);
        Assert.DoesNotContain(diagnostics.Snapshot(), e => e.Kind == StartupEventKind.Failed);
    }

    [Fact]
    public void SkippedSplashHasNoSuccessfulSplashFrame()
    {
        var diagnostics = Create();
        diagnostics.Begin(StartupPhase.StudioSplash);
        diagnostics.End(StartupPhase.StudioSplash, StartupEventKind.Skipped, detail: "CommandLine");
        Assert.Contains(diagnostics.Snapshot(), e => e.Kind == StartupEventKind.Skipped);
        Assert.False(diagnostics.Has(StartupPhase.StudioSplashFirstFrame));
    }

    [Fact]
    public async Task ConcurrentMilestonesAreUniqueAndMonotonic()
    {
        var diagnostics = Create();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            diagnostics.Mark(StartupPhase.FirstPresentedFrame);
            diagnostics.Mark(StartupPhase.RuntimeAssetsReady);
        }, TestContext.Current.CancellationToken)));
        StartupEvent[] events = diagnostics.Snapshot();
        Assert.Equal(2, events.Length);
        Assert.True(events[1].Timestamp >= events[0].Timestamp);
        Assert.True(events[1].ElapsedMilliseconds >= events[0].ElapsedMilliseconds);
        Assert.Equal(1, events[0].Sequence);
        Assert.Equal(2, events[1].Sequence);
    }

    [Fact]
    public void DisabledInstrumentationCollectsNothingAndStillRunsOperations()
    {
        StartupDiagnostics diagnostics = StartupDiagnostics.Disabled;
        diagnostics.Begin(StartupPhase.Launch);
        Assert.Equal(42, diagnostics.Measure(StartupPhase.Settings, () => 42));
        diagnostics.Mark(StartupPhase.FirstPresentedFrame);
        diagnostics.BeginSession();
        diagnostics.GameplayPresented();
        diagnostics.Finish();
        Assert.Empty(diagnostics.Snapshot());
        Assert.False(diagnostics.Enabled);
    }

    [Theory]
    [InlineData(new string[] { "--startup-diagnostics-output", "startup.json", "--skip-splash" }, true)]
    [InlineData(new string[] { "--startup-diagnostics-output" }, false)]
    [InlineData(new string[] { "--startup-diagnostics-output", "--skip-splash" }, false)]
    public void DiagnosticsArgumentPreservesExistingParserContract(string[] args, bool valid)
    {
        Assert.Equal(valid, Program.TryParseArguments(args, out _, out _, out _, out _));
    }
}
