using ForgeLine.Ecs;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation;

public sealed record SimulationDiagnosticsSnapshot(
    bool Enabled,
    int ConfiguredTicksPerSecond,
    ulong CompletedTicks,
    TimeSpan LastTickDuration,
    TimeSpan AverageTickDuration,
    TimeSpan MaxTickDuration,
    long ObservedAllocatedBytes,
    int ObservedGen0Collections,
    int ObservedGen1Collections,
    int ObservedGen2Collections,
    EntityRegistryDiagnostics Entities,
    ComponentCount[] Components,
    JobSchedulerMetrics? Jobs,
    ManagedRuntimeMetrics Runtime);
