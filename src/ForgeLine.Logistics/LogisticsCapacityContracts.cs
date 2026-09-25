using ForgeLine.Simulation;

namespace ForgeLine.Logistics;

public enum LogisticsLoadState : byte
{
    Healthy = 0,
    Busy = 1,
    Saturated = 2,
    Blocked = 3
}

public enum LogisticsCapacityBottleneckKind : byte
{
    None = 0,
    Edge = 1,
    Node = 2
}

public readonly record struct LogisticsThroughputReservationId(ulong Value)
    : IComparable<LogisticsThroughputReservationId>
{
    public static LogisticsThroughputReservationId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(LogisticsThroughputReservationId other) =>
        Value.CompareTo(other.Value);

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct LogisticsCapacityBottleneck(
    LogisticsCapacityBottleneckKind Kind,
    LogisticsEdgeId EdgeId,
    LogisticsNodeId NodeId,
    double CurrentLoad,
    double RequestedLoad,
    double EffectiveCapacity,
    double Utilization)
{
    public static LogisticsCapacityBottleneck None => default;

    public bool IsSpecified =>
        Kind != LogisticsCapacityBottleneckKind.None;
}

public readonly record struct LogisticsEdgeCapacityReadModel(
    LogisticsEdgeId EdgeId,
    LogisticsNodeId Source,
    LogisticsNodeId Destination,
    bool Enabled,
    double CapacityPerSecond,
    double WindowCapacity,
    double ScheduledLoad,
    double Utilization,
    LogisticsLoadState State);

public readonly record struct LogisticsNodeCapacityReadModel(
    LogisticsNodeId NodeId,
    LogisticsNodeKind Kind,
    bool Enabled,
    double CapacityPerSecond,
    double WindowCapacity,
    double ScheduledLoad,
    double Utilization,
    LogisticsLoadState State);

public readonly record struct LogisticsHealthSummary(
    int NodeCount,
    int EdgeCount,
    int HealthyNodeCount,
    int BusyNodeCount,
    int SaturatedNodeCount,
    int BlockedNodeCount,
    int HealthyEdgeCount,
    int BusyEdgeCount,
    int SaturatedEdgeCount,
    int BlockedEdgeCount,
    int BacklogRequestCount,
    double BacklogQuantity,
    long DeniedReservationCount)
{
    public bool HasBlockedInfrastructure =>
        BlockedNodeCount > 0 || BlockedEdgeCount > 0;

    public bool HasSaturation =>
        SaturatedNodeCount > 0 || SaturatedEdgeCount > 0;
}

public sealed class LogisticsCapacityDebugSnapshot
{
    private readonly LogisticsNodeCapacityReadModel[] _nodes;
    private readonly LogisticsEdgeCapacityReadModel[] _edges;

    internal LogisticsCapacityDebugSnapshot(
        SimulationTick capturedAtTick,
        LogisticsHealthSummary health,
        LogisticsNodeCapacityReadModel[] nodes,
        LogisticsEdgeCapacityReadModel[] edges)
    {
        CapturedAtTick = capturedAtTick;
        Health = health;
        _nodes = nodes;
        _edges = edges;
    }

    public static LogisticsCapacityDebugSnapshot Empty { get; } =
        new(
            SimulationTick.Zero,
            default,
            [],
            []);

    public SimulationTick CapturedAtTick { get; }

    public LogisticsHealthSummary Health { get; }

    public IReadOnlyList<LogisticsNodeCapacityReadModel> Nodes =>
        _nodes;

    public IReadOnlyList<LogisticsEdgeCapacityReadModel> Edges =>
        _edges;
}

public interface ILogisticsRouteCostAdjustment
{
    ulong Revision { get; }

    bool TryEvaluateTraversal(
        in LogisticsEdge edge,
        in LogisticsNode from,
        in LogisticsNode to,
        double requestedQuantity,
        out double additionalCost);
}

public static class LogisticsThroughputDefaults
{
    public static double ForNodeKind(LogisticsNodeKind kind) =>
        kind switch
        {
            LogisticsNodeKind.ExtractorOutput => 150.0,
            LogisticsNodeKind.StorageDepot => 300.0,
            LogisticsNodeKind.ProcessingFacility => 200.0,
            LogisticsNodeKind.LogisticsHub => 600.0,
            LogisticsNodeKind.SupplyDepot => 300.0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}
