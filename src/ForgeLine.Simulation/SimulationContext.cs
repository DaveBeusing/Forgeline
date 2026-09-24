using ForgeLine.Ecs;

namespace ForgeLine.Simulation;

public sealed class SimulationContext
{
    internal SimulationContext(EntityRegistry entities, SimulationRandom random)
    {
        Entities = entities;
        Random = random;
    }

    public EntityRegistry Entities { get; }

    public SimulationRandom Random { get; }

    public SimulationTick Tick { get; internal set; }

    public SimulationPhase Phase { get; internal set; }
}
