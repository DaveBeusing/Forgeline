namespace ForgeLine.Presentation;

/// <summary>Receives evaluated layers without exposing renderer-specific APIs.</summary>
public interface ISplashRenderer
{
    void BeginSplash();
    void DrawLayer(SplashLayerDefinition layer, in SplashLayerState state, float masterOpacity);
    void EndSplash();
}

public readonly record struct SplashInputState(bool SkipPressed);

/// <summary>Presentation-time state only; never reads or modifies simulation time.</summary>
public sealed class SplashScreenController
{
    private static readonly TimeSpan SkipFadeDuration = TimeSpan.FromSeconds(0.3);
    private readonly SplashDefinition _definition;
    private readonly SplashLayerDefinition[] _orderedLayers;
    private TimeSpan _elapsed;
    private TimeSpan _fadeElapsed;
    private bool _fading;
    private bool _started;

    public SplashScreenController(SplashDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        _definition = definition;
        _orderedLayers = definition.Layers.OrderBy(layer => layer.SortOrder)
            .Select(layer => layer with { Keyframes = layer.Keyframes.ToArray() }).ToArray();
    }

    public bool IsComplete { get; private set; }
    public bool WasSkipped { get; private set; }
    public TimeSpan Elapsed => _elapsed;
    public float MasterOpacity => _fading
        ? Math.Clamp(1 - (float)(_fadeElapsed.TotalSeconds / SkipFadeDuration.TotalSeconds), 0f, 1f)
        : 1f;

    public void Start()
    {
        _elapsed = TimeSpan.Zero;
        _fadeElapsed = TimeSpan.Zero;
        _fading = false;
        _started = true;
        IsComplete = false;
        WasSkipped = false;
    }

    public void Update(TimeSpan deltaTime, in SplashInputState input)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deltaTime, TimeSpan.Zero);

        if (!_started || IsComplete)
        {
            return;
        }

        _elapsed += deltaTime;
        bool startedFadeThisFrame = false;

        if (!_fading && input.SkipPressed && _definition.AllowSkip &&
            _elapsed >= _definition.MinimumVisibleDuration)
        {
            WasSkipped = true;
            _fading = true;
            _fadeElapsed = TimeSpan.Zero;
            startedFadeThisFrame = true;
        }

        if (_fading)
        {
            if (!startedFadeThisFrame)
            {
                _fadeElapsed += deltaTime;
            }
            IsComplete = _fadeElapsed >= SkipFadeDuration;
        }
        else
        {
            IsComplete = _elapsed >= _definition.Duration;
        }
    }

    public void Render(ISplashRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!_started || IsComplete)
        {
            return;
        }

        float elapsedSeconds = (float)Math.Min(_elapsed.TotalSeconds, _definition.Duration.TotalSeconds);
        float masterOpacity = MasterOpacity;

        renderer.BeginSplash();
        try
        {
            foreach (SplashLayerDefinition layer in _orderedLayers)
            {
                SplashKeyframe[] frames = layer.Keyframes as SplashKeyframe[] ??
                    throw new InvalidOperationException("Splash keyframes must be precompiled to arrays.");
                SplashLayerState state = SplashTimelineEvaluator.Evaluate(frames, elapsedSeconds);
                if (state.Opacity > 0 && masterOpacity > 0)
                {
                    renderer.DrawLayer(layer, in state, masterOpacity);
                }
            }
        }
        finally
        {
            renderer.EndSplash();
        }
    }

    public void Stop()
    {
        IsComplete = true;
        _started = false;
    }
}
