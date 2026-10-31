using System.Diagnostics;

namespace ForgeLine.Client;

// Fixed eight-slot vectors are copied values. Slot meanings are versioned by each
// owner; no per-observation arrays, delegates, serialization or live ECS reads.
internal readonly record struct WorkloadCounterVector(long Slot0, long Slot1, long Slot2, long Slot3,
    long Slot4, long Slot5, long Slot6, long Slot7)
{
    internal WorkloadCounterVector(ReadOnlySpan<long> values) : this(values[0],values[1],values[2],values[3],values[4],values[5],values[6],values[7]) { }
    internal long At(int slot) => slot switch { 0=>Slot0,1=>Slot1,2=>Slot2,3=>Slot3,4=>Slot4,5=>Slot5,6=>Slot6,7=>Slot7,_=>throw new ArgumentOutOfRangeException(nameof(slot)) };
}
internal readonly record struct OwnerWorkloadSnapshot(int SchemaVersion, Guid Identity, ulong Session,
    uint NativeOwnerThread, long ReadStartTick, long ReadEndTick, WorkloadCounterVector Started,
    WorkloadCounterVector Completed, WorkloadCounterVector InFlight, WorkloadCounterVector Events,
    int Queued, int InFlightMessages, int CompletionDepth, int HighWater, int Capacity,
    bool Overflowed, bool Faulted, bool CompositionSupported = true,
    WorkloadCounterVector TransportStarted = default, WorkloadCounterVector TransportCompleted = default)
{
    internal bool Available
    {
        get
        {
            if(SchemaVersion!=1 || Identity==Guid.Empty || NativeOwnerThread==0 || !CompositionSupported || Overflowed || Faulted || ReadStartTick<0 || ReadEndTick<ReadStartTick)return false;
            for(int i=0;i<8;i++)
            {
                long started=Started.At(i),completed=Completed.At(i),flight=InFlight.At(i);
                if(started<0 || completed<0 || completed>started || Events.At(i)<0 || TransportStarted.At(i)<0 || TransportCompleted.At(i)<0 || TransportCompleted.At(i)>TransportStarted.At(i) ||
                    (started==completed ? flight!=0 : started-completed!=1 || flight!=started))return false;
            }
            return Queued>=0 && InFlightMessages>=0 && CompletionDepth>=0 && HighWater>=(long)Queued+InFlightMessages+CompletionDepth;
        }
    }
    internal static long Touching(in OwnerWorkloadSnapshot before, in OwnerWorkloadSnapshot after, int operation)
    {
        if (!before.Available || !after.Available || before.Identity!=after.Identity || before.Session!=after.Session || before.NativeOwnerThread!=after.NativeOwnerThread ||
            before.ReadEndTick>after.ReadStartTick || after.Started.At(operation)<before.Completed.At(operation))
            throw new InvalidDataException("Owner workload observations do not form a coherent identity/counter envelope.");
        for(int i=0;i<8;i++)
            if(after.Started.At(i)<before.Started.At(i) || after.Completed.At(i)<before.Completed.At(i) || after.Events.At(i)<before.Events.At(i) || after.TransportStarted.At(i)<before.TransportStarted.At(i) || after.TransportCompleted.At(i)<before.TransportCompleted.At(i))
                throw new InvalidDataException("Owner workload counters regressed between observations.");
        return after.Started.At(operation)-before.Completed.At(operation);
    }
    internal static long TouchingTransport(in OwnerWorkloadSnapshot before,in OwnerWorkloadSnapshot after,int operation)
    {
        _=Touching(before,after,0);
        return after.TransportStarted.At(operation)-before.TransportCompleted.At(operation);
    }
}

internal sealed class OwnerWorkloadProbe(ulong session, int capacity = 0, bool compositionSupported = true)
{
    private readonly object _gate = new();
    private readonly Guid _identity = Guid.NewGuid();
    private readonly long[] _started=new long[8], _completed=new long[8], _inFlight=new long[8], _events=new long[8], _transportStarted=new long[8], _transportCompleted=new long[8];
    private uint _owner;
    private bool _overflow, _faulted;
    private int _queued, _messages, _completions, _highWater;
    internal void BindOwner()
    {
        uint current=RenderOwnerThreadIdentity.GetCurrentThreadId();
        lock(_gate) { if(_owner!=0 && _owner!=current)_faulted=true;else _owner=current; }
    }
    private void Increment(ref long value) { if(value==long.MaxValue)_overflow=true;else value++; }
    internal long Begin(int operation)
    {
        lock (_gate)
        {
            if (_inFlight[operation]!=0) _faulted=true;
            Increment(ref _started[operation]);
            _inFlight[operation]=_started[operation];return _inFlight[operation];
        }
    }
    internal void End(int operation,long identity,bool success=true,int? eventKind=null)
    {
        lock (_gate)
        {
            if(identity==0 || _inFlight[operation]!=identity)_faulted=true;
            _inFlight[operation]=0;Increment(ref _completed[operation]);if(eventKind.HasValue)Increment(ref _events[eventKind.Value]);if(!success)_faulted=true;
        }
    }
    // These spans can overlap across callers. They retain cumulative counts,
    // not a single-owner ordinal, and are not attributed to NativeOwnerThread.
    internal void BeginTransport(int operation,int? eventKind=null)
    { lock(_gate){Increment(ref _transportStarted[operation]);if(eventKind.HasValue)Increment(ref _events[eventKind.Value]);} }
    internal void EndTransport(int operation,bool success=true)
    { lock(_gate){Increment(ref _transportCompleted[operation]);if(!success)_faulted=true;} }
    internal void Event(int kind) { lock(_gate)Increment(ref _events[kind]); }
    internal void QueueState(int queued,int inFlight,int completions,int? eventKind=null)
    {
        lock(_gate)
        {
            if(eventKind.HasValue)Increment(ref _events[eventKind.Value]);
            if(queued<0 || inFlight<0 || completions<0){_faulted=true;return;}
            _queued=queued;_messages=inFlight;_completions=completions;
            long total=(long)queued+inFlight+completions;
            if(total>int.MaxValue)_overflow=true;else _highWater=Math.Max(_highWater,(int)total);
        }
    }
    internal void Fault() { lock(_gate)_faulted=true; }
    internal OwnerWorkloadSnapshot Capture()
    {
        long start=Stopwatch.GetTimestamp();
        lock(_gate)return new(1,_identity,session,_owner,start,Stopwatch.GetTimestamp(),new(_started),new(_completed),new(_inFlight),new(_events),_queued,_messages,_completions,_highWater,capacity,_overflow,_faulted,compositionSupported,new(_transportStarted),new(_transportCompleted));
    }
}

// Simulation operation slots: tick, control, drain, submission, pause/resume,
// terminal acknowledgement, unpaced smoke completion, control-status publication.
internal enum SimulationWorkloadOperation { Tick, Control, Drain, Submission, PauseResume, Terminal, Smoke, Bookkeeping }
// Transport events: attempted, admitted, rejected, drained, completion publication,
// completion dequeue; remaining slots reserved and not interpreted as byte bounds.
internal enum SimulationWorkloadEvent { Attempted, Admitted, Rejected, Drained, CompletionPublished, CompletionDequeued }
