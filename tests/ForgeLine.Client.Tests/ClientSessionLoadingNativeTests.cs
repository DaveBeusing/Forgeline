using System.Diagnostics;
using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSessionLoadingNativeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadingPresentationDoesNotActivateGameplayBeforeVerifiedSnapshotPresentation(bool diagnosticsEnabled)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Session Loading Test", 640, 360));
        using var release = new ManualResetEventSlim();
        var diagnostics = diagnosticsEnabled
            ? new StartupDiagnostics(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1) : StartupDiagnostics.Disabled;
        diagnostics.BeginSession();
        var target = new GraphicsWindowTarget(window.NativeHandle.Value, 640, 360, false);
        using var transition = new ClientFrontendRenderHost(target,
            initialSurface: ClientSessionLoadingLoop.Surface(new(ClientSessionLoadPhase.ScenarioAssembly)),
            asynchronousStartup: true);
        using var coordinator = new ClientSessionLoadingCoordinator<ClientLoadedSession>((token, report) =>
        {
            release.Wait(token);
            return ClientSessionFactory.CreateOwned(ClientSessionRequest.NewGame(947), report, token);
        }, TestContext.Current.CancellationToken);
        var timeout = Stopwatch.StartNew();
        int presentedPumps = 0;
        bool Pump()
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), "Native session handoff timed out.");
            return platform.PumpEvents() && window.IsOpen;
        }
        Assert.True(ClientSessionLoadingLoop.Wait(coordinator, () =>
        {
            bool open = Pump(); transition.ThrowIfFaulted();
            if (transition.HasPresentedFrame && !release.IsSet && ++presentedPumps == 10)
            {
                Assert.False(coordinator.Completed);
                Assert.Equal(StartupReadiness.Pending, diagnostics.SessionReadiness);
                release.Set();
            }
            return open;
        }, () => false, view => transition.Publish(view), () => platform.WaitForEvents(TimeSpan.FromMilliseconds(16))));
        Assert.True(coordinator.TryTake(out ClientLoadedSession? transferred));
        using ClientLoadedSession loaded = transferred!;
        MatchRuntime runtime = loaded.Runtime;
        var snapshots = new PresentationSnapshotBuffer();
        var commands = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity);
        var extraction = new PresentationExtractionContext(runtime, new PlayerId(1), new PresentationInteractionState(), commands);
        runtime.Simulation.AttachTickObserver(commands);
        runtime.Simulation.AttachTickObserver(new PresentationExtractor(snapshots, extraction));
        using var simulation = new ClientSimulationHost(runtime, commands, snapshots, asynchronousStartup: true);
        while (!snapshots.TryReadLatest(out _))
        {
            simulation.ThrowIfFaulted(); Assert.True(Pump());
            platform.WaitForEvents(TimeSpan.FromMilliseconds(16));
        }
        transition.Dispose();
        var cameraSettings = new ClientUserSettings().CreateCameraSettings(new Vector3(700, 0, 1500));
        using var renderer = new ClientRenderHost(target, runtime.Terrain, snapshots, cameraSettings,
            startup: diagnostics, asynchronousStartup: true, expectedSession: simulation.SessionId);
        var camera = new RtsCamera(cameraSettings);
        ClientRenderFrame Frame(FrontendSurfaceView? frontend) => new(camera.CaptureState(),
            640, 360, false, default, default, default, FormationTemplate.Compact, [], [], [], [], Frontend: frontend);
        while (renderer.Health.CompletedFrames == 0)
        {
            Assert.True(Pump()); renderer.ThrowIfFaulted();
            renderer.Publish(Frame(ClientSessionLoadingLoop.Surface(new(ClientSessionLoadPhase.RendererReadiness))));
            platform.WaitForEvents(TimeSpan.FromMilliseconds(16));
        }
        Assert.False(renderer.HasPresentedGameplayFrame);
        Assert.Equal(StartupReadiness.Pending, diagnostics.SessionReadiness);
        Assert.True(ClientSessionLoadingLoop.WaitForPresentation(Pump, () => false,
            () => renderer.HasPresentedGameplayFrame,
            () => { renderer.ThrowIfFaulted(); simulation.ThrowIfFaulted(); renderer.Publish(Frame(null)); },
            () => platform.WaitForEvents(TimeSpan.FromMilliseconds(16)),
            () => { renderer.RequestStop(); simulation.RequestStop(); },
            () => !renderer.IsExecutionThreadAlive && !simulation.IsExecutionThreadAlive));
        Assert.True(renderer.HasPresentedGameplayFrame);
        if (diagnosticsEnabled)
        {
            Assert.Equal(StartupReadiness.Ready, diagnostics.SessionReadiness);
            Assert.True(diagnostics.Has(StartupPhase.FirstGameplayFrame, 1));
        }
    }
}
