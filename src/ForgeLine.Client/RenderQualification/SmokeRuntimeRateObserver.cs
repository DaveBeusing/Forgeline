using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client.RenderQualification;

// The platform samples published counters even while a slow native frame is in flight.
internal sealed class SmokeRuntimeRateObserver
{
    private readonly RuntimeMetricsSampler _sampler = new();
    private SimulationSessionId _session;
    private TimeSpan _previousTime;
    private ulong _frames, _ticks;
    private bool _observed;
    internal RuntimeMetricsView? LastRunningRates { get; private set; }

    internal void Sample(TimeSpan now, ulong presentedFrames, ClientSimulationTelemetry simulation, bool active)
    {
        if (!active || _observed && (simulation.SessionId != _session || now < _previousTime ||
            presentedFrames < _frames || simulation.CompletedTicks < _ticks))
        {
            _sampler.Reset();
            LastRunningRates = null;
        }
        _observed = true; _session = simulation.SessionId; _previousTime = now;
        _frames = presentedFrames; _ticks = simulation.CompletedTicks;
        RuntimeMetricsView measured = _sampler.Sample(now, presentedFrames, simulation.CompletedTicks,
            simulation.SessionId, simulation.State, active);
        if (measured.SimulationState == RuntimeSimulationState.Running &&
            measured.FramesPerSecond is > 0 && measured.TicksPerSecond is > 0)
            LastRunningRates = measured;
    }
}
