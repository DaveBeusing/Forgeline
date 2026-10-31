using System.Diagnostics;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;

namespace ForgeLine.Client;

internal static class OwnerObservationSettings
{
    internal const string EnvironmentName="FORGELINE_RENDER_OWNER_OBSERVATION_VERSION";
    internal static int ReadCapacity()
    {
        string? value=Environment.GetEnvironmentVariable("FORGELINE_RENDER_OWNER_CYCLE_CAPACITY");
        if(string.IsNullOrWhiteSpace(value))return 8192;
        if(!int.TryParse(value,out int capacity) || capacity is < 1 or > 65536)throw new InvalidDataException("Unsupported owner-cycle storage capacity.");
        return capacity;
    }
    internal static int ReadEnvironment()
    {
        string? value=Environment.GetEnvironmentVariable(EnvironmentName);
        if(string.IsNullOrWhiteSpace(value))return 0;
        if(!int.TryParse(value,out int version) || version is not (0 or 1))throw new InvalidDataException("Unsupported owner observation contract version.");
        return version;
    }
}

internal readonly record struct OwnerWorkloadFacts(long ReadStartTick,long ReadEndTick,
    OwnerWorkloadSnapshot Simulation,PlatformWorkloadTelemetry Platform,
    OwnerWorkloadSnapshot UiAudio,OwnerWorkloadSnapshot WorldAudio,
    bool SimulationAbsentByComposition,bool PlatformProviderAvailable,OwnerSnapshotPopulation Snapshot=default);

internal readonly record struct OwnerSnapshotPopulation(bool Available,ulong Session,ulong Tick,int SimulationEntities,int InstanceRoots,int ObservedInstanceHighWater,string? Scope=null);

internal readonly record struct OwnerAllocationWitness(long ReadStartTick,long ReadEndTick,
    long? ProcessAbsolute,long? ThreadAbsolute,GraphicsTimestampInterval ProcessRead,GraphicsTimestampInterval ThreadRead,
    OwnerWorkloadFacts Before,OwnerWorkloadFacts After);
internal readonly record struct LegacyAllocationWitness(long ProcessAbsolute,long ThreadAbsolute,
    GraphicsTimestampInterval ProcessRead,GraphicsTimestampInterval ThreadRead,
    OwnerWorkloadFacts Before,OwnerWorkloadFacts After);
internal readonly record struct OwnerCycleRecord(int Index,int StartEdge,int EndEdge,string State,int? PayloadIndex,
    int FixtureOrdinal,LegacyAllocationWitness? PayloadBegin,LegacyAllocationWitness? PayloadEnd,
    ulong SubmittedFence,ulong Generation,ulong ResourceGeneration,ulong Submission=0);

