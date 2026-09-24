using System.Diagnostics;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation;

public sealed class SimulationDiagnostics
{
    private readonly bool _enabled;
    private readonly long _startingAllocatedBytes;
    private readonly int _startingGen0Collections;
    private readonly int _startingGen1Collections;
    private readonly int _startingGen2Collections;

    private long _observedTicks;
    private long _lastTickStopwatchTicks;
    private long _totalTickStopwatchTicks;
    private long _maxTickStopwatchTicks;

    internal SimulationDiagnostics(SimulationDiagnosticsOptions? options)
    {
        _enabled = options?.Enabled == true;

        if (_enabled)
        {
            _startingAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            _startingGen0Collections = GC.CollectionCount(0);
            _startingGen1Collections = GC.CollectionCount(1);
            _startingGen2Collections = GC.CollectionCount(2);
        }
    }

    public bool Enabled => _enabled;

    internal TickMeasurement BeginTick()
    {
        if (!_enabled)
        {
            return default;
        }

        return new TickMeasurement(
            Stopwatch.GetTimestamp(),
            GC.GetTotalAllocatedBytes(precise: false));
    }

    internal void EndTick(TickMeasurement measurement)
    {
        if (!_enabled)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - measurement.StartTimestamp;
        _lastTickStopwatchTicks = elapsed;
        _totalTickStopwatchTicks += elapsed;
        _observedTicks++;

        if (elapsed > _maxTickStopwatchTicks)
        {
            _maxTickStopwatchTicks = elapsed;
        }
    }

    public SimulationDiagnosticsSnapshot Capture(SimulationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        long allocatedBytes = _enabled
            ? Math.Max(
                0,
                GC.GetTotalAllocatedBytes(precise: false) - _startingAllocatedBytes)
            : 0;

        JobSchedulerMetrics? jobMetrics = coordinator.Jobs.TryGetMetrics(
            out JobSchedulerMetrics metrics)
            ? metrics
            : null;

        return new SimulationDiagnosticsSnapshot(
            _enabled,
            coordinator.Clock.TicksPerSecond,
            coordinator.CurrentTick.Value,
            ToTimeSpan(_lastTickStopwatchTicks),
            _observedTicks == 0
                ? TimeSpan.Zero
                : ToTimeSpan(_totalTickStopwatchTicks / _observedTicks),
            ToTimeSpan(_maxTickStopwatchTicks),
            allocatedBytes,
            _enabled ? GC.CollectionCount(0) - _startingGen0Collections : 0,
            _enabled ? GC.CollectionCount(1) - _startingGen1Collections : 0,
            _enabled ? GC.CollectionCount(2) - _startingGen2Collections : 0,
            coordinator.Entities.Diagnostics,
            coordinator.Entities.GetComponentCounts(),
            jobMetrics,
            ManagedRuntimeMetrics.Capture());
    }

    private static TimeSpan ToTimeSpan(long stopwatchTicks)
    {
        return stopwatchTicks == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);
    }

    internal readonly record struct TickMeasurement(
        long StartTimestamp,
        long StartingAllocatedBytes);
}
