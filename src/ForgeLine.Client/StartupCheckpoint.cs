namespace ForgeLine.Client;

// A single bounded writer checkpoints startup even if an initialization phase stalls.
internal sealed class StartupCheckpoint : IDisposable
{
    private readonly StartupDiagnostics _diagnostics;
    private readonly string _path;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;
    private int _lastCount = -1;

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
            int count = _diagnostics.Snapshot().Length;
            if (_lastCount == count) return;
            _diagnostics.WriteReport(_path, announce: false);
            _lastCount = count;
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
