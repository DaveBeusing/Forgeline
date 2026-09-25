using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public sealed class ResourceExtractionSystem : ISimulationSystem
{
    private readonly IResourceExtractionSink _sink;
    private double _totalExtractedQuantity;

    public ResourceExtractionSystem(IResourceExtractionSink? sink = null)
    {
        _sink = sink ?? NullResourceExtractionSink.Instance;
    }

    public SimulationPhase Phase => SimulationPhase.Economy;

    public ResourceExtractionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        EntityRegistry entities = context.Entities;
        int extractorCount = 0;
        int activeExtractorCount = 0;
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
                deposit.Richness;
            double requestedQuantity =
                effectiveRatePerSecond * context.TickDuration.TotalSeconds;

            ResourceDeposit updatedDeposit =
                deposit.Extract(requestedQuantity, out double extractedQuantity);

            entities.SetComponent(extractor.Deposit, updatedDeposit);

            ResourceExtractorState extractorState =
                updatedDeposit.IsDepleted
                    ? ResourceExtractorState.DepositDepleted
                    : ResourceExtractorState.Extracting;
            SetExtractorState(
                entities,
                extractorEntity,
                extractor,
                extractorState);

            if (extractedQuantity <= 0.0)
            {
                continue;
            }

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
