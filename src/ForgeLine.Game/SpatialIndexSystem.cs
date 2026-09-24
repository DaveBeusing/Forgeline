using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class SpatialIndexSystem : ISimulationSystem
{
    private readonly SpatialIndexSynchronizer _synchronizer;

    public SpatialIndexSystem(SpatialIndexSynchronizer synchronizer)
    {
        _synchronizer = synchronizer ??
            throw new ArgumentNullException(nameof(synchronizer));
    }

    public SimulationPhase Phase => SimulationPhase.Movement;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _synchronizer.SynchronizeMovement(context.Entities);
    }
}

public sealed class SpatialIndexCleanupSystem : ISimulationSystem
{
    private readonly SpatialIndexSynchronizer _synchronizer;

    public SpatialIndexCleanupSystem(SpatialIndexSynchronizer synchronizer)
    {
        _synchronizer = synchronizer ??
            throw new ArgumentNullException(nameof(synchronizer));
    }

    public SimulationPhase Phase => SimulationPhase.SnapshotEvents;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _synchronizer.CleanupLifecycle(context.Entities);
    }
}
