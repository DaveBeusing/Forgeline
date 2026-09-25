using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum BattlefieldSupplyStatus : byte
{
    Supplied = 0,
    LowSupply = 1,
    Critical = 2,
    Unsupplied = 3
}

public enum BattlefieldSupplyPriority : byte
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

[Flags]
public enum BattlefieldSupplyResource : byte
{
    None = 0,
    Fuel = 1 << 0,
    Ammunition = 1 << 1,
    All = Fuel | Ammunition
}

public readonly record struct UnitFuelState
{
    public UnitFuelState(
        InventoryId inventoryId,
        double capacity,
        double consumptionPerMeter,
        Vector3 observedPosition = default,
        bool hasObservedPosition = false)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Fuel state requires a valid inventory.",
                nameof(inventoryId));
        }

        if (!double.IsFinite(capacity) || capacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (!double.IsFinite(consumptionPerMeter) ||
            consumptionPerMeter < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consumptionPerMeter));
        }

        if (!IsFinite(observedPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedPosition));
        }

        InventoryId = inventoryId;
        Capacity = capacity;
        ConsumptionPerMeter = consumptionPerMeter;
        ObservedPosition = observedPosition;
        HasObservedPosition = hasObservedPosition;
    }

    public InventoryId InventoryId { get; }

    public double Capacity { get; }

    public double ConsumptionPerMeter { get; }

    public Vector3 ObservedPosition { get; }

    public bool HasObservedPosition { get; }

    public UnitFuelState WithObservedPosition(Vector3 position) =>
        new(
            InventoryId,
            Capacity,
            ConsumptionPerMeter,
            position,
            hasObservedPosition: true);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct UnitSupplyPriority
{
    public UnitSupplyPriority(
        BattlefieldSupplyPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        Priority = priority;
    }

    public BattlefieldSupplyPriority Priority { get; }
}

public readonly record struct UnitSupplyState(
    double FuelFraction,
    double AmmunitionFraction,
    BattlefieldSupplyStatus Status,
    SimulationTick UpdatedAtTick);

public readonly record struct SupplyMovementConstraint
{
    public SupplyMovementConstraint(
        float maximumSpeedScale,
        bool canMove)
    {
        if (!float.IsFinite(maximumSpeedScale) ||
            maximumSpeedScale < 0.0f ||
            maximumSpeedScale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSpeedScale));
        }

        MaximumSpeedScale = maximumSpeedScale;
        CanMove = canMove;
    }

    public float MaximumSpeedScale { get; }

    public bool CanMove { get; }
}

public enum SupplyDepotState : byte
{
    Operational = 0,
    Disabled = 1
}

public readonly record struct SupplyDepot
{
    public SupplyDepot(
        InventoryId inventoryId,
        PlayerId owner,
        SupplyDepotState state = SupplyDepotState.Operational)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Supply depots require a valid inventory.",
                nameof(inventoryId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Supply depots require a valid owner.",
                nameof(owner));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        InventoryId = inventoryId;
        Owner = owner;
        State = state;
    }

    public InventoryId InventoryId { get; }

    public PlayerId Owner { get; }

    public SupplyDepotState State { get; }

    public SupplyDepot WithState(SupplyDepotState state) =>
        new(InventoryId, Owner, state);
}

public readonly record struct SupplyProvider
{
    public SupplyProvider(
        InventoryId inventoryId,
        PlayerId owner,
        float resupplyRangeMeters,
        bool enabled = true)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Supply providers require a valid inventory.",
                nameof(inventoryId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Supply providers require a valid owner.",
                nameof(owner));
        }

        if (!float.IsFinite(resupplyRangeMeters) ||
            resupplyRangeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resupplyRangeMeters));
        }

        InventoryId = inventoryId;
        Owner = owner;
        ResupplyRangeMeters = resupplyRangeMeters;
        Enabled = enabled;
    }

    public InventoryId InventoryId { get; }

    public PlayerId Owner { get; }

    public float ResupplyRangeMeters { get; }

    public bool Enabled { get; }
}

public readonly record struct SupplyTruck
{
    public SupplyTruck(
        InventoryId inventoryId,
        PlayerId owner,
        float loadRangeMeters,
        float resupplyRangeMeters,
        double fuelTarget,
        double ammunitionTarget)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Supply trucks require a valid inventory.",
                nameof(inventoryId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Supply trucks require a valid owner.",
                nameof(owner));
        }

        if (!float.IsFinite(loadRangeMeters) ||
            loadRangeMeters <= 0.0f ||
            !float.IsFinite(resupplyRangeMeters) ||
            resupplyRangeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loadRangeMeters));
        }

        if (!double.IsFinite(fuelTarget) ||
            fuelTarget < 0.0 ||
            !double.IsFinite(ammunitionTarget) ||
            ammunitionTarget < 0.0 ||
            fuelTarget + ammunitionTarget <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fuelTarget));
        }

        InventoryId = inventoryId;
        Owner = owner;
        LoadRangeMeters = loadRangeMeters;
        ResupplyRangeMeters = resupplyRangeMeters;
        FuelTarget = fuelTarget;
        AmmunitionTarget = ammunitionTarget;
    }

    public InventoryId InventoryId { get; }

    public PlayerId Owner { get; }

    public float LoadRangeMeters { get; }

    public float ResupplyRangeMeters { get; }

    public double FuelTarget { get; }

    public double AmmunitionTarget { get; }
}

public readonly record struct ResupplyOrder(
    EntityId Provider,
    SimulationTick SubmittedAtTick,
    SimulationTick AcceptedAtTick);
