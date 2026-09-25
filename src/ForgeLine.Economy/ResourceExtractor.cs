using ForgeLine.Core;

namespace ForgeLine.Economy;

public enum ResourceExtractorState
{
    Unbound = 0,
    Ready = 1,
    Extracting = 2,
    Disabled = 3,
    DepositDepleted = 4,
    InvalidDepositReference = 5,
    ResourceMismatch = 6,
    OwnershipMismatch = 7,
    OutputUnavailable = 8,
    OutputBlocked = 9,
    OutputConstrained = 10
}

public readonly record struct ResourceExtractor
{
    public ResourceExtractor(
        EntityId deposit,
        ResourceId resourceId,
        double maximumExtractionRatePerSecond,
        FactionId owner = default,
        bool enabled = true,
        ResourceExtractorState state = ResourceExtractorState.Ready,
        EntityId outputInventory = default)
    {
        if (!deposit.IsValid)
        {
            throw new ArgumentException(
                "Resource extractors require a valid deposit entity reference.",
                nameof(deposit));
        }

        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Resource extractors require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(maximumExtractionRatePerSecond) ||
            maximumExtractionRatePerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExtractionRatePerSecond));
        }

        Deposit = deposit;
        ResourceId = resourceId;
        MaximumExtractionRatePerSecond = maximumExtractionRatePerSecond;
        Owner = owner;
        Enabled = enabled;
        State = enabled ? state : ResourceExtractorState.Disabled;
        OutputInventory = outputInventory;
    }

    public EntityId Deposit { get; }

    public ResourceId ResourceId { get; }

    public double MaximumExtractionRatePerSecond { get; }

    public FactionId Owner { get; }

    public bool Enabled { get; }

    public ResourceExtractorState State { get; }

    public EntityId OutputInventory { get; }

    internal ResourceExtractor WithState(ResourceExtractorState state) =>
        new(
            Deposit,
            ResourceId,
            MaximumExtractionRatePerSecond,
            Owner,
            Enabled,
            state,
            OutputInventory);
}
