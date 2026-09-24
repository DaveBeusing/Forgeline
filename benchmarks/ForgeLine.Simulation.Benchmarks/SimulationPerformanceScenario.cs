using ForgeLine.Core;

namespace ForgeLine.Simulation.Benchmarks;

internal sealed class SimulationPerformanceScenario
{
    private SimulationPerformanceScenario(SimulationCoordinator simulation)
    {
        Simulation = simulation;
    }

    public SimulationCoordinator Simulation { get; }

    public static SimulationPerformanceScenario CreateLightweightEntities(
        int entityCount,
        int systemCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(systemCount);

        var simulation = new SimulationCoordinator(
            initialEntityCapacity: Math.Max(256, entityCount));

        for (int index = 0; index < entityCount; index++)
        {
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new Position(index, -index));
        }

        for (int index = 0; index < systemCount; index++)
        {
            simulation.RegisterSystem(new IterationSystem());
        }

        return new SimulationPerformanceScenario(simulation);
    }

    private sealed class IterationSystem : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.Movement;

        public void Execute(SimulationContext context)
        {
            int checksum = 0;

            foreach (EntityId entity in context.Entities.Query<Position>())
            {
                checksum ^= context.Entities.GetComponent<Position>(entity).X;
            }

            GC.KeepAlive(checksum);
        }
    }

    private readonly record struct Position(int X, int Y);
}
