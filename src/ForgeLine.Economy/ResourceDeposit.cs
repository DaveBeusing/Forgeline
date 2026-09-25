using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Economy;

public enum ResourceDepositState
{
    Available = 0,
    Depleted = 1
}

public readonly record struct ResourceDeposit
{
    private ResourceDeposit(
        ResourceId resourceId,
        AxisAlignedBounds bounds,
        double totalQuantity,
        double remainingQuantity,
        double baseExtractionRatePerSecond,
        double richness,
        FactionId owner,
        ResourceDepositState state)
    {
        ResourceId = resourceId;
        Bounds = bounds;
        TotalQuantity = totalQuantity;
        RemainingQuantity = remainingQuantity;
        BaseExtractionRatePerSecond = baseExtractionRatePerSecond;
        Richness = richness;
        Owner = owner;
        State = state;
    }

    public ResourceDeposit(
        ResourceId resourceId,
        AxisAlignedBounds bounds,
        double totalQuantity,
        double baseExtractionRatePerSecond,
        double richness = 1.0,
        FactionId owner = default)
        : this(
            resourceId,
            bounds,
            totalQuantity,
            totalQuantity,
            baseExtractionRatePerSecond,
            richness,
            owner,
            ResourceDepositState.Available)
    {
        Validate(
            resourceId,
            totalQuantity,
            totalQuantity,
            baseExtractionRatePerSecond,
            richness);
    }

    public ResourceId ResourceId { get; }

    public AxisAlignedBounds Bounds { get; }

    public double TotalQuantity { get; }

    public double RemainingQuantity { get; }

    public double BaseExtractionRatePerSecond { get; }

    public double Richness { get; }

    public FactionId Owner { get; }

    public ResourceDepositState State { get; }

    public bool IsDepleted => State == ResourceDepositState.Depleted;

    public double RemainingFraction =>
        TotalQuantity <= 0.0 ? 0.0 : RemainingQuantity / TotalQuantity;

    public static ResourceDeposit Restore(
        ResourceId resourceId,
        AxisAlignedBounds bounds,
        double totalQuantity,
        double remainingQuantity,
        double baseExtractionRatePerSecond,
        double richness = 1.0,
        FactionId owner = default)
    {
        Validate(
            resourceId,
            totalQuantity,
            remainingQuantity,
            baseExtractionRatePerSecond,
            richness);

        return new ResourceDeposit(
            resourceId,
            bounds,
            totalQuantity,
            remainingQuantity,
            baseExtractionRatePerSecond,
            richness,
            owner,
            remainingQuantity == 0.0
                ? ResourceDepositState.Depleted
                : ResourceDepositState.Available);
    }

    internal ResourceDeposit Extract(
        double requestedQuantity,
        out double extractedQuantity)
    {
        if (!double.IsFinite(requestedQuantity) || requestedQuantity < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        }

        if (IsDepleted || requestedQuantity == 0.0)
        {
            extractedQuantity = 0.0;
            return this;
        }

        extractedQuantity = Math.Min(RemainingQuantity, requestedQuantity);
        double remaining = Math.Max(0.0, RemainingQuantity - extractedQuantity);

        return new ResourceDeposit(
            ResourceId,
            Bounds,
            TotalQuantity,
            remaining,
            BaseExtractionRatePerSecond,
            Richness,
            Owner,
            remaining == 0.0
                ? ResourceDepositState.Depleted
                : ResourceDepositState.Available);
    }

    private static void Validate(
        ResourceId resourceId,
        double totalQuantity,
        double remainingQuantity,
        double baseExtractionRatePerSecond,
        double richness)
    {
        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Resource deposits require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(totalQuantity) || totalQuantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalQuantity));
        }

        if (!double.IsFinite(remainingQuantity) ||
            remainingQuantity < 0.0 ||
            remainingQuantity > totalQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingQuantity));
        }

        if (!double.IsFinite(baseExtractionRatePerSecond) ||
            baseExtractionRatePerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseExtractionRatePerSecond));
        }

        if (!double.IsFinite(richness) || richness <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(richness));
        }
    }
}
