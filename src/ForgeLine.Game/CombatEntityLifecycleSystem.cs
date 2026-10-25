using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class CombatEntityLifecycleSystem : ISimulationSystem
{
    private readonly CombatRuntime _runtime;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly BuildingCombatDestruction? _buildings;
    private readonly Action<SimulationContext>? _staticTopologyChanged;
    private readonly InventoryStore? _inventories;

    public CombatEntityLifecycleSystem(
        CombatRuntime runtime,
        SpatialGridIndex? spatialIndex = null,
        BuildingCombatDestruction? buildings = null,
        Action<SimulationContext>? staticTopologyChanged = null,
        InventoryStore? inventories = null)
    {
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
        _spatialIndex = spatialIndex;
        _buildings = buildings;
        _staticTopologyChanged = staticTopologyChanged;
        _inventories = inventories;
    }

    public SimulationPhase Phase =>
        SimulationPhase.EntityLifecycle;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);

        ReadOnlySpan<PendingCombatDestruction> pending =
            _runtime.PendingDestructions;
        bool staticTopologyChanged = false;

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

            // Conveyor wrecks retain their original entity, inventory and real support obstruction.
            // Repair is the explicit recovery path; occupied demolition/replacement is refused.
            if (context.Entities.HasComponent<ConveyorPiece>(destruction.Entity))
            {
                if(context.Entities.TryGetComponent(destruction.Entity,out ForgeLine.Economy.PowerConsumer power))
                    context.Entities.SetComponent(destruction.Entity,power.WithEnabled(false));
                continue;
            }
            _buildings?.RemoveBeforeEntityDestruction(context, destruction.Entity);
            ReleaseUnitSupply(context, destruction.Entity);
            staticTopologyChanged |= context.Entities.TryGetComponent(
                destruction.Entity, out SpatialPresence presence) && presence.Metadata.Mobility == SpatialMobility.Static;
            _spatialIndex?.Remove(
                destruction.Entity);
            context.Entities.DestroyEntity(
                destruction.Entity);
        }

        _runtime.ClearPendingDestructions();
        if (staticTopologyChanged)
            _staticTopologyChanged?.Invoke(context);
        _runtime.SetActiveProjectiles(
            context.Entities.GetComponentCount<ProjectileState>());
    }

    private void ReleaseUnitSupply(SimulationContext context, EntityId entity)
    {
        if (_inventories is null || !context.Entities.HasComponent<UnitIdentity>(entity)) return;
        InventoryId fuel = context.Entities.TryGetComponent(entity, out UnitFuelState drive)
            ? drive.InventoryId : default;
        InventoryId ammunition = context.Entities.TryGetComponent(entity, out AmmunitionState weapon)
            ? weapon.InventoryId : default;
        // Cargo inventory loss remains owned by CargoTransportSystem's reservation cleanup.
        InventoryId cargo = context.Entities.TryGetComponent(entity, out CargoTransport transport)
            ? transport.CargoInventory : default;
        if (fuel != cargo) _inventories.DestroyInventory(fuel);
        if (ammunition != fuel && ammunition != cargo) _inventories.DestroyInventory(ammunition);
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
                unit.ContentFaction) { SourceEntity = destroyedEntity });

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
