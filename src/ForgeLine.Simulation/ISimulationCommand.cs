namespace ForgeLine.Simulation;

public interface ISimulationCommand
{
    void Execute(SimulationContext context);
}
