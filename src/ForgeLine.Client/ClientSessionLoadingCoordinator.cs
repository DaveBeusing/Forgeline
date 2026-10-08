using ForgeLine.Game;

namespace ForgeLine.Client;

internal enum ClientSessionLoadPhase
{
    Configuration, ScenarioAssembly, Replay, HashVerification, PresentationBinding, RendererReadiness
}

internal readonly record struct ClientSessionLoadProgress(
    ClientSessionLoadPhase Phase, ulong CompletedTicks = 0, ulong TotalTicks = 0)
{
    internal bool HasProgress => Phase == ClientSessionLoadPhase.Replay && TotalTicks > 0;
    internal float Progress => HasProgress ? (float)Math.Min(1d, (double)CompletedTicks / TotalTicks) : 0f;
}

internal enum ClientSessionLoadError
{
    Configuration, CorruptSave, IncompatibleSave, Replay, StateVerification, Presentation, Renderer
}

internal sealed class ClientSessionLoadingException : Exception
{
    internal ClientSessionLoadingException(ClientSessionLoadError category, Exception cause)
        : base(cause.Message, cause) => Category = category;
    internal ClientSessionLoadError Category { get; }

    internal static ClientSessionLoadingException From(Exception error, ClientSessionLoadPhase phase)
    {
        ClientSessionLoadError category = error is MatchPersistenceException persistence
            ? persistence.Reason switch
            {
                MatchPersistenceFailureReason.IncompatibleVersion => ClientSessionLoadError.IncompatibleSave,
                MatchPersistenceFailureReason.CorruptDocument or MatchPersistenceFailureReason.IncompleteCommandHistory
                    => ClientSessionLoadError.CorruptSave,
                MatchPersistenceFailureReason.StateMismatch or MatchPersistenceFailureReason.RandomStateMismatch
                    => ClientSessionLoadError.StateVerification,
                _ => ClientSessionLoadError.Configuration
            }
            : phase switch
            {
                ClientSessionLoadPhase.Replay => ClientSessionLoadError.Replay,
                ClientSessionLoadPhase.HashVerification => ClientSessionLoadError.StateVerification,
                ClientSessionLoadPhase.PresentationBinding => ClientSessionLoadError.Presentation,
                ClientSessionLoadPhase.RendererReadiness => ClientSessionLoadError.Renderer,
                _ => ClientSessionLoadError.Configuration
            };
        return new(category, error);
    }
}

// One dedicated construction owner. A result is either transferred once or disposed after joining.
internal sealed class ClientSessionLoadingCoordinator<TSession> : IDisposable where TSession : class, IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly Thread _worker;
    private TSession? _result;
    private ClientSessionLoadProgress _progress;
    private ClientSessionLoadingException? _failure;
    private bool _completed;
    private bool _taken;
    private bool _disposed;

    internal ClientSessionLoadingCoordinator(
        Func<CancellationToken, Action<ClientSessionLoadProgress>, TSession> create,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = new Thread(() => Execute(create)) { IsBackground = true, Name = "ForgeLine Session Loading" };
        _worker.Start();
    }

    internal ClientSessionLoadProgress Progress { get { lock (_gate) return _progress; } }
    internal bool Completed { get { lock (_gate) return _completed; } }
    internal bool CancellationRequested => _cancellation.IsCancellationRequested;
    internal bool WorkerAlive => _worker.IsAlive;
    internal ClientSessionLoadingException? Failure { get { lock (_gate) return _failure; } }
    internal void Cancel() => _cancellation.Cancel();

    internal bool TryTake(out TSession? session)
    {
        lock (_gate)
        {
            session = null;
            if (!_completed || _taken || _disposed || _failure is not null || _cancellation.IsCancellationRequested)
                return false;
            session = _result;
            _result = null;
            _taken = session is not null;
            return _taken;
        }
    }

    private void Execute(Func<CancellationToken, Action<ClientSessionLoadProgress>, TSession> create)
    {
        TSession? result = null;
        try
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            result = create(_cancellation.Token, progress => { lock (_gate) _progress = progress; }) ??
                throw new InvalidOperationException("Session construction returned no runtime.");
            _cancellation.Token.ThrowIfCancellationRequested();
            lock (_gate) { _result = result; result = null; }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate) _failure = ClientSessionLoadingException.From(error, _progress.Phase);
        }
        finally
        {
            try { result?.Dispose(); }
            catch (Exception error) { lock (_gate) _failure ??= ClientSessionLoadingException.From(error, _progress.Phase); }
            lock (_gate) _completed = true;
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        try
        {
            try { _cancellation.Cancel(); }
            finally { _worker.Join(); }
            TSession? result;
            lock (_gate) { result = _result; _result = null; }
            result?.Dispose();
        }
        finally { _cancellation.Dispose(); }
    }
}
