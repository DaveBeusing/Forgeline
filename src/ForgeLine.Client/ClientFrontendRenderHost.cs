using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed class ClientFrontendRenderHost : IDisposable
{
    private readonly object _gate = new();
    private readonly GraphicsWindowTarget _target;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _frameRendered = new(false);
    private readonly Thread _thread;
    private FrontendSurfaceView? _latest;
    private long _publishedSequence;
    private long _renderedSequence;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private bool _disposed;

    internal ClientFrontendRenderHost(
        in GraphicsWindowTarget target)
    {
        target.Validate();
        _target = target;
        _thread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "ForgeLine Frontend Render"
        };
        _thread.Start();
        _started.Wait();
        ThrowIfFaulted();
    }

    internal void Publish(
        in FrontendSurfaceView view)
    {
        ThrowIfFaulted();
        lock (_gate)
        {
            _latest = view;
            _publishedSequence++;
            _frameRendered.Reset();
        }

        _signal.Set();
    }

    internal bool WaitForLatestFrame(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        ThrowIfFaulted();

        long expectedSequence;
        lock (_gate)
        {
            expectedSequence = _publishedSequence;
        }

        if (expectedSequence == 0 ||
            Volatile.Read(ref _renderedSequence) >= expectedSequence)
        {
            return true;
        }

        var stopwatch = Stopwatch.StartNew();
        while (Volatile.Read(ref _renderedSequence) < expectedSequence)
        {
            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero ||
                !_frameRendered.Wait(remaining))
            {
                ThrowIfFaulted();
                return false;
            }

            _frameRendered.Reset();
            ThrowIfFaulted();
        }

        return true;
    }

    internal void ThrowIfFaulted() =>
        Volatile.Read(ref _failure)?.Throw();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _stopping, 1);
        _signal.Set();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }

        _signal.Dispose();
        _started.Dispose();
        _frameRendered.Dispose();
        _disposed = true;
    }

    private void RenderLoop()
    {
        try
        {
            using IGraphicsDevice graphics =
                GraphicsDeviceFactory.CreateForWindowTarget(
                    _target);
            using var renderer =
                new FrontendOverlayRenderer(graphics);
            _started.Set();

            while (Volatile.Read(ref _stopping) == 0)
            {
                _signal.WaitOne(TimeSpan.FromMilliseconds(16));
                FrontendSurfaceView? view;
                long sequence;
                lock (_gate)
                {
                    view = _latest;
                    sequence = _publishedSequence;
                }

                if (!view.HasValue ||
                    _target.Width <= 0 ||
                    _target.Height <= 0)
                {
                    continue;
                }

                graphics.RenderFrame(
                    GraphicsColor.ForgeLineClear,
                    context =>
                        renderer.Render(
                            context,
                            view.Value));
                Volatile.Write(
                    ref _renderedSequence,
                    sequence);
                _frameRendered.Set();
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(
                ref _failure,
                ExceptionDispatchInfo.Capture(exception));
            _started.Set();
        }
    }
}
