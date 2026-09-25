using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct UnitIdentity(
    UnitId UnitId,
    FactionId ContentFaction);

public sealed class UnitFactory
{
    private readonly EntityRegistry _entities;
    private readonly InventoryStore _inventories;
    private readonly CargoTransportSystem _cargoTransport;

    public UnitFactory(
        EntityRegistry entities,
        InventoryStore inventories,
        CargoTransportSystem cargoTransport)
    {
        _entities = entities ??
            throw new ArgumentNullException(nameof(entities));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _cargoTransport = cargoTransport ??
            throw new ArgumentNullException(nameof(cargoTransport));
    }

    public EntityId Create(
        UnitDefinition definition,
        Vector3 position,
        PlayerId owner)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Units require a valid owner.",
                nameof(owner));
        }

        if (!IsFinite(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        EntityId entity =
            definition.IsCargoTransport
                ? CreateCargoEntity(definition, position, owner)
                : CreateStandardEntity(definition, position, owner);

        AttachCommonGameplay(
            entity,
            definition,
            owner);

        return entity;
    }

    private EntityId CreateStandardEntity(
        UnitDefinition definition,
        Vector3 position,
        PlayerId owner)
    {
        EntityId entity = _entities.CreateEntity();

        _entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                definition.VisualScale));
        _entities.AddComponent(
            entity,
            new VisualIdentity(definition.VisualId));
        _entities.AddComponent(
            entity,
            new ControllableEntity(
                owner,
                ControllableEntityCategory.Unit));
        _entities.AddComponent(
            entity,
            definition.Movement);
        _entities.AddComponent(
            entity,
            GroundMovementState.Stationary());
        _entities.AddComponent(
            entity,
            new NavigationAgent(definition.MovementClass));
        _entities.AddComponent(
            entity,
            new SpatialPresence(
                definition.VisualScale * 0.5f,
                new SpatialEntryMetadata(
                    owner.Value,
                    (ulong)ControllableEntityCategory.Unit,
                    SpatialMobility.Mobile)));

        return entity;
    }

    private EntityId CreateCargoEntity(
        UnitDefinition definition,
        Vector3 position,
        PlayerId owner)
    {
        var transportDefinition =
            new CargoTruckDefinition(
                definition.Key,
                definition.CargoCapacity,
                definition.Movement,
                definition.VisualScale,
                definition.VisualId);

        EntityId entity =
            CargoTruckFactory.Create(
                _entities,
                _inventories,
                position,
                owner,
                _cargoTransport,
                transportDefinition);

        if (definition.IsSupplyTruck)
        {
            CargoTransport transport =
                _entities.GetComponent<CargoTransport>(entity);

            _entities.AddComponent(
                entity,
                new SupplyTruck(
                    transport.CargoInventory,
                    owner,
                    definition.SupplyLoadRangeMeters,
                    definition.SupplyRangeMeters,
                    definition.SupplyFuelTarget,
                    definition.SupplyAmmunitionTarget));
            _entities.AddComponent(
                entity,
                new SupplyProvider(
                    transport.CargoInventory,
                    owner,
                    definition.SupplyRangeMeters));
        }

        return entity;
    }

    private void AttachCommonGameplay(
        EntityId entity,
        UnitDefinition definition,
        PlayerId owner)
    {
        FactionId ownerFaction = ToFactionId(owner);

        _entities.AddComponent(
            entity,
            new UnitIdentity(
                definition.Id,
                definition.Faction));
        _entities.AddComponent(
            entity,
            new Combatant(ownerFaction));
        _entities.AddComponent(
            entity,
            new Targetable(definition.TargetClass));
        _entities.AddComponent(
            entity,
            new TargetPriority(definition.TargetPriority));
        _entities.AddComponent(
            entity,
            HealthState.Full(definition.MaximumHealth));
        _entities.AddComponent(
            entity,
            new CombatHitbox(
                definition.VisualScale * 0.5f));
        _entities.AddComponent(
            entity,
            new IntelligenceSignature(
                ownerFaction,
                definition.Id.Value));

        if (definition.ArmorProfileId.IsSpecified)
        {
            _entities.AddComponent(
                entity,
                new ArmorState(
                    definition.ArmorProfileId));
        }

        if (definition.HasDirectWeapon)
        {
            _entities.AddComponent(
                entity,
                new WeaponState(
                    definition.WeaponId,
                    EntityId.Invalid));
            _entities.AddComponent(
                entity,
                FirePolicyState.FireAtWill);
            _entities.AddComponent(
                entity,
                AutoTargetState.EnabledByDefault);
        }

        if (definition.HasArtilleryWeapon)
        {
            _entities.AddComponent(
                entity,
                new ArtilleryCapability(
                    definition.ArtilleryWeaponId));
        }

        if (definition.VisualSensorRangeMeters > 0.0f)
        {
            _entities.AddComponent(
                entity,
                new VisualSensorState(
                    ownerFaction,
                    definition.VisualSensorRangeMeters,
                    definition.VisualSensorUpdateIntervalTicks));
        }

        if (definition.HasRadar)
        {
            _entities.AddComponent(
                entity,
                new RadarSensorState(
                    ownerFaction,
                    definition.RadarDetectionRangeMeters,
                    definition.RadarIdentificationRangeMeters,
                    definition.RadarUpdateIntervalTicks));
        }

        BattlefieldSupplyFactory.AttachUnitSupply(
            _entities,
            _inventories,
            entity,
            definition.FuelCapacity,
            definition.AmmunitionCapacity,
            definition.FuelConsumptionPerMeter,
            initialFuel:
                definition.FuelCapacity *
                definition.InitialFuelFraction,
            initialAmmunition:
                definition.AmmunitionCapacity *
                definition.InitialAmmunitionFraction,
            definition.SupplyPriority);

        if (definition.AutomaticResupply)
        {
            _entities.AddComponent(
                entity,
                new AutomaticResupplyPolicy());
        }
    }

    private static FactionId ToFactionId(PlayerId owner)
    {
        if (owner.Value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(owner),
                "Player identifiers used by combat/intelligence must fit in FactionId.");
        }

        return new FactionId((uint)owner.Value);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
