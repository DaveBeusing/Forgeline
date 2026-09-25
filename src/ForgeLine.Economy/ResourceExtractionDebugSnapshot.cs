using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Economy;

public readonly record struct ResourceDepositReadModel(
    EntityId Entity,
    ResourceId ResourceId,
    string ResourceKey,
    AxisAlignedBounds Bounds,
    double TotalQuantity,
    double RemainingQuantity,
    double Richness,
    FactionId Owner,
    ResourceDepositState State);

public readonly record struct ResourceExtractorReadModel(
    EntityId Entity,
    EntityId Deposit,
    ResourceId ResourceId,
    double MaximumExtractionRatePerSecond,
    FactionId Owner,
    bool Enabled,
    ResourceExtractorState State,
    EntityId OutputInventory);

public sealed class ResourceExtractionDebugSnapshot
{
    public ResourceExtractionDebugSnapshot(
        IReadOnlyList<ResourceDepositReadModel> deposits,
        IReadOnlyList<ResourceExtractorReadModel> extractors,
        ResourceExtractionMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(deposits);
        ArgumentNullException.ThrowIfNull(extractors);

        Deposits = deposits;
        Extractors = extractors;
        Metrics = metrics;
    }

    public IReadOnlyList<ResourceDepositReadModel> Deposits { get; }

    public IReadOnlyList<ResourceExtractorReadModel> Extractors { get; }

    public ResourceExtractionMetrics Metrics { get; }

    public static ResourceExtractionDebugSnapshot Capture(
        EntityRegistry entities,
        ResourceExtractionMetrics metrics,
        ResourceCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var deposits = new List<ResourceDepositReadModel>(
            entities.GetComponentCount<ResourceDeposit>());
        var extractors = new List<ResourceExtractorReadModel>(
            entities.GetComponentCount<ResourceExtractor>());

        foreach (EntityId entity in
                 entities.Query<ResourceDeposit>(QueryIterationOrder.StableByEntityIndex))
        {
            ResourceDeposit deposit = entities.GetComponent<ResourceDeposit>(entity);
            string resourceKey =
                catalog is not null &&
                catalog.TryGet(deposit.ResourceId, out ResourceDefinition? definition)
                    ? definition.Key
                    : deposit.ResourceId.ToString();

            deposits.Add(
                new ResourceDepositReadModel(
                    entity,
                    deposit.ResourceId,
                    resourceKey,
                    deposit.Bounds,
                    deposit.TotalQuantity,
                    deposit.RemainingQuantity,
                    deposit.Richness,
                    deposit.Owner,
                    deposit.State));
        }

        foreach (EntityId entity in
                 entities.Query<ResourceExtractor>(QueryIterationOrder.StableByEntityIndex))
        {
            ResourceExtractor extractor =
                entities.GetComponent<ResourceExtractor>(entity);

            extractors.Add(
                new ResourceExtractorReadModel(
                    entity,
                    extractor.Deposit,
                    extractor.ResourceId,
                    extractor.MaximumExtractionRatePerSecond,
                    extractor.Owner,
                    extractor.Enabled,
                    extractor.State,
                    extractor.OutputInventory));
        }

        return new ResourceExtractionDebugSnapshot(
            deposits,
            extractors,
            metrics);
    }
}
