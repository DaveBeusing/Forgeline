namespace ForgeLine.Client;

// A single bounded writer checkpoints startup even if an initialization phase stalls.
internal sealed class StartupCheckpoint : IDisposable
{
    private readonly StartupDiagnostics _diagnostics;
    private readonly string _path;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;
    private (int Events, StartupReadiness Application, StartupReadiness Session, int SessionId, int Dropped)? _lastState;

    internal StartupCheckpoint(StartupDiagnostics diagnostics, string path)
    {
        _diagnostics = diagnostics;
        _path = path;
        _timer = new Timer(_ => Flush(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    internal void Flush()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var state = _diagnostics.CheckpointState;
            if (_lastState == state) return;
            // A failed atomic replacement must be retried even without another event.
            if (_diagnostics.WriteReport(_path, announce: false)) _lastState = state;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_gate)
        {
            if (_disposed) return;
            _diagnostics.WriteReport(_path, announce: false);
            _disposed = true;
        }
    }
}
