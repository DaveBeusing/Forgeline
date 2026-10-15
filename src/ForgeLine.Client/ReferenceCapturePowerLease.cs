using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ForgeLine.Client;

/// <summary>Thread-owned execution requirement for explicit, unattended native qualification only.</summary>
internal sealed class ReferenceCapturePowerLease : IDisposable
{
    internal const uint Continuous = 0x80000000;
    internal const uint Required = Continuous | 1 | 2;
    private readonly Func<uint, uint> _setState;
    private readonly uint _previous;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal ReferenceCapturePowerLease(Func<uint, uint>? setState = null)
    {
        _setState = setState ?? SetThreadExecutionState;
        _previous = _setState(Required);
        if (_previous == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot acquire the native capture display/system requirement.");
    }

    internal bool ReleaseSucceeded { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Capture execution requirements must be restored on their acquiring thread.");
        _disposed = true;
        ReleaseSucceeded = _setState(_previous | Continuous) != 0;
        GC.SuppressFinalize(this);
        if (!ReleaseSucceeded)
            Console.Error.WriteLine("[reference:power] Requirement restore failed; ending the capture thread releases its request.");
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);
}
