using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace ForgeLine.Client;

internal readonly record struct ClientStartupResult<TAssets, TFrontend>(TAssets Assets, TFrontend Frontend);

// Two independent CPU/I/O products; window, renderer and simulation state stay on their owners.
internal sealed class ClientStartupCoordinator<TAssets, TFrontend> : IDisposable
    where TAssets : class
    where TFrontend : class
{
    private readonly CancellationTokenSource _cancellation;
    private readonly Task<TAssets> _assets;
    private readonly Task<TFrontend> _frontend;
    private readonly Task _completion;
    private readonly object _disposeGate = new();
    private bool _splashComplete;
    private bool _disposed;
    private ExceptionDispatchInfo? _failure;

    internal ClientStartupCoordinator(
        Func<CancellationToken, TAssets> loadAssets,
        Func<CancellationToken, TFrontend> prepareFrontend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loadAssets);
        ArgumentNullException.ThrowIfNull(prepareFrontend);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _assets = Task.Run(() => Execute(loadAssets));
        _frontend = Task.Run(() => Execute(prepareFrontend));
        _completion = Task.WhenAll(_assets, _frontend);
    }

    internal bool DependenciesReady
    {
        get
        {
            ThrowIfFaulted();
            return _assets.IsCompletedSuccessfully && _frontend.IsCompletedSuccessfully;
        }
    }

    internal bool SplashCompletedOrSkipped => _splashComplete;
    internal bool CanEnterFrontend => DependenciesReady && _splashComplete;
    internal bool WorkersCompleted => _completion.IsCompleted;

    internal void CompleteSplash() => _splashComplete = true;
    internal void Cancel() => _cancellation.Cancel();

    internal bool TryGetAssets([NotNullWhen(true)] out TAssets? assets)
    {
        ThrowIfFaulted();
        assets = _assets.IsCompletedSuccessfully ? _assets.GetAwaiter().GetResult() : null;
        return assets is not null;
    }

    internal ClientStartupResult<TAssets, TFrontend> GetResults()
    {
        if (!CanEnterFrontend)
            throw new InvalidOperationException("Frontend dependencies and intro completion are required.");
        return new(_assets.GetAwaiter().GetResult(), _frontend.GetAwaiter().GetResult());
    }

    internal void ThrowIfFaulted()
    {
        Volatile.Read(ref _failure)?.Throw();
        // Prefer the original worker failure over sibling cancellation.
        if (_assets.IsFaulted) _ = _assets.GetAwaiter().GetResult();
        if (_frontend.IsFaulted) _ = _frontend.GetAwaiter().GetResult();
        _cancellation.Token.ThrowIfCancellationRequested();
    }

    private T Execute<T>(Func<CancellationToken, T> operation) where T : class
    {
        try
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            T result = operation(_cancellation.Token) ??
                throw new InvalidOperationException("Startup work returned no result.");
            _cancellation.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(error), null);
            _cancellation.Cancel();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (_disposed) return;
            try
            {
                _cancellation.Cancel();
                try { _completion.GetAwaiter().GetResult(); }
                catch (Exception) when (_completion.IsCompleted)
                {
                    // Both tasks are joined and their exceptions observed. The pump exposes failures.
                }
            }
            finally
            {
                _cancellation.Dispose();
                _disposed = true;
            }
        }
    }
}
