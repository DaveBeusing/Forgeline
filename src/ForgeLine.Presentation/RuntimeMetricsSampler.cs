using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum RuntimeSimulationState : byte
{
    Unavailable,
    Running,
    Paused,
    Stopped
}

public readonly record struct RuntimeMetricsView(
    double? FramesPerSecond,
    double? TicksPerSecond,
    RuntimeSimulationState SimulationState);

/// <summary>Observes completed work over independent monotonic 750 ms windows.</summary>
public sealed class RuntimeMetricsSampler
{
    private CounterWindow _frames;
    private CounterWindow _ticks;
    private SimulationSessionId _session;
    private RuntimeSimulationState _state;

    public RuntimeMetricsView Sample(TimeSpan now, ulong completedFrames, ulong completedTicks,
        SimulationSessionId session, RuntimeSimulationState state, bool active = true)
    {
        if (!active || session != _session)
        {
            Reset();
            _session = session;
        }
        if (!active) return default;
        if (state != _state) _ticks.Reset();
        _state = state;
        double? fps = _frames.Sample(now, completedFrames);
        double? tps = null;
        if (state == RuntimeSimulationState.Running)
            tps = _ticks.Sample(now, completedTicks);
        else
            _ticks.Reset();
        return new RuntimeMetricsView(fps, tps, state);
    }

    public void Reset()
    {
        _frames.Reset();
        _ticks.Reset();
        _session = default;
        _state = default;
    }

    private struct CounterWindow
    {
        private bool _initialized;
        private TimeSpan _start;
        private TimeSpan _last;
        private ulong _baseline;
        private ulong _previous;
        private double? _rate;

        public double? Sample(TimeSpan now, ulong count)
        {
            if (!_initialized || now < _last || now - _last > TimeSpan.FromSeconds(2) || count < _previous)
            {
                _initialized = true;
                _start = now;
                _baseline = count;
                _rate = null;
            }
            _last = now;
            _previous = count;
            TimeSpan elapsed = now - _start;
            if (elapsed >= TimeSpan.FromMilliseconds(750))
            {
                _rate = (count - _baseline) / elapsed.TotalSeconds;
                _start = now;
                _baseline = count;
            }
            return _rate;
        }

        public void Reset() => this = default;
    }
}
