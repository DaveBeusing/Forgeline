namespace ForgeLine.Presentation;

/// <summary>Owner-thread startup cancellation without changing renderer or graphics contracts.</summary>
public sealed class AssetPreparationScope : IDisposable
{
    [ThreadStatic] private static AssetPreparationScope? _current;
    private readonly AssetPreparationScope? _previous;
    private readonly Func<bool> _stopRequested;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public AssetPreparationScope(Func<bool> stopRequested)
    {
        _stopRequested = stopRequested ?? throw new ArgumentNullException(nameof(stopRequested));
        _previous = _current; _current = this;
    }
    internal static void ThrowIfStopped()
    {
        if (_current?._stopRequested() == true) throw new OperationCanceledException("Visual asset preparation was stopped by its host.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_owner != Environment.CurrentManagedThreadId || !ReferenceEquals(_current,this))
            throw new InvalidOperationException("Preparation scopes close in reverse order on their creating render owner.");
        _current = _previous; _disposed = true;
    }
}
