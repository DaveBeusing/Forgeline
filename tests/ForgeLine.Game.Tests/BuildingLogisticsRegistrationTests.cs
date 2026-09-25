using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingLogisticsRegistrationTests
{
    [Fact]
    public void CompletedStorageDepotRegistersAndRemovesNetworkNode()
    {
        var network = new LogisticsNetwork();
        var simulation = new SimulationCoordinator();
        var registration =
            new BuildingLogisticsRegistrationSystem(network);
        simulation.RegisterSystem(registration);

        EntityId depot = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depot,
            new CompletedBuilding(
                BuildingIds.StorageDepot,
                new PlayerId(1),
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                new Vector3(20.0f, 0.0f, 30.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new InventoryStorage(new InventoryId(1)));
        simulation.Entities.AddComponent(
            depot,
            new StorageDepot(
                new InventoryId(1),
                new FactionId(1)));

        simulation.AdvanceOneTick();

        Assert.True(
            network.TryGetNodeForEntity(
                depot,
                out LogisticsNodeId nodeId));
        Assert.True(network.TryGetNode(nodeId, out LogisticsNode node));
        Assert.Equal(
            LogisticsNodeKind.StorageDepot,
            node.Kind);
        Assert.True(
            node.Capabilities.HasFlag(
                LogisticsNodeCapabilities.Distribution));
        Assert.True(node.Enabled);
        Assert.Equal(1, registration.Metrics.ManagedNodeCount);

        simulation.Entities.DestroyEntity(depot);
        simulation.AdvanceOneTick();

        Assert.False(
            network.TryGetNodeForEntity(
                depot,
                out _));
        Assert.Equal(0, network.NodeCount);
        Assert.Equal(1, registration.Metrics.RemovedNodeCount);
    }

    [Fact]
    public void ExtractorAndProcessingFacilityUseDistinctNodeKinds()
    {
        var network = new LogisticsNetwork();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(
            new BuildingLogisticsRegistrationSystem(network));

        EntityId deposit =
            simulation.Entities.CreateEntity();

        EntityId extractor =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            extractor,
            new CompletedBuilding(
                BuildingIds.Extractor,
                new PlayerId(1),
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            extractor,
            new WorldTransform(
                new Vector3(5.0f, 0.0f, 5.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            extractor,
            new ResourceExtractor(
                deposit,
                ResourceIds.FerrousOre,
                10.0,
                new FactionId(1),
                outputInventory: extractor));

        EntityId processor =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            processor,
            new CompletedBuilding(
                BuildingIds.Smelter,
                new PlayerId(1),
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            processor,
            new WorldTransform(
                new Vector3(15.0f, 0.0f, 5.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            processor,
            new ProductionFacility(
                new InventoryId(2),
                new InventoryId(3),
                ProductionCapability.SteelProcessing,
                SimulationTick.Zero));

        simulation.AdvanceOneTick();

        Assert.True(
            network.TryGetNodeForEntity(
                extractor,
                out LogisticsNodeId extractorNodeId));
        Assert.True(
            network.TryGetNode(
                extractorNodeId,
                out LogisticsNode extractorNode));
        Assert.Equal(
            LogisticsNodeKind.ExtractorOutput,
            extractorNode.Kind);

        Assert.True(
            network.TryGetNodeForEntity(
                processor,
                out LogisticsNodeId processorNodeId));
        Assert.True(
            network.TryGetNode(
                processorNodeId,
                out LogisticsNode processorNode));
        Assert.Equal(
            LogisticsNodeKind.ProcessingFacility,
            processorNode.Kind);
        Assert.True(
            processorNode.Capabilities.HasFlag(
                LogisticsNodeCapabilities.Processing));
    }

    [Fact]
    public void DisabledDepotInvalidatesNetworkAvailability()
    {
        var network = new LogisticsNetwork();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(
            new BuildingLogisticsRegistrationSystem(network));

        EntityId depot = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depot,
            new CompletedBuilding(
                BuildingIds.StorageDepot,
                new PlayerId(1),
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new StorageDepot(
                new InventoryId(1),
                new FactionId(1)));

        simulation.AdvanceOneTick();

        Assert.True(
            network.TryGetNodeForEntity(
                depot,
                out LogisticsNodeId nodeId));
        LogisticsNetworkVersion enabledVersion =
            network.Version;

        simulation.Entities.SetComponent(
            depot,
            new StorageDepot(
                new InventoryId(1),
                new FactionId(1),
                StorageDepotState.Disabled));
        simulation.AdvanceOneTick();

        Assert.True(network.TryGetNode(nodeId, out LogisticsNode node));
        Assert.False(node.Enabled);
        Assert.True(
            network.Version.Value >
            enabledVersion.Value);
    }
}
