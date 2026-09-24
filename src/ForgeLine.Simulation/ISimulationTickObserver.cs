namespace ForgeLine.Simulation;

public interface ISimulationTickObserver
{
    void OnTickCompleted(SimulationContext context);
}
