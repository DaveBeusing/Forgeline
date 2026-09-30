using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Intelligence;
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
                CreateUnitWreckPresentation(
                    context,
                    destruction.Entity);
                CreateBuildingWreckPresentation(
                    context,
                    destruction.Entity);
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

    private static void CreateUnitWreckPresentation(
        SimulationContext context,
        EntityId destroyedEntity)
    {
        if (!context.Entities.TryGetComponent(
                destroyedEntity,
                out UnitIdentity unit) ||
            !context.Entities.TryGetComponent(
                destroyedEntity,
                out WorldTransform transform) ||
            !context.Entities.TryGetComponent(
                destroyedEntity,
                out VisualIdentity visual))
        {
            return;
        }

        EntityId wreck =
            context.Entities.CreateEntity();

        context.Entities.AddComponent(
            wreck,
            transform);
        context.Entities.AddComponent(
            wreck,
            visual);
        context.Entities.AddComponent(
            wreck,
            new UnitWreckPresentationIdentity(
                unit.UnitId,
                unit.ContentFaction));

        if (context.Entities.TryGetComponent(
                destroyedEntity,
                out IntelligenceSignature signature))
        {
            context.Entities.AddComponent(
                wreck,
                signature);
        }
    }

    private static void CreateBuildingWreckPresentation(
        SimulationContext context,
        EntityId destroyedEntity)
    {
        if (!context.Entities.TryGetComponent(
                destroyedEntity,
                out CompletedBuilding building) ||
            !context.Entities.TryGetComponent(
                destroyedEntity,
                out WorldTransform transform) ||
            !context.Entities.TryGetComponent(
                destroyedEntity,
                out VisualIdentity visual))
        {
            return;
        }

        EntityId wreck =
            context.Entities.CreateEntity();

        context.Entities.AddComponent(
            wreck,
            transform);
        context.Entities.AddComponent(
            wreck,
            visual);
        context.Entities.AddComponent(
            wreck,
            new BuildingWreckPresentationIdentity(
                building.BuildingId,
                building.Owner));

        if (context.Entities.TryGetComponent(
                destroyedEntity,
                out IntelligenceSignature signature))
        {
            context.Entities.AddComponent(
                wreck,
                signature);
        }
    }

}
