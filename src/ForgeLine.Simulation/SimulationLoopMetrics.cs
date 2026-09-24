namespace ForgeLine.Simulation;

public readonly record struct SimulationLoopMetrics(
    ulong CompletedTicks,
    ulong CommandsProcessed,
    ulong SystemInvocations,
    int PendingCommands,
    int PeakPendingCommands);
