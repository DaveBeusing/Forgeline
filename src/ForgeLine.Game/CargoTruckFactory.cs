using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public static class CargoTruckFactory
{
    public static EntityId Create(
        EntityRegistry entities,
        InventoryStore inventories,
        Vector3 position,
        PlayerId owner,
        CargoTruckDefinition? definition = null,
        CargoTransportSystem? transportSystem = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(inventories);

        if (!IsFinite(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo trucks require a valid owner.",
                nameof(owner));
        }

        definition ??= CargoTruckDefinition.Default;

        InventoryId cargoInventory = inventories.CreateInventory(
            new InventorySpecification(
                definition.CargoCapacity));

        EntityId entity = entities.CreateEntity();
        var transform = new WorldTransform(
            position,
            Quaternion.Identity,
            definition.VisualScale);

        entities.AddComponent(entity, transform);
        entities.AddComponent(
            entity,
            new CargoTransport(
                cargoInventory,
                definition.CargoCapacity,
                owner));
        entities.AddComponent(
            entity,
            new InventoryStorage(cargoInventory));
        entities.AddComponent(
            entity,
            definition.Movement);
        entities.AddComponent(
            entity,
            GroundMovementState.Stationary());
        entities.AddComponent(
            entity,
            new NavigationAgent(
                NavigationMovementClass.Wheeled));
        entities.AddComponent(
            entity,
            CargoTransportRuntimeState.Idle);
        entities.AddComponent(
            entity,
            new VisualIdentity(definition.VisualId));
        entities.AddComponent(
            entity,
            new ControllableEntity(
                owner,
                ControllableEntityCategory.Logistics));
        entities.AddComponent(
            entity,
            new SpatialPresence(
                definition.VisualScale * 0.5f,
                new SpatialEntryMetadata(
                    owner.Value,
                    (ulong)ControllableEntityCategory.Logistics,
                    SpatialMobility.Mobile)));

        transportSystem?.TrackTransport(
            entity,
            cargoInventory);

        return entity;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
