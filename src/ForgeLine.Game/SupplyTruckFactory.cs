using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

public static class SupplyTruckFactory
{
    private static readonly CargoTruckDefinition DefaultDefinition =
        new(
            "unit.supply_truck",
            cargoCapacity: 140.0,
            movement: new GroundMovement(
                maximumSpeed: 12.0f,
                acceleration: 6.0f,
                deceleration: 10.0f,
                turnRateRadiansPerSecond: 2.3f,
                radius: 1.9f,
                stopRadius: 1.0f,
                separationRadius: 5.5f,
                obstacleLookAhead: 8.0f,
                maximumSlopeDegrees: 25.0f,
                heightOffset: 1.0f),
            visualScale: new Vector3(
                3.2f,
                2.2f,
                6.4f),
            visualId: 2);

    public static EntityId Create(
        EntityRegistry entities,
        InventoryStore inventories,
        Vector3 position,
        PlayerId owner,
        CargoTransportSystem transportSystem,
        double fuelTarget = 70.0,
        double ammunitionTarget = 70.0,
        float loadRangeMeters = 12.0f,
        float resupplyRangeMeters = 16.0f)
    {
        EntityId entity =
            CargoTruckFactory.Create(
                entities,
                inventories,
                position,
                owner,
                transportSystem,
                DefaultDefinition);

        CargoTransport transport =
            entities.GetComponent<CargoTransport>(entity);

        const double operationalFuelCapacity = 80.0;
        InventoryId operationalFuel =
            inventories.CreateInventory(
                new InventorySpecification(
                    operationalFuelCapacity,
                    [ResourceIds.Fuel],
                    new Dictionary<ResourceId, double>
                    {
                        [ResourceIds.Fuel] = operationalFuelCapacity
                    }));
        InventoryOperationResult fuelInitialization =
            inventories.Add(
                operationalFuel,
                ResourceIds.Fuel,
                operationalFuelCapacity);
        if (!fuelInitialization.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to initialize supply-truck fuel: {fuelInitialization.Failure}.");
        }

        entities.AddComponent(
            entity,
            new UnitFuelState(
                operationalFuel,
                operationalFuelCapacity,
                consumptionPerMeter: 0.08));
        entities.AddComponent(
            entity,
            new UnitSupplyPriority(
                BattlefieldSupplyPriority.High));
        entities.AddComponent(
            entity,
            new UnitSupplyState(
                FuelFraction: 1.0,
                AmmunitionFraction: 1.0,
                BattlefieldSupplyStatus.Supplied,
                Simulation.SimulationTick.Zero));
        entities.AddComponent(
            entity,
            new SupplyMovementConstraint(
                maximumSpeedScale: 1.0f,
                canMove: true));

        entities.AddComponent(
            entity,
            new SupplyTruck(
                transport.CargoInventory,
                owner,
                loadRangeMeters,
                resupplyRangeMeters,
                fuelTarget,
                ammunitionTarget));
        entities.AddComponent(
            entity,
            new SupplyProvider(
                transport.CargoInventory,
                owner,
                resupplyRangeMeters));

        return entity;
    }
}
