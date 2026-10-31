using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ForgeLine.Platform.Windows;

/// <summary>Copied optional Windows-owner facts. Counts work, never grants a byte budget or visibility.</summary>
public readonly record struct PlatformWorkloadTelemetry(int SchemaVersion, Guid Identity, uint NativeOwnerThread,
    long NativeWindowHandle,long ReadStartTick,long ReadEndTick,long PumpStarted,long PumpCompleted,
    long CreationDrains,long RemovedMessages,long QuitMessages,long DispatchStarted,long DispatchCompleted,
    long WindowEnqueued,long WindowDequeued,long InputEnqueued,long InputDequeued,long InputDiscarded,
    int WindowDepth,int InputDepth,int WindowHighWater,int InputHighWater,bool Overflowed,bool Faulted,
    int InitialWindowDepth,int InitialInputDepth)
{
    public bool Available => SchemaVersion==1 && Identity!=Guid.Empty && NativeOwnerThread!=0 && NativeWindowHandle!=0 && !Overflowed && !Faulted;
}

internal sealed class PlatformWorkloadProbe
{
    private readonly object _gate=new();
    private readonly Guid _identity=Guid.NewGuid();
    private readonly uint _owner=GetCurrentThreadId();
    private long _window,_pumpStarted,_pumpCompleted,_creation,_removed,_quit,_dispatchStarted,_dispatchCompleted,
        _windowEnqueued,_windowDequeued,_inputEnqueued,_inputDequeued,_inputDiscarded;
    private int _windowDepth,_inputDepth,_windowHigh,_inputHigh,_initialWindow,_initialInput;
    private bool _overflow,_faulted;
    private void Increment(ref long value){if(value==long.MaxValue)_overflow=true;else value++;}
    internal void BindWindow(long handle,int windowDepth,int inputDepth)
    {
        lock(_gate)
        {
            if(_window!=0 && _window!=handle){_faulted=true;return;}
            if(_window==0){_initialWindow=windowDepth;_initialInput=inputDepth;}
            _window=handle;_windowDepth=windowDepth;_inputDepth=inputDepth;
            _windowHigh=Math.Max(_windowHigh,windowDepth);_inputHigh=Math.Max(_inputHigh,inputDepth);
        }
    }
    internal void BeginPump(bool creation=false){lock(_gate){Increment(ref _pumpStarted);if(creation)Increment(ref _creation);}}
    internal void EndPump(bool success){lock(_gate){Increment(ref _pumpCompleted);if(!success)_faulted=true;}}
    internal void Removed(bool quit){lock(_gate){Increment(ref _removed);if(quit)Increment(ref _quit);}}
    internal void BeginDispatch(){lock(_gate)Increment(ref _dispatchStarted);}
    internal void EndDispatch(bool success){lock(_gate){Increment(ref _dispatchCompleted);if(!success)_faulted=true;}}
    internal void Enqueued(bool input,int depth)
    {
        lock(_gate)
        {
            if(input){Increment(ref _inputEnqueued);_inputDepth=depth;_inputHigh=Math.Max(_inputHigh,depth);}
            else {Increment(ref _windowEnqueued);_windowDepth=depth;_windowHigh=Math.Max(_windowHigh,depth);}
        }
    }
    internal void Dequeued(bool input,int depth)
    {
        lock(_gate)
        {
            if(input){Increment(ref _inputDequeued);_inputDepth=depth;}
            else {Increment(ref _windowDequeued);_windowDepth=depth;}
        }
    }
    internal void DiscardInput(int count){lock(_gate){if(count<0 || long.MaxValue-_inputDiscarded<count)_overflow=true;else _inputDiscarded+=count;_inputDepth=0;}}
    internal PlatformWorkloadTelemetry Capture()
    {
        long start=Stopwatch.GetTimestamp();
        lock(_gate)return new(1,_identity,_owner,_window,start,Stopwatch.GetTimestamp(),_pumpStarted,_pumpCompleted,_creation,_removed,_quit,_dispatchStarted,_dispatchCompleted,_windowEnqueued,_windowDequeued,_inputEnqueued,_inputDequeued,_inputDiscarded,_windowDepth,_inputDepth,_windowHigh,_inputHigh,_overflow,_faulted,_initialWindow,_initialInput);
    }
    [DllImport("kernel32.dll",ExactSpelling=true)]private static extern uint GetCurrentThreadId();
}
