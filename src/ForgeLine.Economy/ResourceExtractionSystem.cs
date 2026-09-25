using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public sealed class ResourceExtractionSystem : ISimulationSystem
{
    private readonly IResourceExtractionSink _sink;
    private readonly InventoryStore? _inventories;
    private double _totalExtractedQuantity;

    public ResourceExtractionSystem(
        IResourceExtractionSink? sink = null,
        InventoryStore? inventories = null)
    {
        _sink = sink ?? NullResourceExtractionSink.Instance;
        _inventories = inventories;
    }

    public SimulationPhase Phase => SimulationPhase.Economy;

    public ResourceExtractionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        EntityRegistry entities = context.Entities;
        int extractorCount = 0;
        int activeExtractorCount = 0;
        int blockedExtractorCount = 0;
        double extractedThisTick = 0.0;

        foreach (EntityId extractorEntity in
                 entities.Query<ResourceExtractor>(QueryIterationOrder.StableByEntityIndex))
        {
            extractorCount++;
            ResourceExtractor extractor =
                entities.GetComponent<ResourceExtractor>(extractorEntity);

            if (!extractor.Enabled)
            {
                SetExtractorState(
                    entities,
                    extractorEntity,
                    extractor,
                    ResourceExtractorState.Disabled);
                continue;
            }

            double powerScale = 1.0;
            if (entities.TryGetComponent(extractorEntity, out PowerConsumer powerConsumer))
            {
                powerScale = powerConsumer.OperationalScale;
                if (powerScale <= 0.0)
                {
                    blockedExtractorCount++;
                    SetExtractorState(
                        entities,
                        extractorEntity,
                        extractor,
                        ResourceExtractorState.PowerUnavailable);
                    continue;
                }
            }

            if (!entities.IsAlive(extractor.Deposit) ||
                !entities.TryGetComponent(
                    extractor.Deposit,
                    out ResourceDeposit deposit))
            {
                SetExtractorState(
                    entities,
                    extractorEntity,
                    extractor,
                    ResourceExtractorState.InvalidDepositReference);
                continue;
            }

            if (deposit.ResourceId != extractor.ResourceId)
            {
                SetExtractorState(
                    entities,
                    extractorEntity,
                    extractor,
                    ResourceExtractorState.ResourceMismatch);
                continue;
            }

            if (deposit.Owner.IsSpecified && deposit.Owner != extractor.Owner)
            {
                SetExtractorState(
                    entities,
                    extractorEntity,
                    extractor,
                    ResourceExtractorState.OwnershipMismatch);
                continue;
            }

            if (deposit.IsDepleted)
            {
                SetExtractorState(
                    entities,
                    extractorEntity,
                    extractor,
                    ResourceExtractorState.DepositDepleted);
                continue;
            }

            double effectiveRatePerSecond =
                Math.Min(
                    extractor.MaximumExtractionRatePerSecond,
                    deposit.BaseExtractionRatePerSecond) *
                deposit.Richness *
                powerScale;
            double requestedQuantity =
                effectiveRatePerSecond * context.TickDuration.TotalSeconds;
            requestedQuantity =
                Math.Min(requestedQuantity, deposit.RemainingQuantity);

            bool outputConstrained = false;
            InventoryId outputInventoryId = InventoryId.None;

            if (extractor.OutputInventory.IsValid)
            {
                if (_inventories is null ||
                    !entities.IsAlive(extractor.OutputInventory) ||
                    !entities.TryGetComponent(
                        extractor.OutputInventory,
                        out InventoryStorage storage) ||
                    !_inventories.Contains(storage.InventoryId))
                {
                    blockedExtractorCount++;
                    SetExtractorState(
                        entities,
                        extractorEntity,
                        extractor,
                        ResourceExtractorState.OutputUnavailable);
                    continue;
                }

                outputInventoryId = storage.InventoryId;
                double acceptedQuantity =
                    _inventories.GetAddableQuantity(
                        outputInventoryId,
                        extractor.ResourceId,
                        requestedQuantity);

                if (acceptedQuantity <= 0.0)
                {
                    blockedExtractorCount++;
                    SetExtractorState(
                        entities,
                        extractorEntity,
                        extractor,
                        ResourceExtractorState.OutputBlocked);
                    continue;
                }

                outputConstrained = acceptedQuantity < requestedQuantity;
                requestedQuantity = acceptedQuantity;
            }

            ResourceDeposit updatedDeposit =
                deposit.Extract(requestedQuantity, out double extractedQuantity);

            if (extractedQuantity <= 0.0)
            {
                continue;
            }

            if (outputInventoryId.IsSpecified)
            {
                InventoryOperationResult addResult =
                    _inventories!.Add(
                        outputInventoryId,
                        extractor.ResourceId,
                        extractedQuantity);

                EngineInvariant.Require(
                    addResult.Succeeded,
                    DiagnosticCategory.Simulation,
                    "INVENTORY_EXTRACTION_COMMIT_FAILED",
                    $"Inventory {outputInventoryId} rejected a previously validated extraction commit.");
            }

            entities.SetComponent(extractor.Deposit, updatedDeposit);

            ResourceExtractorState extractorState =
                updatedDeposit.IsDepleted
                    ? ResourceExtractorState.DepositDepleted
                    : outputConstrained
                        ? ResourceExtractorState.OutputConstrained
                        : powerScale < 1.0
                            ? ResourceExtractorState.PowerConstrained
                            : ResourceExtractorState.Extracting;
            SetExtractorState(
                entities,
                extractorEntity,
                extractor,
                extractorState);

            activeExtractorCount++;
            extractedThisTick += extractedQuantity;

            _sink.OnExtracted(
                new ResourceExtractionResult(
                    context.Tick,
                    extractorEntity,
                    extractor.Deposit,
                    extractor.ResourceId,
                    extractedQuantity));
        }

        int depositCount = 0;
        int depletedDepositCount = 0;

        foreach (EntityId depositEntity in
                 entities.Query<ResourceDeposit>(QueryIterationOrder.StableByEntityIndex))
        {
            depositCount++;
            if (entities.GetComponent<ResourceDeposit>(depositEntity).IsDepleted)
            {
                depletedDepositCount++;
            }
        }

        _totalExtractedQuantity += extractedThisTick;
        double tickSeconds = context.TickDuration.TotalSeconds;
        Metrics = new ResourceExtractionMetrics(
            depositCount,
            depletedDepositCount,
            extractorCount,
            activeExtractorCount,
            blockedExtractorCount,
            extractedThisTick,
            tickSeconds > 0.0 ? extractedThisTick / tickSeconds : 0.0,
            _totalExtractedQuantity);
    }

    private static void SetExtractorState(
        EntityRegistry entities,
        EntityId extractorEntity,
        ResourceExtractor extractor,
        ResourceExtractorState state)
    {
        if (extractor.State == state)
        {
            return;
        }

        entities.SetComponent(
            extractorEntity,
            extractor.WithState(state));
    }
}
