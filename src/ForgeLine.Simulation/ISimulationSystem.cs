namespace ForgeLine.Simulation;

public interface ISimulationSystem
{
    SimulationPhase Phase { get; }

    void Execute(SimulationContext context);
}
