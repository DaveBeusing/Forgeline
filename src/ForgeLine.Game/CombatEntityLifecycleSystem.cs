using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class CombatEntityLifecycleSystem : ISimulationSystem
{
    private readonly CombatRuntime _runtime;
    private readonly SpatialGridIndex? _spatialIndex;

    public CombatEntityLifecycleSystem(
        CombatRuntime runtime,
        SpatialGridIndex? spatialIndex = null)
    {
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
        _spatialIndex = spatialIndex;
    }

    public SimulationPhase Phase =>
        SimulationPhase.EntityLifecycle;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);

        ReadOnlySpan<PendingCombatDestruction> pending =
            _runtime.PendingDestructions;

        for (int index = 0;
             index < pending.Length;
             index++)
        {
            PendingCombatDestruction destruction =
                pending[index];

            if (!context.Entities.IsAlive(
                    destruction.Entity))
            {
                continue;
            }

            if (destruction.EmitCombatDestructionEvent)
            {
                _runtime.RecordDestruction(
                    destruction);
            }

            _spatialIndex?.Remove(
                destruction.Entity);
            context.Entities.DestroyEntity(
                destruction.Entity);
        }

        _runtime.ClearPendingDestructions();
        _runtime.SetActiveProjectiles(
            context.Entities.GetComponentCount<ProjectileState>());
    }
}
