namespace ForgeLine.Simulation;

public readonly record struct SimulationCommandEnvelope(
    SimulationTick TargetTick,
    ulong Sequence,
    SimulationCommandSource Source,
    ISimulationCommand Command)
{
    public Type CommandType => Command.GetType();
}
