using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum StrategicLogisticsNetwork : byte { Conveyor, Cargo, Supply }
public enum StrategicLogisticsState : byte { Unknown, Connected, Flowing, NoInput, Disconnected, Blocked, Congested, Damaged, Destroyed, Offline }

/// <summary>Independent observations: scheduled load is not physical throughput; buffer is not available stock.</summary>
public readonly record struct StrategicLogisticsCapacityItem(EntityId Entity, EntityId Destination,
    Vector3 Start, Vector3 End, StrategicLogisticsNetwork Network, StrategicLogisticsState State,
    double? OutputPerSecond = null, double? CapacityPerSecond = null,
    double? BufferedQuantity = null, double? BufferCapacity = null, double? ScheduledUtilization = null,
    ConveyorBlockReason? ConveyorReason = null)
{
    public bool IsLink => Destination.IsValid;
    internal int Priority => State is StrategicLogisticsState.Destroyed or StrategicLogisticsState.Disconnected or
        StrategicLogisticsState.Blocked or StrategicLogisticsState.Offline ? 0 :
        State is StrategicLogisticsState.Congested or StrategicLogisticsState.Damaged ? 1 :
        State == StrategicLogisticsState.Unknown ? 3 : 2;
}

public sealed class StrategicLogisticsCapacitySnapshot
{
    public const int MaximumItemsPerNetwork = 1024;
    public const int MaximumItems = MaximumItemsPerNetwork * 3;
    public const int MaximumScanned = 16384;
    public StrategicLogisticsCapacitySnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IEnumerable<StrategicLogisticsCapacityItem> items, int omitted = 0, bool scanLimited = false)
    {
        Session = session; Tick = tick; Player = player;
        Items = Array.AsReadOnly(items.Take(MaximumItems).ToArray());
        Omitted = omitted; ScanLimited = scanLimited;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<StrategicLogisticsCapacityItem> Items { get; }
    public int Omitted { get; }
    public bool ScanLimited { get; }
    public static StrategicLogisticsCapacitySnapshot? Resolve(PresentationSnapshot? snapshot, StrategicOverlayMode mode) =>
        RtsStrategicOverlayHudModel.IsCurrent(snapshot, mode) && snapshot!.StrategicOverlay!.LogisticsCapacity is { } data &&
        data.Session == snapshot.SessionId && data.Tick == snapshot.Tick && data.Player == snapshot.PlayerExperience!.Value.Player
            ? data : null;
}

