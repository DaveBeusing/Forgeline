using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct PrototypeRoadAccessMetrics(
    int ConnectedBuildings,
    long AddedConnections,
    long RemovedConnections);

public sealed class PrototypeRoadAccessSystem : ISimulationSystem
{
    private readonly LogisticsNetwork _network;
    private readonly IReadOnlyDictionary<string, LogisticsNodeId> _roadNodes;
    private readonly Dictionary<EntityId, LogisticsEdgeId> _buildingEdges = new();
    private readonly List<EntityId> _stale = new();
    private readonly double _maximumAccessDistanceMeters;
    private long _addedConnections;
    private long _removedConnections;

    public PrototypeRoadAccessSystem(
        LogisticsNetwork network,
        IReadOnlyDictionary<string, LogisticsNodeId> roadNodes,
        double maximumAccessDistanceMeters = 700.0)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
        _roadNodes = roadNodes ??
            throw new ArgumentNullException(nameof(roadNodes));

        if (!double.IsFinite(maximumAccessDistanceMeters) ||
            maximumAccessDistanceMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAccessDistanceMeters));
        }

        _maximumAccessDistanceMeters =
            maximumAccessDistanceMeters;
    }

    public SimulationPhase Phase =>
        SimulationPhase.EntityLifecycle;

    public PrototypeRoadAccessMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ConnectEligibleBuildings(context);
        RemoveStaleConnections(context);

        Metrics =
            new PrototypeRoadAccessMetrics(
                _buildingEdges.Count,
                _addedConnections,
                _removedConnections);
    }

    private void ConnectEligibleBuildings(
        SimulationContext context)
    {
        foreach (EntityId entity in
                 context.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (_buildingEdges.ContainsKey(entity) ||
                !_network.TryGetNodeForEntity(
                    entity,
                    out LogisticsNodeId buildingNode) ||
                !_network.TryGetNode(
                    buildingNode,
                    out LogisticsNode node) ||
                !TryFindNearestRoadNode(
                    node.WorldPosition,
                    out LogisticsNodeId roadNode,
                    out double distance))
            {
                continue;
            }

            LogisticsEdgeId edge =
                _network.AddEdge(
                    buildingNode,
                    roadNode,
                    LogisticsTransportMode.GroundRoad,
                    distanceMeters: Math.Max(1.0, distance),
                    baseCost: Math.Max(0.1, distance / 250.0),
                    capacityPerSecond: 120.0,
                    bidirectional: true,
                    enabled: true);

            _buildingEdges.Add(
                entity,
                edge);
            _addedConnections++;
        }
    }

    private bool TryFindNearestRoadNode(
        Vector3 position,
        out LogisticsNodeId selected,
        out double distance)
    {
        selected = LogisticsNodeId.None;
        distance = double.PositiveInfinity;

        foreach (LogisticsNodeId candidateId in
                 _roadNodes.Values.OrderBy(
                     static nodeId => nodeId))
        {
            if (!_network.TryGetNode(
                    candidateId,
                    out LogisticsNode candidate) ||
                !candidate.Enabled)
            {
                continue;
            }

            double candidateDistance =
                HorizontalDistance(
                    position,
                    candidate.WorldPosition);

            if (candidateDistance >
                _maximumAccessDistanceMeters)
            {
                continue;
            }

            if (!selected.IsSpecified ||
                candidateDistance < distance ||
                (candidateDistance == distance &&
                 candidateId < selected))
            {
                selected = candidateId;
                distance = candidateDistance;
            }
        }

        return selected.IsSpecified;
    }

    private void RemoveStaleConnections(
        SimulationContext context)
    {
        _stale.Clear();

        foreach (var pair in _buildingEdges)
        {
            if (!context.Entities.IsAlive(pair.Key) ||
                !_network.TryGetEdge(
                    pair.Value,
                    out _))
            {
                _stale.Add(pair.Key);
            }
        }

        for (int index = 0;
             index < _stale.Count;
             index++)
        {
            EntityId entity = _stale[index];

            if (_buildingEdges.TryGetValue(
                    entity,
                    out LogisticsEdgeId edge))
            {
                _network.RemoveEdge(edge);
            }

            _buildingEdges.Remove(entity);
            _removedConnections++;
        }
    }

    private static double HorizontalDistance(
        Vector3 left,
        Vector3 right)
    {
        double x =
            left.X - right.X;
        double z =
            left.Z - right.Z;
        return Math.Sqrt(
            x * x +
            z * z);
    }
}
