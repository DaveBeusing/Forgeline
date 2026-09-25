using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

public static class BattlefieldSupplyFactory
{
    public static InventoryId AttachUnitSupply(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity,
        double fuelCapacity,
        double ammunitionCapacity,
        double fuelConsumptionPerMeter,
        double initialFuel = 0.0,
        double initialAmmunition = 0.0,
        BattlefieldSupplyPriority priority =
            BattlefieldSupplyPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(inventories);

        if (!entities.IsAlive(entity))
        {
            throw new ArgumentException(
                "Unit supply can only be attached to a live entity.",
                nameof(entity));
        }

        ValidateInitialQuantity(
            initialFuel,
            fuelCapacity,
            nameof(initialFuel));
        ValidateInitialQuantity(
            initialAmmunition,
            ammunitionCapacity,
            nameof(initialAmmunition));

        var capacities =
            new Dictionary<ResourceId, double>
            {
                [ResourceIds.Fuel] = fuelCapacity,
                [ResourceIds.Ammunition] = ammunitionCapacity
            };

        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    fuelCapacity + ammunitionCapacity,
                    [ResourceIds.Fuel, ResourceIds.Ammunition],
                    capacities));

        if (initialFuel > 0.0)
        {
            EnsureAdded(
                inventories,
                inventory,
                ResourceIds.Fuel,
                initialFuel);
        }

        if (initialAmmunition > 0.0)
        {
            EnsureAdded(
                inventories,
                inventory,
                ResourceIds.Ammunition,
                initialAmmunition);
        }

        entities.AddComponent(
            entity,
            new UnitFuelState(
                inventory,
                fuelCapacity,
                fuelConsumptionPerMeter));
        entities.AddComponent(
            entity,
            new AmmunitionState(
                inventory,
                ammunitionCapacity));
        entities.AddComponent(
            entity,
            new UnitSupplyPriority(priority));
        entities.AddComponent(
            entity,
            new UnitSupplyState(
                fuelCapacity > 0.0
                    ? initialFuel / fuelCapacity
                    : 0.0,
                ammunitionCapacity > 0.0
                    ? initialAmmunition / ammunitionCapacity
                    : 0.0,
                ResolveStatus(
                    initialFuel / fuelCapacity,
                    initialAmmunition / ammunitionCapacity),
                Simulation.SimulationTick.Zero));
        entities.AddComponent(
            entity,
            CreateMovementConstraint(
                initialFuel / fuelCapacity));

        return inventory;
    }

    internal static BattlefieldSupplyStatus ResolveStatus(
        double fuelFraction,
        double ammunitionFraction)
    {
        double minimum =
            Math.Min(
                Math.Clamp(fuelFraction, 0.0, 1.0),
                Math.Clamp(ammunitionFraction, 0.0, 1.0));

        if (minimum <= 0.0)
        {
            return BattlefieldSupplyStatus.Unsupplied;
        }

        if (minimum < 0.2)
        {
            return BattlefieldSupplyStatus.Critical;
        }

        return minimum < 0.5
            ? BattlefieldSupplyStatus.LowSupply
            : BattlefieldSupplyStatus.Supplied;
    }

    internal static SupplyMovementConstraint CreateMovementConstraint(
        double fuelFraction)
    {
        double clamped = Math.Clamp(fuelFraction, 0.0, 1.0);

        return clamped <= 0.0
            ? new SupplyMovementConstraint(
                maximumSpeedScale: 0.0f,
                canMove: false)
            : clamped < 0.2
                ? new SupplyMovementConstraint(
                    maximumSpeedScale: 0.7f,
                    canMove: true)
                : clamped < 0.5
                    ? new SupplyMovementConstraint(
                        maximumSpeedScale: 0.9f,
                        canMove: true)
                    : new SupplyMovementConstraint(
                        maximumSpeedScale: 1.0f,
                        canMove: true);
    }

    private static void ValidateInitialQuantity(
        double quantity,
        double capacity,
        string parameterName)
    {
        if (!double.IsFinite(capacity) || capacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Supply capacity must be finite and positive.");
        }

        if (!double.IsFinite(quantity) ||
            quantity < 0.0 ||
            quantity > capacity)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void EnsureAdded(
        InventoryStore inventories,
        InventoryId inventory,
        ResourceId resource,
        double quantity)
    {
        InventoryOperationResult result =
            inventories.Add(
                inventory,
                resource,
                quantity);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to initialize unit supply inventory: {result.Failure}.");
        }
    }
}
