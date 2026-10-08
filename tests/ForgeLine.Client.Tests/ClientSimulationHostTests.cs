using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSimulationHostTests
{
    [Fact]
    public void CompletedGpuQualificationPreservesMeasuredSceneAndCpuCounters()
    {
        ClientVisualQualificationSnapshot snapshot = default(ClientVisualQualificationSnapshot) with
        {
            VisibleInstances = 17,
            FrameMilliseconds = 8.0,
            CpuRenderMilliseconds = 3.0,
            TotalMeasuredDrawCalls = 5
        };
        var graphics = new GraphicsDiagnostics(new GraphicsDeviceInfo("test", 0, false, "test", true), default)
        {
            GpuTimingAvailable = true,
            GpuFrameMilliseconds = 2.5,
            Debug = new GraphicsDebugDiagnostics(true, 1, 2)
        };
        ClientVisualQualificationSnapshot completed = ClientRenderHost.CompleteGpuQualification(snapshot, graphics);
        Assert.Equal(snapshot with
        {
            GpuTimingAvailable = true,
            GpuMilliseconds = 2.5,
            DebugLayerEnabled = true,
            DebugLayerWarningCount = 1,
            DebugLayerErrorCount = 2
        }, completed);
    }

    [Fact]
    public void CompletedGpuQualificationDoesNotInventUnavailableTiming()
    {
        ClientVisualQualificationSnapshot snapshot = default(ClientVisualQualificationSnapshot) with
        {
            GpuTimingAvailable = true,
            GpuMilliseconds = 9
        };
        var graphics = new GraphicsDiagnostics(new GraphicsDeviceInfo("test", 0, false, "test", false), default);
        ClientVisualQualificationSnapshot completed = ClientRenderHost.CompleteGpuQualification(snapshot, graphics);
        Assert.False(completed.GpuTimingAvailable);
        Assert.Null(completed.GpuMilliseconds);
    }

    private static readonly TimeSpan TestTimeout =
        TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConcurrentRenderDisposersWaitForTheActiveFrame()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var host = new ClientRenderHost(_ =>
        {
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
        });
        host.Publish(CreateRenderFrame(800));
        Assert.True(entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
        Task first = Task.Run(host.Dispose, TestContext.Current.CancellationToken);
        Task second = Task.Run(host.Dispose, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => host.IsStopping, TestTimeout));
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(host.Publish(CreateRenderFrame(1024)));
        }
        finally
        {
            release.Set();
        }
        await Task.WhenAll(first, second).WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        Assert.False(host.IsExecutionThreadAlive);
        host.Dispose();
    }

    [Fact]
    public async Task ConcurrentSimulationDisposersWaitForTheActiveTick()
    {
        using var slowSystem = new ControlledSlowSystem(new SimulationTick(2), TestContext.Current.CancellationToken);
        using ClientHostFixture fixture = ClientHostFixture.Create(additionalSystem: slowSystem);
        Assert.True(slowSystem.Entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
        Task first = Task.Run(fixture.Host.Dispose, TestContext.Current.CancellationToken);
        Task second = Task.Run(fixture.Host.Dispose, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => fixture.Host.IsStopping, TestTimeout));
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(fixture.Host.TrySetPaused(true));
        }
        finally
        {
            slowSystem.Release();
        }
        await Task.WhenAll(first, second).WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        Assert.False(fixture.Host.IsExecutionThreadAlive);
    }

    [Fact]
    public void FaultingSubmissionPublishesOneCompletionAndRestoresBoundaryAccounting()
    {
        using ClientHostFixture fixture = ClientHostFixture.Create();
        Assert.True(fixture.Host.TrySubmit(fixture.Host.SessionId, static _ =>
            throw new InvalidOperationException("controlled submission failure")));
        Assert.True(fixture.Host.WaitForFault(TestTimeout));
        Assert.Throws<InvalidOperationException>(fixture.Host.ThrowIfFaulted);
        Assert.True(fixture.Host.TryReadSubmissionCompletion(out ClientSubmissionCompletion completion));
        Assert.Equal(ClientSubmissionFailure.Faulted, completion.Failure);
        Assert.False(fixture.Host.TryReadSubmissionCompletion(out _));
        Assert.Equal(0, fixture.Host.OutstandingHostMessages);
    }

    [Fact]
    public void SimulationOwnerAdvancesWithoutBlockingCallingThread()
    {
        using var slowSystem =
            new ControlledSlowSystem(
                blockAtTick: new SimulationTick(2),
                TestContext.Current.CancellationToken);
        using ClientHostFixture fixture =
            ClientHostFixture.Create(
                additionalSystem: slowSystem);

        try
        {
            Assert.True(
                slowSystem.Entered.Wait(
                    TestTimeout,
                    TestContext.Current.CancellationToken));

            Assert.Equal(
                new SimulationTick(2),
                fixture.Host.CurrentTick);
            Assert.True(
                fixture.Snapshots.TryReadLatest(
                    out PresentationSnapshot completedSnapshot));
            Assert.Equal(
                new SimulationTick(1),
                completedSnapshot.Tick);

            int callingThreadProgress = 0;
            for (int index = 0;
                 index < 10_000;
                 index++)
            {
                callingThreadProgress++;
            }

            Assert.Equal(
                10_000,
                callingThreadProgress);
        }
        finally
        {
            slowSystem.Release();
        }

        Assert.True(
            fixture.Host.WaitForTickAtLeast(
                new SimulationTick(2),
                TestTimeout));
    }

    [Fact]
    public void PauseAndResumeApplyBetweenCompletedTicks()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create();

        Assert.True(
            fixture.Host.TrySetPaused(
                paused: true));
        Assert.True(
            fixture.Host.WaitForPauseState(
                paused: true,
                TestTimeout));
        Assert.Equal(
            MatchLifecyclePhase.Paused,
            fixture.Scenario.GetMatchState().Lifecycle);

        SimulationTick pausedAt =
            fixture.Host.CurrentTick;

        Assert.True(
            fixture.Host.TrySetPaused(
                paused: false));
        Assert.True(
            fixture.Host.WaitForPauseState(
                paused: false,
                TestTimeout));
        Assert.Equal(
            MatchLifecyclePhase.Running,
            fixture.Scenario.GetMatchState().Lifecycle);
        Assert.True(
            fixture.Host.WaitForTickAtLeast(
                new SimulationTick(
                    pausedAt.Value + 1),
                TestTimeout));
    }

    [Fact]
    public void HostBoundaryRetainsCapacityUntilSubmissionReceiptIsConsumed()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create(
                boundaryCapacity: 1);

        EntityId unit =
            fixture.Scenario.GetBase(new PlayerId(1)).StartingUnits[0];
        SimulationTick observed =
            fixture.Host.CurrentTick;

        Assert.True(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                    gateway.SubmitMovement(
                        fixture.Scenario.GetBase(new PlayerId(1)).Player,
                        [unit],
                        new Vector3(480.0f, 0.0f, 1_480.0f),
                        observed,
                        FormationTemplate.Compact)));

        Assert.False(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                    gateway.SubmitMovement(
                        fixture.Scenario.GetBase(new PlayerId(1)).Player,
                        [unit],
                        new Vector3(500.0f, 0.0f, 1_480.0f),
                        observed,
                        FormationTemplate.Compact)));

        Assert.True(
            fixture.Host.WaitForSubmissionCompletion(
                TestTimeout,
                out ClientSubmissionCompletion first));
        Assert.True(
            first.Accepted);

        Assert.True(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                    gateway.SubmitMovement(
                        fixture.Scenario.GetBase(new PlayerId(1)).Player,
                        [unit],
                        new Vector3(520.0f, 0.0f, 1_480.0f),
                        fixture.Host.CurrentTick,
                        FormationTemplate.Compact)));
    }

    [Fact]
    public void OldSessionSubmissionIsRejectedWithoutInvokingGateway()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create();

        int gatewayCalls = 0;
        var staleSession =
            new SimulationSessionId(
                fixture.Host.SessionId.Value + 1);

        Assert.True(
            fixture.Host.TrySubmit(
                staleSession,
                gateway =>
                {
                    Interlocked.Increment(
                        ref gatewayCalls);

                    return gateway.SubmitEndMatch(
                        fixture.Scenario.GetBase(new PlayerId(1)).Player,
                        fixture.Host.CurrentTick);
                }));

        Assert.True(
            fixture.Host.WaitForSubmissionCompletion(
                TestTimeout,
                out ClientSubmissionCompletion completion));

        Assert.Equal(
            ClientSubmissionFailure.OldSession,
            completion.Failure);
        Assert.Equal(
            0,
            gatewayCalls);
        Assert.Null(
            completion.Receipt);
    }

    [Fact]
    public void AcceptedSubmissionInvokesGatewayExactlyOnce()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create();

        int gatewayCalls = 0;
        EntityId unit =
            fixture.Scenario.GetBase(new PlayerId(1)).StartingUnits[0];
        SimulationTick observed =
            fixture.Host.CurrentTick;

        Assert.True(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                {
                    Interlocked.Increment(
                        ref gatewayCalls);

                    return gateway.SubmitMovement(
                        fixture.Scenario.GetBase(new PlayerId(1)).Player,
                        [unit],
                        new Vector3(480.0f, 0.0f, 1_500.0f),
                        observed,
                        FormationTemplate.Compact);
                }));

        Assert.True(
            fixture.Host.WaitForSubmissionCompletion(
                TestTimeout,
                out ClientSubmissionCompletion completion));

        Assert.True(
            completion.Accepted);
        Assert.Equal(
            1,
            gatewayCalls);
        Assert.True(
            completion.Receipt.HasValue);

        SimulationTick target =
            completion.Receipt.Value.TargetTick;

        Assert.True(
            fixture.Host.WaitForTickAtLeast(
                target,
                TestTimeout));

        Assert.True(
            fixture.Host.CommandResults.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            completion.Receipt.Value.CorrelationId,
            result.CorrelationId);
    }

    [Fact]
    public void TerminalAcknowledgementDoesNotAdvanceGameplayTick()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create();

        Assert.True(
            fixture.Host.TryRunSmokeCompletion(
                fixture.Scenario.GetBase(new PlayerId(2)).CommandCore));
        Assert.True(
            fixture.Host.WaitForTerminalState(
                terminal: true,
                TestTimeout));
        Assert.True(
            fixture.Host.WaitForPlayerMatchStatus(
                PlayerMatchStatus.Victory,
                TestTimeout));

        SimulationTick terminalTick =
            fixture.Host.CurrentTick;

        Assert.True(
            fixture.Host.TryAcknowledgeTerminal(
                fixture.Host.SessionId,
                fixture.Scenario.GetBase(new PlayerId(1)).Player,
                terminalTick));
        Assert.True(
            fixture.Host.WaitForPlayerMatchStatus(
                PlayerMatchStatus.Ended,
                TestTimeout));

        Assert.Equal(
            terminalTick,
            fixture.Host.CurrentTick);

        MatchState finalized =
            fixture.Scenario.GetMatchState();

        Assert.Equal(
            MatchLifecyclePhase.Completed,
            finalized.Lifecycle);
        Assert.Equal(
            MatchOutcome.Victory,
            finalized.Outcome);
        Assert.Equal(
            MatchTerminationReason.CommandCoreDestroyed,
            finalized.TerminationReason);
    }

    [Fact]
    public void SlowRenderOwnerDoesNotBlockFramePublisher()
    {
        using var entered =
            new ManualResetEventSlim(false);
        using var release =
            new ManualResetEventSlim(false);
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var host =
            new ClientRenderHost(
                _ =>
                {
                    entered.Set();
                    release.Wait(
                        cancellationToken);
                });

        ClientRenderFrame first =
            CreateRenderFrame(
                viewportWidth: 800);
        ClientRenderFrame second =
            CreateRenderFrame(
                viewportWidth: 1_024);

        Assert.True(
            host.Publish(first));

        try
        {
            Assert.True(
                entered.Wait(
                    TestTimeout,
                    TestContext.Current.CancellationToken));

            Assert.True(
                host.Publish(second));

            int publisherProgress = 0;
            for (int index = 0;
                 index < 10_000;
                 index++)
            {
                publisherProgress++;
            }

            Assert.Equal(
                10_000,
                publisherProgress);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void DisposeJoinsRenderOwner()
    {
        var host =
            new ClientRenderHost(
                static _ =>
                {
                });

        Assert.True(
            host.IsExecutionThreadAlive);

        host.Dispose();

        Assert.False(
            host.IsExecutionThreadAlive);
    }

    [Fact]
    public void SimulationOwnerFailurePropagatesToHost()
    {
        using ClientHostFixture fixture =
            ClientHostFixture.Create(
                additionalSystem:
                    new FaultingSystem(
                        new SimulationTick(2)));

        Assert.True(
            fixture.Host.WaitForFault(
                TestTimeout));
        Assert.Throws<InvalidOperationException>(
            fixture.Host.ThrowIfFaulted);
    }

    [Fact]
    public void RenderOwnerFailurePropagatesToHost()
    {
        using var host =
            new ClientRenderHost(
                static _ =>
                    throw new InvalidOperationException(
                        "controlled render failure"));

        Assert.True(
            host.Publish(
                CreateRenderFrame(
                    viewportWidth: 800)));
        Assert.True(
            host.WaitForFault(
                TestTimeout));
        Assert.Throws<InvalidOperationException>(
            host.ThrowIfFaulted);
    }

    [Fact]
    public void ShutdownWaitsForSlowSimulationTickToComplete()
    {
        using var slowSystem =
            new ControlledSlowSystem(
                blockAtTick: new SimulationTick(2),
                TestContext.Current.CancellationToken);
        using ClientHostFixture fixture =
            ClientHostFixture.Create(
                additionalSystem: slowSystem);
        using var disposeStarted =
            new ManualResetEventSlim(false);
        using var disposeFinished =
            new ManualResetEventSlim(false);

        Assert.True(
            slowSystem.Entered.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));

        var disposer =
            new Thread(
                () =>
                {
                    disposeStarted.Set();
                    fixture.Host.Dispose();
                    disposeFinished.Set();
                });

        disposer.Start();

        Assert.True(
            disposeStarted.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));
        Assert.False(
            disposeFinished.IsSet);

        slowSystem.Release();

        Assert.True(
            disposeFinished.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));
        disposer.Join();

        Assert.False(
            fixture.Host.IsExecutionThreadAlive);
    }

    [Fact]
    public void ShutdownWaitsForSlowRenderFrameToComplete()
    {
        using var entered =
            new ManualResetEventSlim(false);
        using var release =
            new ManualResetEventSlim(false);
        using var disposeStarted =
            new ManualResetEventSlim(false);
        using var disposeFinished =
            new ManualResetEventSlim(false);
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var host =
            new ClientRenderHost(
                _ =>
                {
                    entered.Set();
                    release.Wait(
                        cancellationToken);
                });

        Assert.True(
            host.Publish(
                CreateRenderFrame(
                    viewportWidth: 800)));
        Assert.True(
            entered.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));

        var disposer =
            new Thread(
                () =>
                {
                    disposeStarted.Set();
                    host.Dispose();
                    disposeFinished.Set();
                });

        disposer.Start();

        Assert.True(
            disposeStarted.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));
        Assert.False(
            disposeFinished.IsSet);

        release.Set();

        Assert.True(
            disposeFinished.Wait(
                TestTimeout,
                TestContext.Current.CancellationToken));
        disposer.Join();

        Assert.False(
            host.IsExecutionThreadAlive);
    }

    [Fact]
    public void DisposeJoinsSimulationOwner()
    {
        ClientHostFixture fixture =
            ClientHostFixture.Create();

        Assert.True(
            fixture.Host.IsExecutionThreadAlive);

        fixture.Dispose();

        Assert.False(
            fixture.Host.IsExecutionThreadAlive);
    }

    private static ClientRenderFrame CreateRenderFrame(
        int viewportWidth) =>
        new(
            new RtsCamera().CaptureState(),
            viewportWidth,
            720,
            OverlayEnabled: false,
            DebugOverlayView.Disabled,
            default,
            default,
            FormationTemplate.Compact,
            [],
            [],
            [],
            []);

    [Fact]
    public void PublishedArraysRemainOwnedAcrossMutationAndDroppedFrames()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var observed = new ManualResetEventSlim(false);
        DebugLine captured = default;
        int renders = 0;
        using var host = new ClientRenderHost(frame =>
        {
            if (Interlocked.Increment(ref renders) != 1) return;
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
            captured = frame.GameplayLines[0];
            observed.Set();
        });
        var original = new DebugLine(System.Numerics.Vector3.One, System.Numerics.Vector3.Zero, System.Numerics.Vector4.One);
        DebugLine[] source = [original];
        try
        {
            Assert.True(host.Publish(CreateRenderFrame(800) with { GameplayLines = source }));
            Assert.True(entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
            source[0] = default;
            for (int i = 0; i < 128; i++)
                Assert.True(host.Publish(CreateRenderFrame(800) with { GameplayLines = new DebugLine[i + 1] }));
            release.Set();
            Assert.True(observed.Wait(TestTimeout, TestContext.Current.CancellationToken));
            Assert.Equal(original, captured);
        }
        finally { release.Set(); }
    }

    private sealed class ClientHostFixture : IDisposable
    {
        private bool _disposed;

        private ClientHostFixture(
            MatchRuntime scenario,
            PresentationSnapshotBuffer snapshots,
            ClientSimulationHost host)
        {
            Scenario = scenario;
            Snapshots = snapshots;
            Host = host;
        }

        public MatchRuntime Scenario { get; }

        public PresentationSnapshotBuffer Snapshots { get; }

        public ClientSimulationHost Host { get; }

        public static ClientHostFixture Create(
            int boundaryCapacity = 32,
            ISimulationSystem? additionalSystem = null)
        {
            MatchScenarioSettings settings =
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Gameplay);
            MatchRuntimeSettings runtime =
                CentralDivideScenario.CreateHeadless(
                    settings.Profile,
                    seed: 9147,
                    enableDiagnostics: false,
                    enableDebugCapture: false) with
                {
                    Scenario = settings,
                    Participants =
                        CentralDivideScenario
                            .CreateDefaultParticipants(
                                westComputerControlled: false,
                                eastComputerControlled: true)
                };

            MatchRuntime scenario =
                CentralDivideScenario.Create(
                    runtime);

            if (additionalSystem is not null)
            {
                scenario.Simulation.RegisterSystem(
                    additionalSystem);
            }

            var snapshots =
                new PresentationSnapshotBuffer();
            var interaction =
                new PresentationInteractionState();
            var commands =
                new PlayerCommandGateway(
                    scenario.Simulation,
                    scenario.Services.BuildingCommands,
                    scenario.BattlefieldRuntime.MatchStateEntity,
                    32,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons);
            var extraction =
                new PresentationExtractionContext(
                    scenario,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                    interaction,
                    commands);

            scenario.Simulation.RegisterTickObserver(
                commands);
            scenario.Simulation.RegisterTickObserver(
                new PresentationExtractor(
                    snapshots,
                    extraction));

            var host =
                new ClientSimulationHost(
                    scenario,
                    commands,
                    snapshots,
                    boundaryCapacity);

            return new ClientHostFixture(
                scenario,
                snapshots,
                host);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Host.Dispose();
            Scenario.Dispose();
            _disposed = true;
        }
    }

    private sealed class FaultingSystem :
        ISimulationSystem
    {
        private readonly SimulationTick _faultAtTick;

        public FaultingSystem(
            SimulationTick faultAtTick)
        {
            _faultAtTick =
                faultAtTick;
        }

        public SimulationPhase Phase =>
            SimulationPhase.AiDecisions;

        public void Execute(
            SimulationContext context)
        {
            if (context.Tick ==
                _faultAtTick)
            {
                throw new InvalidOperationException(
                    "controlled simulation failure");
            }
        }
    }

    private sealed class ControlledSlowSystem :
        ISimulationSystem,
        IDisposable
    {
        private readonly SimulationTick _blockAtTick;
        private readonly ManualResetEventSlim _release =
            new(false);
        private readonly CancellationToken _cancellationToken;

        public ControlledSlowSystem(
            SimulationTick blockAtTick,
            CancellationToken cancellationToken)
        {
            _blockAtTick =
                blockAtTick;
            _cancellationToken =
                cancellationToken;
        }

        public ManualResetEventSlim Entered { get; } =
            new(false);

        public SimulationPhase Phase =>
            SimulationPhase.AiDecisions;

        public void Execute(
            SimulationContext context)
        {
            if (context.Tick !=
                _blockAtTick)
            {
                return;
            }

            Entered.Set();
            _release.Wait(
                _cancellationToken);
        }

        public void Release() =>
            _release.Set();

        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
            Entered.Dispose();
        }
    }
}
