using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSimulationHostTests
{
    private static readonly TimeSpan TestTimeout =
        TimeSpan.FromSeconds(5);

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
            fixture.Scenario.West.StartingUnits[0];
        SimulationTick observed =
            fixture.Host.CurrentTick;

        Assert.True(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                    gateway.SubmitMovement(
                        fixture.Scenario.West.Player,
                        [unit],
                        new Vector3(480.0f, 0.0f, 1_480.0f),
                        observed,
                        FormationTemplate.Compact)));

        Assert.False(
            fixture.Host.TrySubmit(
                fixture.Host.SessionId,
                gateway =>
                    gateway.SubmitMovement(
                        fixture.Scenario.West.Player,
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
                        fixture.Scenario.West.Player,
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
                        fixture.Scenario.West.Player,
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
            fixture.Scenario.West.StartingUnits[0];
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
                        fixture.Scenario.West.Player,
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
                fixture.Scenario.East.CommandCore));
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
                fixture.Scenario.West.Player,
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

    private sealed class ClientHostFixture : IDisposable
    {
        private bool _disposed;

        private ClientHostFixture(
            VerticalSliceScenario scenario,
            PresentationSnapshotBuffer snapshots,
            ClientSimulationHost host)
        {
            Scenario = scenario;
            Snapshots = snapshots;
            Host = host;
        }

        public VerticalSliceScenario Scenario { get; }

        public PresentationSnapshotBuffer Snapshots { get; }

        public ClientSimulationHost Host { get; }

        public static ClientHostFixture Create(
            int boundaryCapacity = 32,
            ISimulationSystem? additionalSystem = null)
        {
            VerticalSliceScenarioSettings settings =
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Gameplay);
            VerticalSliceRuntimeSettings runtime =
                VerticalSliceRuntimeSettings.CreateHeadless(
                    settings.Profile,
                    seed: 9147,
                    enableDiagnostics: false,
                    enableDebugCapture: false) with
                {
                    Scenario = settings,
                    Participants =
                        VerticalSliceRuntimeSettings
                            .CreateDefaultParticipants(
                                westComputerControlled: false,
                                eastComputerControlled: true)
                };

            VerticalSliceScenario scenario =
                VerticalSliceScenario.Create(
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
                    scenario.West.Player,
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
