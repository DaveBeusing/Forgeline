using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed partial class ClientSimulationHostTests
{
    [Fact]
    public void SessionExecutorAvailabilityFollowsCompiledCapability()
    {
        using var fixture = ClientHostFixture.Create(timeProvider: new ControlledHostTime());
        Assert.Equal(DeveloperToolsCapability.Current.IsCompiled,
            fixture.Host.DeveloperSessions.Status.Availability == DeveloperProviderAvailability.Available);
        var permission = DeveloperToolsCapability.Current.BindSession(fixture.Host.SessionId, true);
        var request = ControlRequest(fixture.Host, DeveloperOperation.Pause);
        var result = fixture.Host.SubmitDeveloper(permission, ControlContext(fixture.Host), request);
        Assert.Equal(DeveloperRequestStatus.Rejected, result.Result.Status);
        Assert.Equal(DeveloperToolsCapability.Current.IsCompiled ? DeveloperFailureReason.SessionNotActivated :
            DeveloperFailureReason.CapabilityUnavailable, result.Result.Failure);
    }

    private static DeveloperExecutionContext ControlContext(ClientSimulationHost host) =>
        new(host.SessionId, host.CaptureControlStatus().Revision, _ => false, _ => false);

    private static DeveloperRequest ControlRequest(ClientSimulationHost host, DeveloperOperation operation,
        params DeveloperValue[] values) => new(host.SessionId, Guid.NewGuid(), host.CaptureControlStatus().Revision,
            operation, DeveloperTarget.Global, DeveloperParameterId.None, values);

#if FORGELINE_DEVELOPER_TOOLS
    private static DeveloperSessionAccess ActivateControls(ClientSimulationHost host)
    {
        var permission = DeveloperToolsCapability.Current.BindSession(host.SessionId, true);
        Assert.True(host.TrySetDeveloperAccess(permission));
        Assert.True(SpinWait.SpinUntil(() => host.CaptureControlStatus().DeveloperActivated, TestTimeout));
        return permission;
    }

    private static ClientDeveloperControlCompletion ReadControl(ClientSimulationHost host)
    {
        ClientDeveloperControlCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(() => host.TryReadDeveloperCompletion(out completion), TestTimeout));
        return completion;
    }

    private static ClientDeveloperControlCompletion ExecuteControl(ClientSimulationHost host,
        DeveloperSessionAccess permission, DeveloperOperation operation, params DeveloperValue[] values)
    {
        var request = ControlRequest(host, operation, values);
        var receipt = host.SubmitDeveloper(permission, ControlContext(host), request);
        Assert.Equal(DeveloperRequestStatus.Accepted, receipt.Result.Status);
        var completion = ReadControl(host);
        Assert.Equal(request.Correlation, completion.Result.Correlation);
        Assert.Equal(receipt.HostSequence, completion.HostSequence);
        Assert.Equal(DeveloperRequestStatus.Applied, completion.Result.Status);
        return completion;
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(10u)]
    [InlineData(100u)]
    public void DeveloperStepsCompleteExactNormalTicksAndRestorePause(uint steps)
    {
        using var fixture = ClientHostFixture.Create(timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        ulong start = host.CaptureControlStatus().CompletedTick.Value;
        var completion = ExecuteControl(host, permission, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(steps));
        Assert.Equal(steps, completion.RequestedSteps);
        Assert.Equal(steps, completion.CompletedSteps);
        Assert.Equal(start + steps, completion.CompletedTick.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(50), host.CaptureControlStatus().TickDuration);
        Assert.True(host.IsPaused);
        Assert.Equal(MatchLifecyclePhase.Paused, fixture.Scenario.GetMatchState().Lifecycle);
        Assert.True(fixture.Snapshots.TryReadLatest(out var snapshot));
        Assert.Equal(completion.CompletedTick, snapshot.Tick);
        Assert.Equal(0u, host.CaptureControlStatus().RemainingSteps);
        Assert.Equal(0, host.OutstandingHostMessages);
    }

    [Theory]
    [InlineData(0.25, 200)]
    [InlineData(0.5, 100)]
    [InlineData(1.0, 50)]
    [InlineData(2.0, 25)]
    [InlineData(4.0, 13)]
    public void DeveloperSpeedUsesFullIntervalsAndIdenticalRequestsKeepDeadline(double speed, int interval)
    {
        var clock = new ControlledHostTime();
        using var fixture = ClientHostFixture.Create(timeProvider: clock);
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        clock.Advance(10_000);
        ExecuteControl(host, permission, DeveloperOperation.SetHostSpeed, DeveloperValue.FromDouble(speed));
        ulong tick = host.CurrentTick.Value;
        ExecuteControl(host, permission, DeveloperOperation.Resume);
        clock.Advance(interval - 1);
        ExecuteControl(host, permission, DeveloperOperation.Resume);
        ExecuteControl(host, permission, DeveloperOperation.SetHostSpeed, DeveloperValue.FromDouble(speed));
        Assert.Equal(tick, host.CurrentTick.Value);
        clock.Advance(1);
        Synchronize(host);
        Assert.True(host.WaitForTickAtLeast(new(tick + 1), TestTimeout));
        Synchronize(host);
        Assert.Equal(tick + 1, host.CurrentTick.Value);
        Assert.Equal(speed, host.CaptureControlStatus().RequestedSpeed);
        Assert.Equal(TimeSpan.FromMilliseconds(50), host.CaptureControlStatus().TickDuration);
        // A real running speed change discards elapsed host debt and starts one new interval.
        double next = speed == 4 ? 0.25 : 4;
        int nextInterval = next == 4 ? 13 : 200;
        clock.Advance(1);
        ExecuteControl(host, permission, DeveloperOperation.SetHostSpeed, DeveloperValue.FromDouble(next));
        tick = host.CurrentTick.Value;
        clock.Advance(nextInterval - 1);
        Synchronize(host);
        Assert.Equal(tick, host.CurrentTick.Value);
        clock.Advance(1);
        Synchronize(host);
        Assert.True(host.WaitForTickAtLeast(new(tick + 1), TestTimeout));
        Synchronize(host);
        Assert.Equal(tick + 1, host.CurrentTick.Value);
    }

    [Fact]
    public void DeveloperControlsRejectStaleSessionRevisionPayloadAndUnboundAccess()
    {
        using var fixture = ClientHostFixture.Create(timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        var oldRequest = ControlRequest(host, DeveloperOperation.Pause);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        Assert.Equal(ClientDeveloperControlFailure.StaleRevision,
            host.SubmitDeveloper(permission, ControlContext(host), oldRequest).Failure);
        var stale = new DeveloperRequest(new SimulationSessionId(host.SessionId.Value + 1), Guid.NewGuid(),
            host.CaptureControlStatus().Revision, DeveloperOperation.Pause, DeveloperTarget.Global, DeveloperParameterId.None, []);
        Assert.Equal(ClientDeveloperControlFailure.OldSession, host.SubmitDeveloper(permission, ControlContext(host), stale).Failure);
        var unbound = DeveloperToolsCapability.Current.BindSession(host.SessionId, true);
        Assert.Equal(ClientDeveloperControlFailure.NotActivated,
            host.SubmitDeveloper(unbound, ControlContext(host), ControlRequest(host, DeveloperOperation.Resume)).Failure);
        foreach (var request in new[] {
            ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(2)),
            ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromDouble(10)),
            ControlRequest(host, DeveloperOperation.SetHostSpeed, DeveloperValue.FromDouble(3)),
            ControlRequest(host, DeveloperOperation.Pause, DeveloperValue.FromBoolean(true)) })
            Assert.Equal(ClientDeveloperControlFailure.InvalidRequest, host.SubmitDeveloper(permission, ControlContext(host), request).Failure);
    }

    [Fact]
    public void PauseOwnersAreIndependentAndMenuTransferIsConfirmedAtBoundary()
    {
        using var fixture = ClientHostFixture.Create(timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        Assert.True(host.MenuPause.TrySetPaused(true)); Synchronize(host);
        Assert.True(host.WindowPause.TrySetPaused(true)); Synchronize(host);
        var receipt = host.MenuPause.TransferToDeveloper(permission, host.CaptureControlStatus().Revision);
        Assert.Equal(DeveloperRequestStatus.Accepted, receipt.Result.Status);
        Assert.Equal(DeveloperRequestStatus.Applied, ReadControl(host).Result.Status);
        Assert.True(host.CaptureControlStatus().MenuPauseTransferred);
        var step = ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(10));
        Assert.Equal(DeveloperRequestStatus.Accepted, host.SubmitDeveloper(permission, ControlContext(host), step).Result.Status);
        Assert.Equal(ClientDeveloperControlFailure.BlockedByPauseOwner, ReadControl(host).Failure);
        ExecuteControl(host, permission, DeveloperOperation.Resume);
        Assert.True(host.IsPaused);
        Assert.True(host.SessionLoadingPause.TrySetPaused(true)); Synchronize(host);
        Assert.True(host.WindowPause.TrySetPaused(false)); Synchronize(host);
        Assert.True(host.IsPaused);
        Assert.Equal(ClientSimulationPauseReason.SessionLoading, host.CaptureControlStatus().PauseReasons);
        var restore = host.MenuPause.RestoreFromDeveloper(permission, host.CaptureControlStatus().Revision);
        Assert.Equal(DeveloperRequestStatus.Accepted, restore.Result.Status);
        ReadControl(host);
        Assert.False(host.CaptureControlStatus().MenuPauseTransferred);
        Assert.True(host.SessionLoadingPause.TrySetPaused(false)); Synchronize(host);
        Assert.True(host.IsPaused);
        Assert.Equal(ClientSimulationPauseReason.Menu, host.CaptureControlStatus().PauseReasons);
    }

    [Theory]
    [InlineData("window")]
    [InlineData("revoke")]
    [InlineData("dispose")]
    [InlineData("loading")]
    public async Task ActiveSeriesIsBoundedAndInterruptibleBetweenCompleteTicks(string interruption)
    {
        using var slow = new ControlledSlowSystem(new(2), TestContext.Current.CancellationToken);
        using var fixture = ClientHostFixture.Create(boundaryCapacity: 1, additionalSystem: slow, timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        var step = ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(100));
        Assert.Equal(DeveloperRequestStatus.Accepted, host.SubmitDeveloper(permission, ControlContext(host), step).Result.Status);
        Task? dispose = null;
        try
        {
            Assert.True(slow.Entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
            Assert.Equal(1UL, host.CaptureControlStatus().CompletedTick.Value);
            Assert.Equal(1, host.OutstandingHostMessages);
            Assert.Equal(ClientDeveloperControlFailure.StepInProgress, host.SubmitDeveloper(permission, ControlContext(host),
                ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(1))).Failure);
            Assert.Equal(ClientDeveloperControlFailure.BoundaryFull, host.SubmitDeveloper(permission, ControlContext(host),
                ControlRequest(host, DeveloperOperation.Pause)).Failure);
            if (interruption == "window") Assert.True(host.WindowPause.TrySetPaused(true));
            else if (interruption == "revoke") Assert.True(host.TrySetDeveloperAccess(DeveloperToolsCapability.Current.BindSession(host.SessionId, false)));
            else if (interruption == "loading") Assert.True(host.SessionLoadingPause.TrySetPaused(true));
            else
            {
                dispose = Task.Run(host.Dispose, TestContext.Current.CancellationToken);
                Assert.True(SpinWait.SpinUntil(() => host.IsStopping, TestTimeout));
            }
        }
        finally { slow.Release(); }
        if (dispose is not null) await dispose;
        var completion = ReadControl(host);
        Assert.Equal(100u, completion.RequestedSteps);
        Assert.Equal(1u, completion.CompletedSteps);
        Assert.Equal(2UL, completion.CompletedTick.Value);
        Assert.Equal(DeveloperRequestStatus.Rejected, completion.Result.Status);
        Assert.Equal(0, host.OutstandingHostMessages);
        if (interruption != "dispose")
        {
            Assert.False(host.CaptureControlStatus().IsStepping);
            if (interruption != "revoke") Assert.True(host.IsPaused);
        }
    }

    [Fact]
    public void ResumeCancelsRemainingSeriesBeforeNormalPacing()
    {
        using var slow = new ControlledSlowSystem(new(2), TestContext.Current.CancellationToken);
        using var fixture = ClientHostFixture.Create(additionalSystem: slow, timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        host.SubmitDeveloper(permission, ControlContext(host), ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(100)));
        try
        {
            Assert.True(slow.Entered.Wait(TestTimeout, TestContext.Current.CancellationToken));
            Assert.Equal(DeveloperRequestStatus.Accepted, host.SubmitDeveloper(permission, ControlContext(host), ControlRequest(host, DeveloperOperation.Resume)).Result.Status);
        }
        finally { slow.Release(); }
        var step = ReadControl(host);
        var resume = ReadControl(host);
        Assert.Equal(ClientDeveloperControlFailure.Cancelled, step.Failure);
        Assert.Equal(1u, step.CompletedSteps);
        Assert.Equal(DeveloperRequestStatus.Applied, resume.Result.Status);
        Assert.False(host.IsPaused);
        Assert.Equal(2UL, host.CurrentTick.Value);
    }

    [Fact]
    public void TerminalStepPublishesActualCountAndNoFurtherTick()
    {
        var terminator = new TerminalStepSystem();
        using var fixture = ClientHostFixture.Create(additionalSystem: terminator, timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        terminator.Core = fixture.Scenario.GetBase(new PlayerId(2)).CommandCore;
        var completion = ExecuteControl(host, permission, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(100));
        Assert.Equal(ClientDeveloperControlFailure.Terminal, completion.Failure);
        Assert.Equal(1u, completion.CompletedSteps);
        Assert.True(host.IsTerminalFrozen);
        Assert.Equal(2UL, host.CurrentTick.Value);
        Assert.Equal(ClientDeveloperControlFailure.Terminal, host.SubmitDeveloper(permission, ControlContext(host), ControlRequest(host, DeveloperOperation.Resume)).Failure);
    }

    [Fact]
    public void FaultedTickDoesNotCountAsCompletedOrRepublishPartialSnapshot()
    {
        using var fixture = ClientHostFixture.Create(additionalSystem: new FaultingSystem(new(2)), timeProvider: new ControlledHostTime());
        var host = fixture.Host;
        var permission = ActivateControls(host);
        ExecuteControl(host, permission, DeveloperOperation.Pause);
        host.SubmitDeveloper(permission, ControlContext(host), ControlRequest(host, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(10)));
        var completion = ReadControl(host);
        Assert.Equal(ClientDeveloperControlFailure.Faulted, completion.Failure);
        Assert.Equal(0u, completion.CompletedSteps);
        Assert.Equal(1UL, completion.CompletedTick.Value);
        Assert.True(host.WaitForFault(TestTimeout));
        Assert.True(fixture.Snapshots.TryReadLatest(out var snapshot));
        Assert.Equal(1UL, snapshot.Tick.Value);
    }

    private sealed class TerminalStepSystem : ISimulationSystem
    {
        internal EntityId Core { get; set; }
        public SimulationPhase Phase => SimulationPhase.InputCommands;
        public void Execute(SimulationContext context)
        { if (context.Tick.Value == 2) context.Entities.DestroyEntity(Core); }
    }
#endif
}
