using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class LogisticsDisruptionSystemTests
{
    [Fact]
    public void NodeDisableAndRestorePersistAcrossBuildingRegistration()
    {
        var network = new LogisticsNetwork();
        var simulation = new SimulationCoordinator();
        var disruption = new LogisticsDisruptionSystem(network);

        simulation.RegisterSystem(disruption);
        simulation.RegisterSystem(
            new BuildingLogisticsRegistrationSystem(network));

        EntityId depot =
            CreateStorageDepot(
                simulation,
                new InventoryId(1),
                new Vector3(10.0f, 0.0f, 10.0f));

        simulation.AdvanceOneTick();

        Assert.True(
            network.TryGetNodeForEntity(
                depot,
                out LogisticsNodeId nodeId));
        Assert.True(
            network.TryGetNode(
                nodeId,
                out LogisticsNode initial));
        Assert.True(initial.Enabled);

        LogisticsNetworkVersion beforeDisable =
            network.Version;

        var disable =
            new SetLogisticsNodeAvailabilityCommand(
                nodeId,
                enabled: false,
                new SimulationTick(2));
        simulation.SubmitCommand(
            disable,
            new SimulationTick(2));
        simulation.AdvanceOneTick();

        Assert.True(disable.Accepted);
        Assert.True(disruption.Metrics.LastSucceeded);
        Assert.True(
            network.Version > beforeDisable);
        Assert.True(
            network.TryGetNode(
                nodeId,
                out LogisticsNode disabled));
        Assert.False(disabled.Enabled);
        Assert.True(
            simulation.Entities.TryGetComponent(
                depot,
                out LogisticsNodeAvailabilityOverride availability));
        Assert.False(availability.Enabled);

        var restore =
            new SetLogisticsNodeAvailabilityCommand(
                nodeId,
                enabled: true,
                new SimulationTick(3));
        simulation.SubmitCommand(
            restore,
            new SimulationTick(3));
        simulation.AdvanceOneTick();

        Assert.True(restore.Accepted);
        Assert.True(
            network.TryGetNode(
                nodeId,
                out LogisticsNode restored));
        Assert.True(restored.Enabled);
        Assert.True(
            simulation.Entities.TryGetComponent(
                depot,
                out availability));
        Assert.True(availability.Enabled);
        Assert.Equal(2, disruption.Metrics.AppliedChangeCount);
        Assert.Equal(0, disruption.Metrics.RejectedChangeCount);
    }

    [Fact]
    public void EdgeDisableBreaksRouteAndRestoreRecoversSameTopology()
    {
        var network = new LogisticsNetwork();
        var simulation = new SimulationCoordinator();
        var disruption = new LogisticsDisruptionSystem(network);

        simulation.RegisterSystem(disruption);

        LogisticsNodeId source =
            network.AddNode(
                new EntityId(100, 1),
                Vector3.Zero,
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities);
        LogisticsNodeId destination =
            network.AddNode(
                new EntityId(101, 1),
                new Vector3(20.0f, 0.0f, 0.0f),
                LogisticsNodeKind.StorageDepot,
                StandardCapabilities);
        LogisticsEdgeId edge =
            network.AddEdge(
                source,
                destination,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: 20.0,
                baseCost: 1.0,
                capacityPerSecond: 100.0);

        var disable =
            new SetLogisticsEdgeAvailabilityCommand(
                edge,
                enabled: false,
                new SimulationTick(1));
        simulation.SubmitCommand(
            disable,
            new SimulationTick(1));
        simulation.AdvanceOneTick();

        Assert.True(disable.Accepted);
        Assert.False(
            network.IsReachable(
                source,
                destination));
        Assert.False(
            network.FindRoute(
                source,
                destination).Succeeded);

        var restore =
            new SetLogisticsEdgeAvailabilityCommand(
                edge,
                enabled: true,
                new SimulationTick(2));
        simulation.SubmitCommand(
            restore,
            new SimulationTick(2));
        simulation.AdvanceOneTick();

        Assert.True(restore.Accepted);
        Assert.True(
            network.IsReachable(
                source,
                destination));
        Assert.True(
            network.FindRoute(
                source,
                destination).Succeeded);
        Assert.Equal(1, network.EdgeCount);
    }

    private const LogisticsNodeCapabilities StandardCapabilities =
        LogisticsNodeCapabilities.CargoSource |
        LogisticsNodeCapabilities.CargoDestination |
        LogisticsNodeCapabilities.Storage |
        LogisticsNodeCapabilities.Distribution;

    private static EntityId CreateStorageDepot(
        SimulationCoordinator simulation,
        InventoryId inventoryId,
        Vector3 position)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new CompletedBuilding(
                BuildingIds.StorageDepot,
                new PlayerId(1),
                SimulationTick.Zero));
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(inventoryId));
        simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                new FactionId(1)));
        return entity;
    }
}
