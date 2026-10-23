using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Attaches the normal combat contract to a completed, owned structure.</summary>
public static class BuildingCombatInitialization
{
    public static void Initialize(
        EntityRegistry entities,
        EntityId entity,
        BuildingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        if (!entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out CompletedBuilding building) ||
            building.BuildingId != definition.Id ||
            !building.Owner.IsSpecified || building.Owner.Value > uint.MaxValue ||
            entities.HasComponent<ConstructionSite>(entity))
        {
            throw new InvalidOperationException(
                $"Entity {entity} is not a completed owned '{definition.Key}' structure.");
        }

        var faction = new FactionId((uint)building.Owner.Value);
        if (entities.TryGetComponent(entity, out ControllableEntity controllable))
        {
            if (controllable.Owner != building.Owner ||
                controllable.Category != ControllableEntityCategory.Building)
                throw new InvalidOperationException($"Building {entity} has inconsistent ownership.");
        }
        else
        {
            entities.AddComponent(entity,
                new ControllableEntity(building.Owner, ControllableEntityCategory.Building));
        }

        if (entities.TryGetComponent(entity, out Combatant combatant))
        {
            if (combatant.Faction != faction)
                throw new InvalidOperationException($"Building {entity} has an inconsistent combat faction.");
        }
        else
        {
            entities.AddComponent(entity, new Combatant(faction));
        }

        if (entities.TryGetComponent(entity, out Targetable targetable))
        {
            if (targetable.Class != TargetClass.Structure)
                throw new InvalidOperationException($"Building {entity} has an inconsistent target class.");
        }
        else
        {
            entities.AddComponent(entity, new Targetable(TargetClass.Structure));
        }

        // Reattaching the contract must never repair an existing damaged or depleted building.
        if (!entities.HasComponent<HealthState>(entity))
            entities.AddComponent(entity, HealthState.Full(definition.MaximumHealth));
        if (!entities.HasComponent<TargetPriority>(entity))
            entities.AddComponent(entity, new TargetPriority(definition.TargetPriority));
        if (!entities.HasComponent<CombatHitbox>(entity))
        {
            var halfExtents = entities.TryGetComponent(entity, out SpatialPresence presence)
                ? presence.HalfExtents
                : definition.Footprint.GetHalfExtents(BuildingOrientation.North);
            entities.AddComponent(entity, new CombatHitbox(halfExtents));
        }
        if (definition.ArmorProfileId.IsSpecified && !entities.HasComponent<ArmorState>(entity))
            entities.AddComponent(entity, new ArmorState(definition.ArmorProfileId));
    }
}
