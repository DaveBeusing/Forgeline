using System.Diagnostics;
using ForgeLine.Client;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ActualOwnerWorkloadTests
{
    private static readonly TimeSpan s_timeout=TimeSpan.FromSeconds(5);
    [Fact]
    public void ActualPublicHostRecordsInitialUnpacedTicksControlsAndRejectedTransportWithoutRateAssumptions()
    {
        using var scenario=CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay,seed:9147,enableDiagnostics:false,enableDebugCapture:false),TestContext.Current.CancellationToken);
        var snapshots=new PresentationSnapshotBuffer();
        var commands=new PlayerCommandGateway(scenario.Simulation,scenario.Services.BuildingCommands,scenario.BattlefieldRuntime.MatchStateEntity,32,scenario.Intelligence,scenario.Services.Weapons,scenario.Services.ArtilleryWeapons);
        scenario.Simulation.RegisterTickObserver(commands);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(snapshots,new PresentationExtractionContext(scenario,scenario.GetBase(new PlayerId(1)).Player,new PresentationInteractionState(),commands)));
        using var host=new ClientSimulationHost(scenario,commands,snapshots,timeProvider:new FrozenTime(),observeWorkload:true);
        var initial=host.CaptureTelemetry().Workload;
        Assert.True(initial.NativeOwnerThread>0);Assert.Equal(!ClientBuildIdentity.DeveloperToolsEnabled,initial.CompositionSupported);
        Assert.True(initial.Completed.Slot0>=1); // Initial extraction tick is unpaced and still counted.
        Assert.True(host.TrySetPaused(true));Assert.True(host.WaitForPauseState(true,s_timeout));
        Assert.True(host.TrySetPaused(false));Assert.True(host.WaitForPauseState(false,s_timeout));
        Assert.True(host.TrySubmit(host.SessionId,static _=>default));Assert.True(host.WaitForSubmissionCompletion(s_timeout,out _));
        Assert.True(host.TrySubmit(SimulationSessionId.None,static _=>default));Assert.True(host.WaitForSubmissionCompletion(s_timeout,out _));
        var transported=host.CaptureTelemetry().Workload;
        Assert.True(transported.Completed.Slot1>=2);Assert.True(transported.Completed.Slot3>=2);
        Assert.True(transported.Events.Slot0>=4);Assert.True(transported.Events.Slot1>=4);Assert.True(transported.Events.Slot4>=2);Assert.True(transported.Events.Slot5>=2);
        Assert.True(transported.TransportStarted.Slot0>=4);Assert.True(transported.TransportStarted.Slot1>=2);
        Assert.Equal(initial.NativeOwnerThread,transported.NativeOwnerThread);Assert.Equal(initial.Identity,transported.Identity);
        Assert.True(host.TryRunSmokeCompletion(scenario.GetBase(new PlayerId(2)).CommandCore));
        Assert.True(host.WaitForTerminalState(true,s_timeout));
        var terminal=host.CaptureTelemetry().Workload;
        Assert.True(terminal.Started.Slot6>=1);Assert.True(terminal.Completed.Slot0>initial.Completed.Slot0);
        host.ThrowIfFaulted();
    }
    [Fact]
    public void WorldAudioOwnerIterationsExistWithoutPublishedBlocksAndAreNotATimeRateCap()
    {
        using var audio=new WaveOutWorldAudio(()=>throw new InvalidOperationException("Must not open while inactive"),static _=>{},observeWorkload:true);
        Assert.True(SpinWait.SpinUntil(()=>audio.Workload.Completed.Slot1>=3,s_timeout));
        var before=audio.Workload;Assert.Equal(0,before.Events.Slot0);Assert.Equal(0,before.Started.Slot2);
        Assert.True(SpinWait.SpinUntil(()=>audio.Workload.Completed.Slot1>before.Completed.Slot1,s_timeout));
        var after=audio.Workload;Assert.True(OwnerWorkloadSnapshot.Touching(before,after,1)>=1);
        Assert.True(after.Available);Assert.Equal(0,after.Events.Slot0);
    }
    [Fact]
    public void RepeatedFactsAndMultiCallerTransportUsePreallocatedCopiedStorage()
    {
        var probe=new OwnerWorkloadProbe(7);probe.BindOwner();for(int i=0;i<32;i++)_ = probe.Capture();
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){probe.BeginTransport(0);probe.EndTransport(0);_ = probe.Capture();}
        long delta=GC.GetAllocatedBytesForCurrentThread()-allocated;Assert.Equal(0,delta);
        probe.BeginTransport(0);probe.BeginTransport(0);var crossing=probe.Capture();
        probe.EndTransport(0);probe.EndTransport(0);var complete=probe.Capture();
        Assert.Equal(2,OwnerWorkloadSnapshot.TouchingTransport(crossing,complete,0));Assert.True(complete.Available);
    }
    private sealed class FrozenTime:TimeProvider
    {
        public override long TimestampFrequency=>1000;
        public override long GetTimestamp()=>1;
    }
}
