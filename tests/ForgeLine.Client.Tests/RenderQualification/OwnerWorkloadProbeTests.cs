using System.Reflection;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class OwnerWorkloadProbeTests
{
    [Fact]
    public void CrossingOwnerInvocationUsesStartedAfterMinusCompletedBefore()
    {
        var probe=new OwnerWorkloadProbe(7);probe.BindOwner();
        long first=probe.Begin(0);
        var before=probe.Capture();
        probe.End(0,first);long next=probe.Begin(0);
        var after=probe.Capture();
        Assert.Equal(2,OwnerWorkloadSnapshot.Touching(before,after,0));
        Assert.Equal(next,after.InFlight.Slot0);Assert.Equal(1,after.Completed.Slot0);
        probe.End(0,next);Assert.True(probe.Capture().Available);
    }
    [Fact]
    public void UnboundForeignRegressingAndIncoherentObservationsStayUnavailable()
    {
        var probe=new OwnerWorkloadProbe(7);Assert.False(probe.Capture().Available);probe.BindOwner();
        var before=probe.Capture();long operation=probe.Begin(0);probe.End(0,operation);var after=probe.Capture();
        foreach(var invalid in new[]{after with {NativeOwnerThread=0},after with {NativeOwnerThread=after.NativeOwnerThread+1},after with {Identity=Guid.NewGuid()},after with {Completed=after.Completed with {Slot0=2}},after with {Started=after.Started with {Slot1=-1}},after with {InFlight=after.InFlight with {Slot1=10}},after with {CompositionSupported=false}})
            Assert.Throws<InvalidDataException>(()=>OwnerWorkloadSnapshot.Touching(before,invalid,0));
        Assert.Throws<InvalidDataException>(()=>OwnerWorkloadSnapshot.Touching(after,before,0));
    }
    [Fact]
    public void QueueTransitionAndCumulativeEventsAreOneCopiedPoint()
    {
        var probe=new OwnerWorkloadProbe(7,4);probe.BindOwner();
        probe.QueueState(3,0,1,(int)SimulationWorkloadEvent.Admitted);
        var value=probe.Capture();Assert.Equal(1,value.Events.Slot1);Assert.Equal(3,value.Queued);Assert.Equal(1,value.CompletionDepth);Assert.Equal(4,value.HighWater);
        probe.QueueState(2,1,1,(int)SimulationWorkloadEvent.Drained);
        Assert.Equal(3,value.Queued);Assert.Equal(1,probe.Capture().Events.Slot3);
    }
    [Fact]
    public void OverflowAndFailedOrReboundOwnersInvalidateWithoutChangingWork()
    {
        var probe=new OwnerWorkloadProbe(7);probe.BindOwner();
        var counts=(long[])typeof(OwnerWorkloadProbe).GetField("_started",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(probe)!;
        counts[0]=long.MaxValue;_ = probe.Begin(0);Assert.True(probe.Capture().Overflowed);Assert.False(probe.Capture().Available);
        var second=new OwnerWorkloadProbe(8);second.BindOwner();long operation=second.Begin(0);second.End(0,operation,false);Assert.True(second.Capture().Faulted);
        var foreign=new Thread(second.BindOwner);foreign.Start();foreign.Join();Assert.False(second.Capture().Available);
    }
}
