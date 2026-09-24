namespace ForgeLine.Presentation;

public readonly record struct FrameTimingMetrics(
    double FramesPerSecond,
    double FrameMilliseconds,
    double CpuRenderMilliseconds);

public sealed class FrameTimingTracker
{
    private const double SmoothingFactor = 0.1;

    private bool _initialized;
    private double _smoothedFrameSeconds;
    private double _smoothedCpuRenderSeconds;

    public FrameTimingMetrics Record(
        TimeSpan frameDuration,
        TimeSpan cpuRenderDuration)
    {
        double frameSeconds =
            Math.Max(frameDuration.TotalSeconds, 0.000_001);
        double renderSeconds =
            Math.Max(cpuRenderDuration.TotalSeconds, 0.0);

        if (!_initialized)
        {
            _smoothedFrameSeconds = frameSeconds;
            _smoothedCpuRenderSeconds = renderSeconds;
            _initialized = true;
        }
        else
        {
            _smoothedFrameSeconds +=
                (frameSeconds - _smoothedFrameSeconds) *
                SmoothingFactor;
            _smoothedCpuRenderSeconds +=
                (renderSeconds - _smoothedCpuRenderSeconds) *
                SmoothingFactor;
        }

        return new FrameTimingMetrics(
            1.0 / _smoothedFrameSeconds,
            _smoothedFrameSeconds * 1000.0,
            _smoothedCpuRenderSeconds * 1000.0);
    }
}
