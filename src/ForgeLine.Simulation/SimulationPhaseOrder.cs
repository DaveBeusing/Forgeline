namespace ForgeLine.Simulation;

public static class SimulationPhaseOrder
{
    private static readonly SimulationPhase[] OrderedPhases =
    [
        SimulationPhase.InputCommands,
        SimulationPhase.OrderProcessing,
        SimulationPhase.AiDecisions,
        SimulationPhase.NavigationRequests,
        SimulationPhase.Movement,
        SimulationPhase.Sensors,
        SimulationPhase.Combat,
        SimulationPhase.DamageResolution,
        SimulationPhase.Supply,
        SimulationPhase.Logistics,
        SimulationPhase.Production,
        SimulationPhase.Economy,
        SimulationPhase.EntityLifecycle,
        SimulationPhase.SnapshotEvents,
    ];

    public static ReadOnlySpan<SimulationPhase> All => OrderedPhases;

    public static int GetIndex(SimulationPhase phase)
    {
        return phase switch
        {
            SimulationPhase.InputCommands => 0,
            SimulationPhase.OrderProcessing => 1,
            SimulationPhase.AiDecisions => 2,
            SimulationPhase.NavigationRequests => 3,
            SimulationPhase.Movement => 4,
            SimulationPhase.Sensors => 5,
            SimulationPhase.Combat => 6,
            SimulationPhase.DamageResolution => 7,
            SimulationPhase.Supply => 8,
            SimulationPhase.Logistics => 9,
            SimulationPhase.Production => 10,
            SimulationPhase.Economy => 11,
            SimulationPhase.EntityLifecycle => 12,
            SimulationPhase.SnapshotEvents => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown simulation phase."),
        };
    }
}
