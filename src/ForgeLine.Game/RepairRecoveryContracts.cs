using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum RepairRecoveryStatus : byte
{
    None = 0,
    Repairing = 1,
    FullyRecovered = 2,
    NoMaterial = 3
}

public readonly record struct RepairProvider
{
    public RepairProvider(
        InventoryId inventoryId,
        PlayerId owner,
        float repairRangeMeters,
        double healthPerTick,
        ResourceId resourceId,
        double resourcePerHealth)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Repair providers require a valid inventory.",
                nameof(inventoryId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Repair providers require a valid owner.",
                nameof(owner));
        }

        if (!float.IsFinite(repairRangeMeters) ||
            repairRangeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(repairRangeMeters));
        }

        if (!double.IsFinite(healthPerTick) ||
            healthPerTick <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(healthPerTick));
        }

        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Repair providers require a physical repair resource.",
                nameof(resourceId));
        }

        if (!double.IsFinite(resourcePerHealth) ||
            resourcePerHealth <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resourcePerHealth));
        }

        InventoryId = inventoryId;
        Owner = owner;
        RepairRangeMeters = repairRangeMeters;
        HealthPerTick = healthPerTick;
        ResourceId = resourceId;
        ResourcePerHealth = resourcePerHealth;
    }

    public InventoryId InventoryId { get; }

    public PlayerId Owner { get; }

    public float RepairRangeMeters { get; }

    public double HealthPerTick { get; }

    public ResourceId ResourceId { get; }

    public double ResourcePerHealth { get; }
}

public readonly record struct RepairRecoveryState(
    EntityId Provider,
    RepairRecoveryStatus Status,
    double HealthRestoredThisTick,
    double ResourceConsumedThisTick,
    SimulationTick UpdatedAtTick);

public enum RetreatRecoveryReason : byte
{
    None = 0,
    RepairAndSupply = 1,
    Repair = 2,
    Supply = 3,
    NoProvider = 4
}

public readonly record struct RetreatRecoveryState(
    EntityId Provider,
    RetreatRecoveryReason Reason,
    System.Numerics.Vector3 Destination,
    SimulationTick IssuedAtTick);
