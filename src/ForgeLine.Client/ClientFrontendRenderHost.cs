using System.Diagnostics;
using System.Runtime.ExceptionServices;
using ForgeLine.Graphics;
using ForgeLine.Assets;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed class ClientFrontendRenderHost : IDisposable
{
    private readonly object _gate = new();
    private readonly GraphicsWindowTarget _target;
    private readonly StartupDiagnostics _startup;
    private readonly bool _asynchronousStartup;
    private ulong _presentedFrames;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _frameRendered = new(false);
    private readonly Thread _thread;
    private FrontendSurfaceView? _latest;
    private RuntimeAssetCatalog? _menuAssets;
    private FrontendBackgroundDiagnostics _menuImageDiagnostics = new("Deferred", 0, 0, null);
    private Action<IGraphicsDevice>? _nextStage;
    private long _publishedSequence;
    private long _presentedSequence;
    private long _presentedDimensions;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _surfaceSuspended;
    private ExceptionDispatchInfo? _failure;
    private int _stopping;
    private int _splashUnavailable;
    private int _splashPresented;
    private bool _disposed;

    internal ClientFrontendRenderHost(
        in GraphicsWindowTarget target,
        StartupDiagnostics? startup = null,
        FrontendSurfaceView? initialSurface = null,
        bool asynchronousStartup = false)
    {
        _startup = startup ?? StartupDiagnostics.Disabled;
        target.Validate();
        _target = target;
        _asynchronousStartup = asynchronousStartup;
        _surfaceWidth = target.Width;
        _surfaceHeight = target.Height;
        _surfaceSuspended = target.Suspended;
        if (initialSurface.HasValue)
        {
            _latest = initialSurface;
            _publishedSequence = 1;
        }
        _thread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "ForgeLine Frontend Render"
        };
        _thread.Start();
        if (!asynchronousStartup)
        {
            _started.Wait();
            ThrowIfFaulted();
        }
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

    internal FrontendBackgroundDiagnostics MenuImageDiagnostics { get { lock (_gate) return _menuImageDiagnostics; } }

    internal void SetMenuAssets(RuntimeAssetCatalog? catalog)
    {
        lock (_gate) _menuAssets = catalog;
        _signal.Set();
    }

    internal bool HasPresentedSplashFrame => Volatile.Read(ref _splashPresented) != 0;
    internal bool HasPresentedFrame => Volatile.Read(ref _presentedSequence) > 0;
    internal Thread ExecutionThread => _thread;

    // Continue on the device's creating thread; never move GPU ownership to another thread.
    internal void ContinueWith(Action<IGraphicsDevice> nextStage)
    {
        ArgumentNullException.ThrowIfNull(nextStage);
        ThrowIfFaulted();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_nextStage is not null || Volatile.Read(ref _stopping) != 0)
                throw new InvalidOperationException("The frontend render owner cannot accept another stage.");
            _nextStage = nextStage;
        }
        _signal.Set();
    }

    internal void UpdateSurface(int width, int height, bool suspended)
    {
        lock (_gate)
        {
            _surfaceWidth = width;
            _surfaceHeight = height;
            _surfaceSuspended = suspended || width <= 0 || height <= 0;
        }
        _signal.Set();
    }
    internal bool IsSurfacePresented(int width, int height) =>
        Volatile.Read(ref _presentedDimensions) == ((long)(uint)width << 32 | (uint)height);

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
            Volatile.Read(ref _presentedSequence) >= expectedSequence)
        {
            return true;
        }

        var stopwatch = Stopwatch.StartNew();
        while (Volatile.Read(ref _presentedSequence) < expectedSequence)
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
        FrontendSurfaceKind kind, bool splashRendered, bool bootstrapRendered = false)
    {
        if (current <= previous) return false;
        startup.Mark(StartupPhase.FirstPresentedFrame);
        if (kind == FrontendSurfaceKind.StudioSplash && bootstrapRendered)
            startup.Mark(StartupPhase.SplashBootstrapFirstFrame);
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
            Action<IGraphicsDevice>? nextStage = RenderFrontend(graphics);
            if (nextStage is not null) nextStage(graphics);
        }
        catch (Exception exception)
        {
            if (_asynchronousStartup)
                _startup.End(StartupPhase.BootRenderer, StartupEventKind.Failed,
                    detail: exception.GetType().Name);
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(exception));
            _started.Set();
            _frameRendered.Set();
        }
    }

    private Action<IGraphicsDevice>? RenderFrontend(IGraphicsDevice graphics)
    {
        using var renderer =
            new FrontendOverlayRenderer(graphics);
        if (_asynchronousStartup)
        {
            _startup.End(StartupPhase.BootRenderer);
            _startup.Mark(StartupPhase.GraphicsReady);
        }
        _started.Set();
        int width = _target.Width, height = _target.Height;
        bool suspended = _target.Suspended;
        StudioSplashVideoRenderer? videoRenderer = null;
        bool videoAttempted = false;
        bool menuPresented = false;
        var feedback = new UiInteractionFeedback();
        using var uiAudio = new WaveOutUiAudio();
        try
        {
            while (Volatile.Read(ref _stopping) == 0)
            {
                _signal.WaitOne(TimeSpan.FromMilliseconds(16));
                FrontendSurfaceView? view;
                long sequence;
                RuntimeAssetCatalog? menuAssets;
                int requestedWidth, requestedHeight;
                bool requestedSuspended;
                lock (_gate)
                {
                    if (_nextStage is not null) return _nextStage;
                    view = _latest;
                    menuAssets = _menuAssets;
                    sequence = _publishedSequence;
                    requestedWidth = _surfaceWidth;
                    requestedHeight = _surfaceHeight;
                    requestedSuspended = _surfaceSuspended;
                }

                if (!view.HasValue)
                {
                    continue;
                }

                if (requestedSuspended)
                {
                    feedback.Cancel(); uiAudio.StopPlayback();
                    if (!suspended) graphics.Resize(0, 0);
                    suspended = true;
                    continue;
                }
                if (suspended || width != requestedWidth || height != requestedHeight)
                {
                    feedback.Cancel(); uiAudio.StopPlayback();
                    graphics.Resize(requestedWidth, requestedHeight);
                    width = requestedWidth;
                    height = requestedHeight;
                    suspended = false;
                }

                if (view.Value.Kind == FrontendSurfaceKind.StudioSplash && _presentedFrames > 0 && !videoAttempted)
                {
                    videoAttempted = true;
                    videoRenderer = StudioSplashVideoRenderer.TryCreate(graphics);
                    if (videoRenderer is null) Volatile.Write(ref _splashUnavailable, 1);
                }
                if (view.Value.Kind != FrontendSurfaceKind.StudioSplash)
                {
                    videoRenderer?.Dispose();
                    videoRenderer = null;
                }
                if (videoRenderer is not null)
                {
                    try
                    {
                        videoRenderer.Prepare(view.Value.SplashElapsedSeconds);
                        videoRenderer.UpdateAudio(view.Value.SplashElapsedSeconds, view.Value.SplashMasterOpacity);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
                    {
                        Console.Error.WriteLine($"[studio:video:unavailable] {error.Message}");
                        videoRenderer.Dispose();
                        videoRenderer = null;
                        Volatile.Write(ref _splashUnavailable, 1);
                    }
                }

                if (menuPresented && menuAssets is not null && view.Value.Kind != FrontendSurfaceKind.StudioSplash)
                {
                    renderer.PrepareSkin(menuAssets);
                    if(view.Value.Kind == FrontendSurfaceKind.MainMenu)
                        renderer.PrepareBackground(menuAssets, view.Value.BackgroundQuality, graphics.Diagnostics.Device.IsSoftwareAdapter);
                    lock (_gate) _menuImageDiagnostics = renderer.BackgroundDiagnostics;
                }
                TimeSpan uiNow = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                view = feedback.Frontend(view.Value, uiNow, view.Value.FeedbackActive, graphics.Diagnostics.Device.IsSoftwareAdapter);
                graphics.RenderFrame(
                    view.Value.Kind == FrontendSurfaceKind.StudioSplash
                        ? new GraphicsColor(0, 0, 0, 1) : GraphicsColor.ForgeLineClear,
                    context =>
                    {
                        context.BeginPass(GraphicsFramePass.Overlay);
                        if (view.Value.Kind == FrontendSurfaceKind.StudioSplash && videoRenderer?.HasFrame == true)
                        {
                            videoRenderer.Render(context, view.Value.SplashMasterOpacity);
                        }
                        else if (view.Value.Kind != FrontendSurfaceKind.StudioSplash)
                        {
                            renderer.Render(context, view.Value);
                        }
                        else
                        {
                            renderer.Render(context, FrontendSurfaceView.Loading("PREPARING STUDIO INTRO", false, 0));
                        }
                    });
                if (feedback.ResetAudio) uiAudio.StopPlayback();
                uiAudio.Update(feedback.Cue, view.Value.FeedbackSettings, uiNow, view.Value.FeedbackActive);
                if (ObservePresentation(_startup, _presentedFrames,
                    graphics.PresentedFrameCount, view.Value.Kind,
                    videoRenderer?.HasFrame == true,
                    view.Value.Kind == FrontendSurfaceKind.StudioSplash && videoRenderer?.HasFrame != true))
                {
                    if (view.Value.Kind == FrontendSurfaceKind.StudioSplash && videoRenderer?.HasFrame == true)
                        Volatile.Write(ref _splashPresented, 1);
                    if (view.Value.Kind == FrontendSurfaceKind.MainMenu) menuPresented = true;
                    _presentedFrames = graphics.PresentedFrameCount;
                    Volatile.Write(ref _presentedDimensions, (long)(uint)width << 32 | (uint)height);
                    Volatile.Write(ref _presentedSequence, sequence);
                    _frameRendered.Set();
                }
            }
        }
        finally
        {
            videoRenderer?.Dispose();
        }
        return null;
    }
}
