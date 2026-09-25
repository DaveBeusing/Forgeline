using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum LogisticsInfrastructureTargetKind : byte
{
    Node = 0,
    Edge = 1
}

public readonly record struct LogisticsAvailabilityChangeRequest(
    LogisticsInfrastructureTargetKind TargetKind,
    LogisticsNodeId NodeId,
    LogisticsEdgeId EdgeId,
    bool Enabled,
    SimulationTick SubmittedAtTick);

public readonly record struct LogisticsDisruptionMetrics(
    long AppliedChangeCount,
    long RejectedChangeCount,
    LogisticsInfrastructureTargetKind LastTargetKind,
    LogisticsNodeId LastNodeId,
    LogisticsEdgeId LastEdgeId,
    bool LastEnabled,
    bool LastSucceeded);

public sealed class SetLogisticsNodeAvailabilityCommand
    : ISimulationCommand
{
    public SetLogisticsNodeAvailabilityCommand(
        LogisticsNodeId nodeId,
        bool enabled,
        SimulationTick submittedAtTick)
    {
        if (!nodeId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        NodeId = nodeId;
        Enabled = enabled;
        SubmittedAtTick = submittedAtTick;
    }

    public LogisticsNodeId NodeId { get; }

    public bool Enabled { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public EntityId RequestEntity { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new LogisticsAvailabilityChangeRequest(
                LogisticsInfrastructureTargetKind.Node,
                NodeId,
                LogisticsEdgeId.None,
                Enabled,
                SubmittedAtTick));

        RequestEntity = request;
        Accepted = true;
    }
}

public sealed class SetLogisticsEdgeAvailabilityCommand
    : ISimulationCommand
{
    public SetLogisticsEdgeAvailabilityCommand(
        LogisticsEdgeId edgeId,
        bool enabled,
        SimulationTick submittedAtTick)
    {
        if (!edgeId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeId));
        }

        EdgeId = edgeId;
        Enabled = enabled;
        SubmittedAtTick = submittedAtTick;
    }

    public LogisticsEdgeId EdgeId { get; }

    public bool Enabled { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public EntityId RequestEntity { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new LogisticsAvailabilityChangeRequest(
                LogisticsInfrastructureTargetKind.Edge,
                LogisticsNodeId.None,
                EdgeId,
                Enabled,
                SubmittedAtTick));

        RequestEntity = request;
        Accepted = true;
    }
}

public sealed class LogisticsDisruptionSystem : ISimulationSystem
{
    private readonly LogisticsNetwork _network;
    private readonly List<EntityId> _requests = new();
    private long _appliedChangeCount;
    private long _rejectedChangeCount;

    public LogisticsDisruptionSystem(LogisticsNetwork network)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
    }

    public SimulationPhase Phase =>
        SimulationPhase.OrderProcessing;

    public LogisticsDisruptionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _requests.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<LogisticsAvailabilityChangeRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _requests.Add(entity);
        }

        for (int index = 0; index < _requests.Count; index++)
        {
            EntityId requestEntity = _requests[index];

            if (!context.Entities.TryGetComponent(
                    requestEntity,
                    out LogisticsAvailabilityChangeRequest request))
            {
                continue;
            }

            bool succeeded =
                request.TargetKind switch
                {
                    LogisticsInfrastructureTargetKind.Node =>
                        _network.SetNodeEnabled(
                            request.NodeId,
                            request.Enabled),
                    LogisticsInfrastructureTargetKind.Edge =>
                        _network.SetEdgeEnabled(
                            request.EdgeId,
                            request.Enabled),
                    _ => false
                };

            if (succeeded)
            {
                _appliedChangeCount++;
            }
            else
            {
                _rejectedChangeCount++;
            }

            Metrics =
                new LogisticsDisruptionMetrics(
                    _appliedChangeCount,
                    _rejectedChangeCount,
                    request.TargetKind,
                    request.NodeId,
                    request.EdgeId,
                    request.Enabled,
                    succeeded);

            context.Entities.DestroyEntity(requestEntity);
        }
    }
}
