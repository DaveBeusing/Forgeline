using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class OwnerCycleObservationTests
{
    private static object? s_escape;
    private static OwnerWorkloadFacts Facts()
    {
        long before=Stopwatch.GetTimestamp();return new(before,Stopwatch.GetTimestamp(),default,default,default,default,true,false);
    }
    [Fact]
    public void SharedEdgesIncludePositiveLeadPayloadAndAckTailWithoutChangingLegacyDelta()
    {
        using var legacy=new LiveRenderTelemetry(new Source(),1){ObserveAbsoluteReadBrackets=true};
        var cycles=new OwnerCycleObservations(4,true,Facts);legacy.OwnerCycles=cycles;
        cycles.Edge();Volatile.Write(ref s_escape,new byte[4096]);
        var before=Facts();var start=legacy.Begin();var after=Facts();cycles.BeginPayload(start,before,after);
        Volatile.Write(ref s_escape,new byte[8192]);
        before=Facts();legacy.Complete(start,"gameplay");after=Facts();cycles.EndPayload("gameplay",0,legacy.LastAbsoluteRead,before,after,legacy.WindowCompleted);
        Volatile.Write(ref s_escape,new byte[2048]);cycles.Edge();
        Assert.True(cycles.Sealed);Assert.Equal(1,cycles.Count);
        var cycle=cycles.CycleAt(0);var first=cycles.EdgeAt(cycle.StartEdge);var last=cycles.EdgeAt(cycle.EndEdge);
        Assert.True(last.ThreadAbsolute-first.ThreadAbsolute>=4096+8192+2048);
        var begin=cycle.PayloadBegin!.Value;var end=cycle.PayloadEnd!.Value;
        var json=legacy.Export()["frames"]![0]!;
        Assert.Equal(end.ThreadAbsolute-begin.ThreadAbsolute,json["threadAllocatedBytes"]!.GetValue<long>());
        Assert.Equal(end.ProcessAbsolute-begin.ProcessAbsolute,json["processAllocatedBytes"]!.GetValue<long>());
        Assert.True(begin.Before.ReadEndTick<=Math.Min(begin.ProcessRead.StartTick,begin.ThreadRead.StartTick));
        Assert.True(begin.After.ReadStartTick>=Math.Max(begin.ProcessRead.EndTick,begin.ThreadRead.EndTick));
        Assert.True(end.Before.ReadEndTick<=Math.Min(end.ProcessRead.StartTick,end.ThreadRead.StartTick));
        Assert.True(end.After.ReadStartTick>=Math.Max(end.ProcessRead.EndTick,end.ThreadRead.EndTick));
        int count=cycles.Count;cycles.Edge();cycles.Close(true);Assert.Equal(count,cycles.Count);
    }
    [Fact]
    public void IdleCyclesShareOneExactWitnessAndOffNeverInventsCounterTotals()
    {
        var cycles=new OwnerCycleObservations(4,false,Facts);cycles.Edge();cycles.Edge();cycles.Edge();
        Assert.Equal(2,cycles.Count);Assert.Equal(cycles.CycleAt(0).EndEdge,cycles.CycleAt(1).StartEdge);
        Assert.Equal("idle/no-payload",cycles.CycleAt(0).State);Assert.Null(cycles.CycleAt(0).PayloadIndex);
        for(int i=0;i<3;i++){Assert.Null(cycles.EdgeAt(i).ProcessAbsolute);Assert.Null(cycles.EdgeAt(i).ThreadAbsolute);}
        cycles.Close(true);Assert.Equal(3,cycles.Count);Assert.False(cycles.Sealed);
    }
    [Fact]
    public void NoEntryFaultAndOverflowCannotProduceACompleteWindowOrOverwriteStorage()
    {
        var empty=new OwnerCycleObservations(1,false,Facts);empty.Close(true);Assert.Equal(0,empty.Count);Assert.False(empty.Sealed);
        var fault=new OwnerCycleObservations(1,false,Facts);fault.Edge();fault.Close(false);Assert.Equal("fault/incomplete-cycle",fault.CycleAt(0).State);Assert.False(fault.Sealed);
        var overflow=new OwnerCycleObservations(1,false,Facts);overflow.Edge();overflow.Edge();var original=overflow.CycleAt(0);overflow.Edge();overflow.Close(true);Assert.Equal(original,overflow.CycleAt(0));Assert.Equal(1,overflow.Count);Assert.False(overflow.Sealed);
    }
    [Fact]
    public void ActualCompiledRenderLoopConditionDominatesCapturedBodyAndAllRepeats()
    {
        var method=typeof(ClientRenderHost).GetMethod("RenderLoop",BindingFlags.Instance|BindingFlags.NonPublic,binder:null,types:[typeof(IGraphicsDevice)],modifiers:null)!;
        var il=Instructions(method);var successors=Successors(il);
        var edge=Assert.Single(il,i=>Called(method,i)?.Name=="ObserveOwnerCycleEdge");
        var fromEdge=Reach(edge.Offset,successors);var toEdge=Reach(edge.Offset,Reverse(successors));
        var closures=il.Where(i=>i.Code==OpCodes.Newobj && Called(method,i)?.DeclaringType is { } t && t.Name.Contains("DisplayClass",StringComparison.Ordinal) && fromEdge.Contains(i.Offset) && toEdge.Contains(i.Offset)).ToArray();
        Assert.NotEmpty(closures);
        foreach(var closure in closures)
        {
            Assert.DoesNotContain(closure.Offset,Reach(il[0].Offset,successors,edge.Offset));
            // A repeated captured-body entry (including continue paths) must cross the edge again.
            foreach(int next in successors[closure.Offset])Assert.DoesNotContain(closure.Offset,Reach(next,successors,edge.Offset));
        }
        var close=Assert.Single(il,i=>Called(method,i)?.DeclaringType==typeof(OwnerCycleObservations) && Called(method,i)?.Name=="Close");
        var terminal=Assert.Single(method.GetMethodBody()!.ExceptionHandlingClauses,c=>c.Flags==ExceptionHandlingClauseOptions.Finally && close.Offset>=c.HandlerOffset && close.Offset<c.HandlerOffset+c.HandlerLength);
        Assert.InRange(edge.Offset,terminal.TryOffset,terminal.TryOffset+terminal.TryLength-1);
        Assert.All(closures,c=>Assert.InRange(c.Offset,terminal.TryOffset,terminal.TryOffset+terminal.TryLength-1));
    }
    [Fact]
    public void ActualCompiledOffWitnessBypassesGcAndLegacyEachReadOnlyOnce()
    {
        var method=typeof(OwnerCycleObservations).GetMethod("Witness",BindingFlags.Instance|BindingFlags.NonPublic)!;var il=Instructions(method);var graph=Successors(il);
        int field=Array.FindIndex(il,i=>i.Token is int token && i.Code==OpCodes.Ldfld && method.Module.ResolveField(token)?.Name=="_precise");Assert.True(field>=0);
        var branch=il[field+1];Assert.True(branch.Code==OpCodes.Brfalse || branch.Code==OpCodes.Brfalse_S);
        var counterCalls=il.Where(i=>Called(method,i)?.DeclaringType==typeof(GC)).Select(i=>i.Offset).ToArray();Assert.Equal(2,counterCalls.Length);
        var offReach=Reach(Assert.Single(branch.Targets),graph);Assert.All(counterCalls,c=>Assert.DoesNotContain(c,offReach));
        foreach(string name in new[]{"Begin","Complete"})
        {
            var legacy=typeof(LiveRenderTelemetry).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!;
            var calls=Instructions(legacy).Select(i=>Called(legacy,i)).Where(m=>m?.DeclaringType==typeof(GC)).ToArray();
            Assert.Single(calls,m=>m!.Name==nameof(GC.GetTotalAllocatedBytes));Assert.Single(calls,m=>m!.Name==nameof(GC.GetAllocatedBytesForCurrentThread));
        }
    }
    private sealed class Source:IGraphicsRenderTelemetry
    {
        public bool TelemetryOverflowed=>false;public GraphicsCpuFrameIntervals CpuIntervals=>default;public GraphicsResourceMemoryTelemetry ResourceMemory=>default;
        public void EnableRenderTelemetry(int capacity=8192){}public void StopRenderTelemetry(){}
        public bool TryReadGpuCompletion(out GraphicsRawGpuTiming value){value=default;return false;}
    }
    private sealed record Instruction(int Offset,int Size,OpCode Code,int? Token,int[] Targets);
    private static readonly Dictionary<short,OpCode> s_codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Select(f=>(OpCode)f.GetValue(null)!).GroupBy(c=>c.Value).ToDictionary(g=>g.Key,g=>g.First());
    private static Instruction[] Instructions(MethodInfo method)
    {
        byte[] data=method.GetMethodBody()!.GetILAsByteArray()!;var result=new List<Instruction>();int p=0;
        while(p<data.Length)
        {
            int at=p;short code=data[p++];if(code==0xfe)code=(short)(0xfe00|data[p++]);var op=s_codes[code];int? token=null;int[] targets=[];
            int n=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(data,p),_=>4};
            if(op.OperandType==OperandType.InlineMethod)token=BitConverter.ToInt32(data,p);
            if(op.OperandType==OperandType.InlineField)token=BitConverter.ToInt32(data,p);
            if(op.OperandType==OperandType.InlineBrTarget)targets=[p+n+BitConverter.ToInt32(data,p)];
            if(op.OperandType==OperandType.ShortInlineBrTarget)targets=[p+n+(sbyte)data[p]];
            if(op.OperandType==OperandType.InlineSwitch)targets=Enumerable.Range(0,BitConverter.ToInt32(data,p)).Select(i=>p+n+BitConverter.ToInt32(data,p+4+4*i)).ToArray();
            p+=n;result.Add(new(at,p-at,op,token,targets));
        }
        return result.ToArray();
    }
    private static MethodBase? Called(MethodInfo method,Instruction value)=>value.Code.OperandType==OperandType.InlineMethod && value.Token.HasValue ? method.Module.ResolveMethod(value.Token.Value):null;
    private static Dictionary<int,int[]> Successors(Instruction[] il)
    {
        var offsets=il.Select(i=>i.Offset).ToHashSet();return il.ToDictionary(i=>i.Offset,i=>(i.Code.FlowControl is FlowControl.Return or FlowControl.Throw ? [] : i.Code.FlowControl==FlowControl.Branch ? i.Targets : i.Targets.Concat(offsets.Contains(i.Offset+i.Size)?[i.Offset+i.Size]:[])).ToArray());
    }
    private static Dictionary<int,int[]> Reverse(Dictionary<int,int[]> graph)=>graph.Keys.ToDictionary(k=>k,k=>graph.Where(p=>p.Value.Contains(k)).Select(p=>p.Key).ToArray());
    private static HashSet<int> Reach(int start,Dictionary<int,int[]> graph,int? blocked=null)
    {
        var seen=new HashSet<int>();var pending=new Stack<int>();pending.Push(start);
        while(pending.TryPop(out int at))if(at!=blocked && seen.Add(at))foreach(int next in graph[at])pending.Push(next);
        return seen;
    }
}