internal static class StrategicLogisticsCapacityCapture
{
    internal static StrategicLogisticsCapacitySnapshot Capture(SimulationContext context,
        PresentationExtractionContext extraction, RenderInstance[] instances)
    {
        var scenario = extraction.Scenario;
        var entities = context.Entities;
        var positions = new Dictionary<EntityId, Vector3>();
        bool limited = false;
        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly var instance = ref instances[i];
            if (!OperationsWorldOverlay.IsOwnedVisible(instance, extraction.Player) ||
                !entities.TryGetComponent(instance.Entity, out ControllableEntity owner) || !owner.IsControllable || owner.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out CompletedBuilding building) && building.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out UnitProductionFacility factory) && factory.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out SupplyDepot depot) && depot.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out ConveyorPiece belt) && belt.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out SupplyProvider provider) && provider.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out SupplyTruck truck) && truck.Owner != extraction.Player ||
                entities.TryGetComponent(instance.Entity, out CargoTransport cargo) && cargo.Owner != extraction.Player) continue;
            var p = instance.Transform.Position;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
            if (positions.Count == StrategicLogisticsCapacitySnapshot.MaximumScanned) { limited = true; break; }
            positions[instance.Entity] = p;
        }
        var queues = new PriorityQueue<StrategicLogisticsCapacityItem, (int, int)>[3];
        for (int i = 0; i < queues.Length; i++) queues[i] = new();
        int admitted = 0;
        foreach (var pair in positions)
        {
            var entity = pair.Key;
            if (entities.TryGetComponent(entity, out ConveyorPiece piece) && !piece.Placement.IsSupport && piece.Placement.Module != ConveyorModule.Endcap)
            {
                ConveyorFlowObservation observation = default;
                bool observed = scenario.Services.ConveyorFlow.ObservationTick == context.Tick &&
                    scenario.Services.ConveyorFlow.TryGetObservation(entity, out observation);
                bool hasFlow = entities.TryGetComponent(entity, out ConveyorFlowState flow);
                ConveyorBlockReason? reason = observed ? observation.First.Reason : null;
                if (observed && observation.First.Cause.IsValid && !positions.ContainsKey(observation.First.Cause)) reason = null;
                var state = !observed || !hasFlow ? StrategicLogisticsState.Unknown : State(flow.Status);
                if (reason is null) state = StrategicLogisticsState.Unknown;
                Add(new(entity, default, pair.Value, pair.Value, StrategicLogisticsNetwork.Conveyor, state,
                    observed && hasFlow ? flow.OutputPerSecond : null, hasFlow ? scenario.Services.ConveyorFlow.EffectiveThroughput : null,
                    hasFlow && scenario.Inventories.Contains(flow.Buffer) ? scenario.Inventories.GetTotalQuantity(flow.Buffer) : null,
                    hasFlow ? flow.Capacity : null, ConveyorReason: reason));
                if (!observed || !hasFlow) continue;
                Link(flow.OutputA, flow.OutputAPerSecond);
                Link(flow.OutputB, flow.OutputBPerSecond);
                void Link(EntityId target, double rate)
                {
                    if (positions.TryGetValue(target, out var end))
                        Add(new(entity, target, pair.Value, end, StrategicLogisticsNetwork.Conveyor, state,
                            rate, scenario.Services.ConveyorFlow.EffectiveThroughput, ConveyorReason: reason));
                }
            }
            // Field service is an accepted provider relation, not a predicted road path or tactical readiness marker.
            if (entities.TryGetComponent(entity, out SupplyProvider _) || entities.TryGetComponent(entity, out SupplyTruck _))
                Add(new(entity, default, pair.Value, pair.Value, StrategicLogisticsNetwork.Supply, StrategicLogisticsState.Unknown));
            if (entities.TryGetComponent(entity, out ResupplyOrder order) && positions.TryGetValue(order.Provider, out var start))
                Add(new(order.Provider, entity, start, pair.Value, StrategicLogisticsNetwork.Supply, StrategicLogisticsState.Connected));
        }
        var nodes = new Dictionary<LogisticsNodeId, LogisticsNode>();
        foreach (var pair in positions)
            if (scenario.Logistics.TryGetNodeForEntity(pair.Key, out var id) && scenario.Logistics.TryGetNode(id, out var node)) nodes[id] = node;
        var capacity = scenario.AutomatedDistribution.LastCapacityDebugSnapshot;
        var nodeLoads = new Dictionary<LogisticsNodeId, LogisticsNodeCapacityReadModel>();
        var edgeLoads = new Dictionary<LogisticsEdgeId, LogisticsEdgeCapacityReadModel>();
        if (capacity.CapturedAtTick == context.Tick)
        {
            for (int i = 0; i < Math.Min(capacity.Nodes.Count, StrategicLogisticsCapacitySnapshot.MaximumScanned); i++)
                if (nodes.ContainsKey(capacity.Nodes[i].NodeId)) nodeLoads[capacity.Nodes[i].NodeId] = capacity.Nodes[i];
            for (int i = 0; i < Math.Min(capacity.Edges.Count, StrategicLogisticsCapacitySnapshot.MaximumScanned); i++)
            { var e = capacity.Edges[i]; if (nodes.ContainsKey(e.Source) && nodes.ContainsKey(e.Destination)) edgeLoads[e.EdgeId] = e; }
        }
        foreach (var node in nodes.Values)
        {
            double? load = nodeLoads.TryGetValue(node.Id, out var n) ? n.Utilization : null;
            Add(new(node.Entity, default, positions[node.Entity], positions[node.Entity], StrategicLogisticsNetwork.Cargo,
                !node.Enabled ? StrategicLogisticsState.Offline : LoadState(load), CapacityPerSecond: node.ThroughputCapacityPerSecond, ScheduledUtilization: load));
        }
        var edges = scenario.Logistics.GetEdges();
        int authorizedEdges = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge.Mode != LogisticsTransportMode.GroundRoad || !nodes.TryGetValue(edge.Source, out var source) || !nodes.TryGetValue(edge.Destination, out var target)) continue;
            if (authorizedEdges++ == StrategicLogisticsCapacitySnapshot.MaximumScanned) { limited = true; break; }
            double? load = edgeLoads.TryGetValue(edge.Id, out var e) ? e.Utilization : null;
            Add(new(source.Entity, target.Entity, positions[source.Entity], positions[target.Entity], StrategicLogisticsNetwork.Cargo,
                !edge.Enabled ? StrategicLogisticsState.Offline : LoadState(load), CapacityPerSecond: edge.CapacityPerSecond, ScheduledUtilization: load));
        }
        var rows = new List<StrategicLogisticsCapacityItem>();
        foreach (var queue in queues)
        {
            var group = new List<StrategicLogisticsCapacityItem>(queue.Count);
            while (queue.TryDequeue(out var item, out _)) group.Add(item);
            group.Reverse(); rows.AddRange(group);
        }
        return new(scenario.Simulation.SessionId, context.Tick, extraction.Player, rows, admitted - rows.Count, limited);

        void Add(StrategicLogisticsCapacityItem item)
        {
            int index = admitted++;
            var queue = queues[(int)item.Network];
            var priority = (-item.Priority, -index);
            if (queue.Count < StrategicLogisticsCapacitySnapshot.MaximumItemsPerNetwork) queue.Enqueue(item, priority);
            else queue.EnqueueDequeue(item, priority);
        }
    }

    private static StrategicLogisticsState LoadState(double? utilization) => utilization is null ? StrategicLogisticsState.Unknown :
        utilization >= 1 ? StrategicLogisticsState.Congested : StrategicLogisticsState.Connected;
    private static StrategicLogisticsState State(ConveyorLinkStatus status) => status switch
    {
        ConveyorLinkStatus.Flowing => StrategicLogisticsState.Flowing, ConveyorLinkStatus.NoInput => StrategicLogisticsState.NoInput,
        ConveyorLinkStatus.Disconnected => StrategicLogisticsState.Disconnected, ConveyorLinkStatus.Congested => StrategicLogisticsState.Congested,
        ConveyorLinkStatus.Blocked => StrategicLogisticsState.Blocked, ConveyorLinkStatus.Destroyed => StrategicLogisticsState.Destroyed,
        ConveyorLinkStatus.Offline => StrategicLogisticsState.Offline, ConveyorLinkStatus.Damaged or ConveyorLinkStatus.Critical => StrategicLogisticsState.Damaged,
        _ => StrategicLogisticsState.Unknown
    };
}
