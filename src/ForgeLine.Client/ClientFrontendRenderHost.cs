using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed class ClientFrontendRenderHost : IDisposable
{
    private readonly object _gate = new();
    private readonly GraphicsWindowTarget _target;
    private readonly StartupDiagnostics _startup;
    private ulong _presentedFrames;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _frameRendered = new(false);
    private readonly Thread _thread;
    private FrontendSurfaceView? _latest;
    private RuntimeAssetCatalog? _splashAssets;
    private long _publishedSequence;
    private long _renderedSequence;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private int _splashUnavailable;
    private bool _disposed;

    internal ClientFrontendRenderHost(
        in GraphicsWindowTarget target,
        StartupDiagnostics? startup = null)
    {
        _startup = startup ?? StartupDiagnostics.Disabled;
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

    internal void UseSplashAssets(RuntimeAssetCatalog assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        lock (_gate) _splashAssets = assets;
        _signal.Set();
    }

    internal bool WaitForLatestFrame(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Timeout cannot be negative.");
        }
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

    internal bool SplashUnavailable => Volatile.Read(ref _splashUnavailable) != 0;

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

    internal static bool ObservePresentation(StartupDiagnostics startup, ulong previous, ulong current,
        FrontendSurfaceKind kind, bool splashRendered)
    {
        if (current <= previous) return false;
        startup.Mark(StartupPhase.FirstPresentedFrame);
        if (kind == FrontendSurfaceKind.StudioSplash && splashRendered)
            startup.Mark(StartupPhase.StudioSplashFirstFrame);
        if (kind == FrontendSurfaceKind.MainMenu)
            startup.Mark(StartupPhase.MainMenuFirstFrame);
        return true;
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
            StudioSplashTextureRenderer? splashRenderer = null;
            try
            {
            while (Volatile.Read(ref _stopping) == 0)
            {
                _signal.WaitOne(TimeSpan.FromMilliseconds(16));
                FrontendSurfaceView? view;
                RuntimeAssetCatalog? splashAssets;
                long sequence;
                lock (_gate)
                {
                    view = _latest;
                    splashAssets = _splashAssets;
                    sequence = _publishedSequence;
                }

                if (!view.HasValue ||
                    _target.Width <= 0 ||
                    _target.Height <= 0)
                {
                    continue;
                }

                if (view.Value.Kind == FrontendSurfaceKind.StudioSplash &&
                    splashRenderer is null && splashAssets is not null)
                {
                    try
                    {
                        splashRenderer = new StudioSplashTextureRenderer(graphics, splashAssets);
                        if (!splashRenderer.HasAssets)
                            Volatile.Write(ref _splashUnavailable, 1);
                    }
                    catch (Exception error)
                    {
                        Volatile.Write(ref _splashUnavailable, 1);
                        Console.Error.WriteLine($"[studio:renderer:fallback] {error.Message}");
                    }
                }

                graphics.RenderFrame(
                    GraphicsColor.ForgeLineClear,
                    context =>
                    {
                        if (view.Value.Kind == FrontendSurfaceKind.StudioSplash &&
                            splashRenderer?.HasAssets == true)
                        {
                            splashRenderer.Render(context,
                                view.Value.SplashElapsedSeconds,
                                view.Value.SplashMasterOpacity);
                        }
                        else
                        {
                            renderer.Render(context, view.Value);
                        }
                    });
                if (_startup.Enabled && ObservePresentation(_startup, _presentedFrames,
                    graphics.PresentedFrameCount, view.Value.Kind, splashRenderer?.HasAssets == true))
                {
                    _presentedFrames = graphics.PresentedFrameCount;
                }
                Volatile.Write(
                    ref _renderedSequence,
                    sequence);
                _frameRendered.Set();
            }
            }
            finally
            {
                splashRenderer?.Dispose();
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
