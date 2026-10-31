using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class PlatformWorkloadProbeTests
{
    [Fact]
    public void RemovedQuitIsNotDispatchedAndPumpsMayContainNoMessages()
    {
        var probe=new PlatformWorkloadProbe();probe.BindWindow(1,0,0);
        probe.BeginPump();probe.EndPump(true);
        probe.BeginPump();probe.Removed(true);probe.Removed(false);probe.BeginDispatch();
        var crossing=probe.Capture();Assert.Equal(2,crossing.RemovedMessages);Assert.Equal(1,crossing.QuitMessages);Assert.Equal(1,crossing.DispatchStarted);Assert.Equal(0,crossing.DispatchCompleted);
        probe.EndDispatch(true);probe.EndPump(true);var value=probe.Capture();Assert.Equal(2,value.PumpCompleted);Assert.True(value.Available);
    }
    [Fact]
    public void InitialQueuesGrowthWithoutDrainAndCopiedHighWaterAreRetained()
    {
        var probe=new PlatformWorkloadProbe();probe.BindWindow(1,2,3);
        probe.Enqueued(true,4);probe.Enqueued(true,5);probe.Enqueued(false,3);
        var before=probe.Capture();Assert.Equal(2,before.InitialWindowDepth);Assert.Equal(3,before.InitialInputDepth);Assert.Equal(5,before.InputHighWater);Assert.Equal(2,before.InputEnqueued);
        probe.Dequeued(true,4);probe.DiscardInput(4);var after=probe.Capture();Assert.Equal(5,before.InputDepth);Assert.Equal(0,after.InputDepth);Assert.Equal(4,after.InputDiscarded);Assert.Equal(5,after.InputHighWater);
    }
    [Fact]
    public void MissingForeignWindowAndFaultedDispatchAreNotValidFacts()
    {
        var probe=new PlatformWorkloadProbe();Assert.False(probe.Capture().Available);probe.BindWindow(1,0,0);probe.BindWindow(2,0,0);Assert.False(probe.Capture().Available);
        var other=new PlatformWorkloadProbe();other.BindWindow(1,0,0);other.BeginDispatch();other.EndDispatch(false);Assert.True(other.Capture().Faulted);
    }
}