// One shared edge closes a cycle and starts the next. No body-local Begin can
// precede compiler-inserted display-class construction; the loop condition can.
internal sealed class OwnerCycleObservations
{
    private readonly bool _precise;
    private readonly Func<OwnerWorkloadFacts> _facts;
    private readonly OwnerAllocationWitness[] _edges;
    private readonly OwnerCycleRecord[] _cycles;
    private int _edgeCount,_count;
    private string _state="idle/no-payload";
    private int? _payload;
    private int _fixture=-1;
    private LegacyAllocationWitness? _begin,_end;
    private ulong _fence,_generation,_resource,_submission;
    private bool _pendingSeal,_sealed,_closed,_overflow,_faulted;
    private string? _closeFailure;
    private int? _firstMeasuredCycle,_lastMeasuredCycle;
    internal bool Sealed=>_sealed;
    internal bool PreciseEnabled=>_precise;
    internal int Count=>_count;
    internal int MeasuredCount { get {int n=0;for(int i=0;i<_count;i++)if(_cycles[i].PayloadIndex.HasValue)n++;return n;} }
    internal bool Failed=>_overflow || _faulted;
    internal OwnerAllocationWitness LastSealedEdge => _sealed && _edgeCount>0 ? _edges[_edgeCount-1] : throw new InvalidOperationException("Owner-cycle window is not sealed.");
    internal GraphicsFrameSubmission LastMeasuredSubmission { get {var row=_cycles[_lastMeasuredCycle ?? throw new InvalidOperationException("No measured cycle.")];return new(row.Submission,row.SubmittedFence,row.Generation,row.ResourceGeneration);} }
    internal long LastMeasuredEndTick { get; private set; }
    internal OwnerCycleObservations(int capacity,bool precise,Func<OwnerWorkloadFacts> facts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity,1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity,65536);
        _precise=precise;_facts=facts;_edges=new OwnerAllocationWitness[checked(capacity+1)];_cycles=new OwnerCycleRecord[capacity];
    }
    internal OwnerWorkloadFacts Facts()=>_sealed || _closed || _overflow ? default : _facts();
    private OwnerAllocationWitness Witness()
    {
        long start=Stopwatch.GetTimestamp();var before=_facts();
        long? process=null,thread=null;GraphicsTimestampInterval processRead=default,threadRead=default;
        if(_precise)
        {
            long read=Stopwatch.GetTimestamp();process=GC.GetTotalAllocatedBytes(precise:true);
            processRead=new(read,Stopwatch.GetTimestamp());
            read=Stopwatch.GetTimestamp();thread=GC.GetAllocatedBytesForCurrentThread();
            threadRead=new(read,Stopwatch.GetTimestamp());
        }
        var after=_facts();
        return new(start,Stopwatch.GetTimestamp(),process,thread,processRead,threadRead,before,after);
    }
    internal void Edge()
    {
        if(_sealed || _closed || _overflow)return;
        if(_edgeCount==0){_edges[_edgeCount++]=Witness();return;}
        if(_count==_cycles.Length){_overflow=true;return;}
        _edges[_edgeCount]=Witness();
        _cycles[_count]=new(_count,_edgeCount-1,_edgeCount,_state,_payload,_fixture,_begin,_end,_fence,_generation,_resource,_submission);
        if(_payload.HasValue){_firstMeasuredCycle??=_count;_lastMeasuredCycle=_count;}
        _count++;_edgeCount++;
        if(_pendingSeal){_sealed=true;return;}
        _state="idle/no-payload";_payload=null;_fixture=-1;_begin=_end=null;_fence=_generation=_resource=_submission=0;
    }
    internal void BeginPayload(in LiveTelemetryStart value,in OwnerWorkloadFacts before,in OwnerWorkloadFacts after)
    {
        if(_sealed || _closed || _overflow)return;
        if(value.Tick!=0)_begin=new(value.ProcessBytes,value.ThreadBytes,value.BeginProcessCounter,value.BeginThreadCounter,before,after);
        _state="payload-incomplete";
    }
    internal void EndPayload(string state,int? payloadIndex,in LiveTelemetryCounterEnd? value,
        in OwnerWorkloadFacts before,in OwnerWorkloadFacts after,bool windowCompleted)
    {
        if(_sealed || _closed || _overflow)return;
        _state=state;_payload=payloadIndex;
        if(value is { } end){_end=new(end.ProcessAbsolute,end.ThreadAbsolute,end.ProcessRead,end.ThreadRead,before,after);LastMeasuredEndTick=end.PayloadEndTick;}
        _pendingSeal|=windowCompleted;
    }
    internal void FixtureFrame(int ordinal,int warmups,int samples,in GraphicsFrameSubmission submission)
    {
        if(_sealed || _closed || _overflow)return;
        _fixture=ordinal;_state="gameplay";if(!_precise)LastMeasuredEndTick=Stopwatch.GetTimestamp();_submission=submission.Submission;_fence=submission.SubmittedFence;_generation=submission.Generation;_resource=submission.ResourceGeneration;
        if(ordinal>=warmups && ordinal<warmups+samples)_payload=ordinal-warmups;
        _pendingSeal|=ordinal==warmups+samples-1;
    }
    internal void CommonProbeEnd(long tick){if(!_precise && !_sealed && _payload.HasValue)LastMeasuredEndTick=tick;}
    internal void Submission(in LiveTelemetryFrame frame)
    {
        if(_sealed || _closed || _overflow)return;
        if(_payload!=frame.Index){_faulted=true;return;}
        _submission=frame.Submission;_fence=frame.SubmittedFence;_generation=frame.Generation;_resource=frame.ResourceGeneration;
    }
    internal void Close(bool normal)
    {
        if(_closed)return;
        try
        {
            if(!_sealed && !_overflow && _edgeCount>0)
            {
                if(!normal)_state="fault/incomplete-cycle";
                Edge();
            }
            if(!normal || !_sealed)_faulted=true;
        }
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
        { _faulted=true;_closeFailure=error.GetType().Name; }
        finally{_closed=true;}
    }
    internal object Export()
    {
        var edges=_edges.AsSpan(0,_edgeCount).ToArray();
        var cycles=_cycles.AsSpan(0,_count).ToArray();
        return new { schemaVersion=1,kind="render-owner-cycle-observations",preciseCountersAvailable=_precise,
            capacity=_cycles.Length,overflowed=_overflow,faulted=_faulted,windowSealed=_sealed,terminalClosed=_closed,
            closeFailure=_closeFailure,firstMeasuredCycle=_firstMeasuredCycle,lastMeasuredCycle=_lastMeasuredCycle,edges,cycles,
            scope="Uncaptured shared-edge owner cycles include idle/mailbox wait and ACK/backedge tail; measured seal precedes readbacks; lifecycle close precedes final GPU idle/teardown",
            allocationSemantics="Cycle totals overlap legacy payload totals; never add them. Process/thread reads are separately bracketed, not simultaneous. Missing/Off values remain null." };
    }
    internal OwnerAllocationWitness EdgeAt(int index)=>_edges[index];
    internal OwnerCycleRecord CycleAt(int index)=>_cycles[index];
}
internal readonly record struct LiveTelemetryCounterEnd(long ProcessAbsolute,long ThreadAbsolute,
    GraphicsTimestampInterval ProcessRead,GraphicsTimestampInterval ThreadRead,long PayloadEndTick=0);
