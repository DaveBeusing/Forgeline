using ForgeLine.Ecs;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation;

public sealed class SimulationContext
{
    internal SimulationContext(
        EntityRegistry entities,
        SimulationRandom random,
        JobScheduler? jobScheduler)
    {
        Entities = entities;
        Random = random;
        Jobs = new SimulationJobs(jobScheduler);
    }

    public EntityRegistry Entities { get; }

    public SimulationRandom Random { get; }

    public SimulationJobs Jobs { get; }

    public SimulationTick Tick { get; internal set; }

    public SimulationPhase Phase { get; internal set; }
}
