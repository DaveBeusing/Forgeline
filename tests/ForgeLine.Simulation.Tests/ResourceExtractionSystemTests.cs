using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class ResourceExtractionSystemTests
{
    [Fact]
    public void InitialCatalogExposesStableVerticalSliceResources()
    {
        ResourceCatalog catalog = InitialResourceDefinitions.CreateCatalog();

        Assert.Equal(3, catalog.Count);
        Assert.Equal("resource.ferrous_ore", catalog[ResourceIds.FerrousOre].Key);
        Assert.Equal("resource.volatiles", catalog[ResourceIds.Volatiless].Key);
        Assert.Equal("resource.silicates", catalog[ResourceIds.Silicates].Key);
        Assert.True(catalog.TryResolve("resource.ferrous_ore", out ResourceId resolved));
        Assert.Equal(ResourceIds.FerrousOre, resolved);
    }

    [Fact]
    public void ExtractsAtFixedTickRateUsingDepositAndExtractorLimits()
    {
        var sink = new RecordingExtractionSink();
        var system = new ResourceExtractionSystem(sink);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.FerrousOre,
            totalQuantity: 100.0,
            baseRate: 10.0);
        EntityId extractorEntity = AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.FerrousOre,
            maximumRate: 20.0);

        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);
        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);

        Assert.InRange(deposit.RemainingQuantity, 99.499999999, 99.500000001);
        Assert.Equal(ResourceDepositState.Available, deposit.State);
        Assert.Equal(ResourceExtractorState.Extracting, extractor.State);
        Assert.Single(sink.Results);
        Assert.InRange(sink.Results[0].Quantity, 0.499999999, 0.500000001);
        Assert.Equal(1, system.Metrics.ActiveExtractorCount);
        Assert.InRange(
            system.Metrics.LastTickExtractionRatePerSecond,
            9.99999999,
            10.00000001);
    }

    [Fact]
    public void RichnessScalesEffectiveExtractionRate()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.Volatiles,
            totalQuantity: 100.0,
            baseRate: 8.0,
            richness: 1.5);
        AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Volatiles,
            maximumRate: 8.0);

        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);
        Assert.InRange(deposit.RemainingQuantity, 99.399999999, 99.600000001);
    }

    [Fact]
    public void ExactDepletionClampsAtZeroAndStopsFurtherExtraction()
    {
        var sink = new RecordingExtractionSink();
        var system = new ResourceExtractionSystem(sink);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.Silicates,
            totalQuantity: 0.5,
            baseRate: 10.0);
        EntityId extractorEntity = AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Silicates,
            maximumRate: 10.0);

        simulation.AdvanceOneTick();
        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);
        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);

        Assert.Equal(0.0, deposit.RemainingQuantity);
        Assert.Equal(ResourceDepositState.Depleted, deposit.State);
        Assert.Equal(ResourceExtractorState.DepositDepleted, extractor.State);
        Assert.Single(sink.Results);
        Assert.Equal(0.5, sink.Results[0].Quantity);
        Assert.Equal(1, system.Metrics.DepletedDepositCount);
    }

    [Fact]
    public void AlreadyDepletedDepositProducesNoOutput()
    {
        var sink = new RecordingExtractionSink();
        var system = new ResourceExtractionSystem(sink);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depositEntity,
            ResourceDeposit.Restore(
                ResourceIds.FerrousOre,
                CreateBounds(0.0f),
                totalQuantity: 100.0,
                remainingQuantity: 0.0,
                baseExtractionRatePerSecond: 10.0));
        EntityId extractorEntity = AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.FerrousOre,
            maximumRate: 10.0);

        simulation.AdvanceOneTick();

        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);

        Assert.Equal(ResourceExtractorState.DepositDepleted, extractor.State);
        Assert.Empty(sink.Results);
        Assert.Equal(0.0, system.Metrics.TotalExtractedQuantity);
    }

    [Fact]
    public void StaleDepositReferenceIsRejectedSafely()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId staleDeposit = AddDeposit(
            simulation,
            ResourceIds.FerrousOre,
            totalQuantity: 100.0,
            baseRate: 10.0);
        Assert.True(simulation.Entities.DestroyEntity(staleDeposit));

        EntityId extractorEntity = AddExtractor(
            simulation,
            staleDeposit,
            ResourceIds.FerrousOre,
            maximumRate: 10.0);

        simulation.AdvanceOneTick();

        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);
        Assert.Equal(
            ResourceExtractorState.InvalidDepositReference,
            extractor.State);
    }

    [Fact]
    public void ResourceTypeMismatchPreventsExtraction()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.FerrousOre,
            totalQuantity: 100.0,
            baseRate: 10.0);
        EntityId extractorEntity = AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Volatiles,
            maximumRate: 10.0);

        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);
        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);

        Assert.Equal(100.0, deposit.RemainingQuantity);
        Assert.Equal(ResourceExtractorState.ResourceMismatch, extractor.State);
    }

    [Fact]
    public void OwnedDepositRejectsDifferentFaction()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.Volatiles,
            totalQuantity: 100.0,
            baseRate: 8.0,
            owner: new FactionId(7));
        EntityId extractorEntity = AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Volatiles,
            maximumRate: 8.0,
            owner: new FactionId(9));

        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);
        ResourceExtractor extractor =
            simulation.Entities.GetComponent<ResourceExtractor>(extractorEntity);

        Assert.Equal(100.0, deposit.RemainingQuantity);
        Assert.Equal(ResourceExtractorState.OwnershipMismatch, extractor.State);
    }

    [Fact]
    public void MultipleExtractorsNeverOverdrawSharedDeposit()
    {
        var sink = new RecordingExtractionSink();
        var system = new ResourceExtractionSystem(sink);
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.Silicates,
            totalQuantity: 0.25,
            baseRate: 10.0);
        AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Silicates,
            maximumRate: 10.0);
        AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Silicates,
            maximumRate: 10.0);

        simulation.AdvanceOneTick();

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);

        Assert.Equal(0.0, deposit.RemainingQuantity);
        Assert.Equal(ResourceDepositState.Depleted, deposit.State);
        Assert.Single(sink.Results);
        Assert.Equal(0.25, sink.Results[0].Quantity);
        Assert.Equal(0.25, system.Metrics.TotalExtractedQuantity);
    }

    [Fact]
    public void RepeatedHeadlessFixturesProduceIdenticalState()
    {
        (ResourceDeposit Deposit, ResourceExtractionMetrics Metrics) first =
            RunDeterministicFixture();
        (ResourceDeposit Deposit, ResourceExtractionMetrics Metrics) second =
            RunDeterministicFixture();

        Assert.Equal(first.Deposit, second.Deposit);
        Assert.Equal(first.Metrics, second.Metrics);
    }

    [Fact]
    public void HeadlessDepletionFixtureRunsWithoutPresentationDependencies()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.FerrousOre,
            totalQuantity: 5.0,
            baseRate: 10.0);
        AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.FerrousOre,
            maximumRate: 10.0);

        ulong executed = simulation.RunTicks(
            1_000,
            TestContext.Current.CancellationToken);

        ResourceDeposit deposit =
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity);

        Assert.Equal(1_000UL, executed);
        Assert.Equal(ResourceDepositState.Depleted, deposit.State);
        Assert.Equal(0.0, deposit.RemainingQuantity);
        Assert.Equal(5.0, system.Metrics.TotalExtractedQuantity);
    }

    private static (ResourceDeposit Deposit, ResourceExtractionMetrics Metrics)
        RunDeterministicFixture()
    {
        var system = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator(seed: 0xF047EUL);
        simulation.RegisterSystem(system);

        EntityId depositEntity = AddDeposit(
            simulation,
            ResourceIds.Volatiles,
            totalQuantity: 250.0,
            baseRate: 8.0,
            richness: 1.25,
            owner: new FactionId(3));
        AddExtractor(
            simulation,
            depositEntity,
            ResourceIds.Volatiles,
            maximumRate: 6.0,
            owner: new FactionId(3));

        simulation.RunTicks(200, TestContext.Current.CancellationToken);

        return (
            simulation.Entities.GetComponent<ResourceDeposit>(depositEntity),
            system.Metrics);
    }

    private static EntityId AddDeposit(
        SimulationCoordinator simulation,
        ResourceId resourceId,
        double totalQuantity,
        double baseRate,
        double richness = 1.0,
        FactionId owner = default)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new ResourceDeposit(
                resourceId,
                CreateBounds(entity.Index),
                totalQuantity,
                baseRate,
                richness,
                owner));
        return entity;
    }

    private static EntityId AddExtractor(
        SimulationCoordinator simulation,
        EntityId deposit,
        ResourceId resourceId,
        double maximumRate,
        FactionId owner = default)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new ResourceExtractor(
                deposit,
                resourceId,
                maximumRate,
                owner));
        return entity;
    }

    private static AxisAlignedBounds CreateBounds(uint offset)
    {
        float position = offset * 4.0f;
        return new AxisAlignedBounds(
            new Vector3(position, 0.0f, position),
            new Vector3(position + 2.0f, 1.0f, position + 2.0f));
    }

    private sealed class RecordingExtractionSink : IResourceExtractionSink
    {
        public List<ResourceExtractionResult> Results { get; } = new();

        public void OnExtracted(in ResourceExtractionResult result)
        {
            Results.Add(result);
        }
    }
}
