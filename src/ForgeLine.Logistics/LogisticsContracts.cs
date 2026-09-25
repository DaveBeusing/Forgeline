using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Logistics;

public readonly record struct LogisticsNodeId(ulong Value)
    : IComparable<LogisticsNodeId>
{
    public static LogisticsNodeId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(LogisticsNodeId other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(
        LogisticsNodeId left,
        LogisticsNodeId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        LogisticsNodeId left,
        LogisticsNodeId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        LogisticsNodeId left,
        LogisticsNodeId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        LogisticsNodeId left,
        LogisticsNodeId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct LogisticsEdgeId(ulong Value)
    : IComparable<LogisticsEdgeId>
{
    public static LogisticsEdgeId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(LogisticsEdgeId other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(
        LogisticsEdgeId left,
        LogisticsEdgeId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        LogisticsEdgeId left,
        LogisticsEdgeId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        LogisticsEdgeId left,
        LogisticsEdgeId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        LogisticsEdgeId left,
        LogisticsEdgeId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct LogisticsNetworkVersion(ulong Value)
    : IComparable<LogisticsNetworkVersion>
{
    public static LogisticsNetworkVersion Initial { get; } = new(1);

    public bool IsValid => Value != 0;

    public int CompareTo(LogisticsNetworkVersion other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(
        LogisticsNetworkVersion left,
        LogisticsNetworkVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        LogisticsNetworkVersion left,
        LogisticsNetworkVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        LogisticsNetworkVersion left,
        LogisticsNetworkVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        LogisticsNetworkVersion left,
        LogisticsNetworkVersion right) =>
        left.CompareTo(right) >= 0;
}

public enum LogisticsNodeKind : byte
{
    ExtractorOutput = 1,
    StorageDepot = 2,
    ProcessingFacility = 3,
    LogisticsHub = 4,
    SupplyDepot = 5
}

[Flags]
public enum LogisticsNodeCapabilities : ushort
{
    None = 0,
    CargoSource = 1 << 0,
    CargoDestination = 1 << 1,
    Storage = 1 << 2,
    Processing = 1 << 3,
    Distribution = 1 << 4,
    Supply = 1 << 5
}

public enum LogisticsTransportMode : byte
{
    GroundRoad = 1,
    Rail = 2,
    Pipeline = 3,
    Drone = 4
}

[Flags]
public enum LogisticsTransportModeMask : byte
{
    None = 0,
    GroundRoad = 1 << 0,
    Rail = 1 << 1,
    Pipeline = 1 << 2,
    Drone = 1 << 3,
    All = GroundRoad | Rail | Pipeline | Drone
}

public static class LogisticsTransportModes
{
    public static LogisticsTransportModeMask ToMask(
        LogisticsTransportMode mode) =>
        mode switch
        {
            LogisticsTransportMode.GroundRoad =>
                LogisticsTransportModeMask.GroundRoad,
            LogisticsTransportMode.Rail =>
                LogisticsTransportModeMask.Rail,
            LogisticsTransportMode.Pipeline =>
                LogisticsTransportModeMask.Pipeline,
            LogisticsTransportMode.Drone =>
                LogisticsTransportModeMask.Drone,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}

public readonly record struct LogisticsNode
{
    public LogisticsNode(
        LogisticsNodeId id,
        EntityId entity,
        Vector3 worldPosition,
        LogisticsNodeKind kind,
        LogisticsNodeCapabilities capabilities,
        bool enabled = true)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (!entity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(entity));
        }

        if (!IsFinite(worldPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(worldPosition));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        const LogisticsNodeCapabilities supported =
            LogisticsNodeCapabilities.CargoSource |
            LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Storage |
            LogisticsNodeCapabilities.Processing |
            LogisticsNodeCapabilities.Distribution |
            LogisticsNodeCapabilities.Supply;

        if (capabilities == LogisticsNodeCapabilities.None ||
            (capabilities & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        Id = id;
        Entity = entity;
        WorldPosition = worldPosition;
        Kind = kind;
        Capabilities = capabilities;
        Enabled = enabled;
    }

    public LogisticsNodeId Id { get; }

    public EntityId Entity { get; }

    public Vector3 WorldPosition { get; }

    public LogisticsNodeKind Kind { get; }

    public LogisticsNodeCapabilities Capabilities { get; }

    public bool Enabled { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct LogisticsEdge
{
    public LogisticsEdge(
        LogisticsEdgeId id,
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsTransportMode mode,
        double distanceMeters,
        double baseCost,
        double capacityPerSecond,
        bool bidirectional = true,
        bool enabled = true,
        double congestionPenalty = 0.0,
        double threatPenalty = 0.0)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (!source.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (!destination.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (source == destination)
        {
            throw new ArgumentException(
                "Logistics edges must connect distinct nodes.",
                nameof(destination));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ValidatePositiveFinite(distanceMeters, nameof(distanceMeters));
        ValidateNonNegativeFinite(baseCost, nameof(baseCost));
        ValidatePositiveFinite(capacityPerSecond, nameof(capacityPerSecond));
        ValidateNonNegativeFinite(
            congestionPenalty,
            nameof(congestionPenalty));
        ValidateNonNegativeFinite(
            threatPenalty,
            nameof(threatPenalty));

        Id = id;
        Source = source;
        Destination = destination;
        Mode = mode;
        DistanceMeters = distanceMeters;
        BaseCost = baseCost;
        CapacityPerSecond = capacityPerSecond;
        Bidirectional = bidirectional;
        Enabled = enabled;
        CongestionPenalty = congestionPenalty;
        ThreatPenalty = threatPenalty;
    }

    public LogisticsEdgeId Id { get; }

    public LogisticsNodeId Source { get; }

    public LogisticsNodeId Destination { get; }

    public LogisticsTransportMode Mode { get; }

    public double DistanceMeters { get; }

    public double BaseCost { get; }

    public double CapacityPerSecond { get; }

    public bool Bidirectional { get; }

    public bool Enabled { get; }

    public double CongestionPenalty { get; }

    public double ThreatPenalty { get; }

    public bool CanTraverseFrom(LogisticsNodeId node) =>
        node == Source ||
        (Bidirectional && node == Destination);

    public LogisticsNodeId GetOther(LogisticsNodeId node)
    {
        if (node == Source)
        {
            return Destination;
        }

        if (Bidirectional && node == Destination)
        {
            return Source;
        }

        throw new ArgumentException(
            $"Node {node} cannot traverse logistics edge {Id}.",
            nameof(node));
    }

    private static void ValidatePositiveFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public readonly record struct LogisticsRouteCostPolicy
{
    public LogisticsRouteCostPolicy(
        LogisticsTransportModeMask allowedModes,
        double baseCostWeight = 1.0,
        double distanceWeight = 0.0,
        double congestionWeight = 0.0,
        double threatWeight = 0.0,
        double minimumCapacityPerSecond = 0.0)
    {
        if (allowedModes == LogisticsTransportModeMask.None ||
            (allowedModes & ~LogisticsTransportModeMask.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedModes));
        }

        ValidateNonNegativeFinite(
            baseCostWeight,
            nameof(baseCostWeight));
        ValidateNonNegativeFinite(
            distanceWeight,
            nameof(distanceWeight));
        ValidateNonNegativeFinite(
            congestionWeight,
            nameof(congestionWeight));
        ValidateNonNegativeFinite(
            threatWeight,
            nameof(threatWeight));
        ValidateNonNegativeFinite(
            minimumCapacityPerSecond,
            nameof(minimumCapacityPerSecond));

        if (baseCostWeight == 0.0 &&
            distanceWeight == 0.0 &&
            congestionWeight == 0.0 &&
            threatWeight == 0.0)
        {
            throw new ArgumentException(
                "At least one route-cost weight must be greater than zero.");
        }

        AllowedModes = allowedModes;
        BaseCostWeight = baseCostWeight;
        DistanceWeight = distanceWeight;
        CongestionWeight = congestionWeight;
        ThreatWeight = threatWeight;
        MinimumCapacityPerSecond = minimumCapacityPerSecond;
    }

    public static LogisticsRouteCostPolicy Default { get; } =
        new(LogisticsTransportModeMask.GroundRoad);

    public LogisticsTransportModeMask AllowedModes { get; }

    public double BaseCostWeight { get; }

    public double DistanceWeight { get; }

    public double CongestionWeight { get; }

    public double ThreatWeight { get; }

    public double MinimumCapacityPerSecond { get; }

    public bool Allows(LogisticsEdge edge) =>
        edge.Enabled &&
        (AllowedModes & LogisticsTransportModes.ToMask(edge.Mode)) != 0 &&
        edge.CapacityPerSecond >= MinimumCapacityPerSecond;

    public double Evaluate(LogisticsEdge edge)
    {
        if (!Allows(edge))
        {
            return double.PositiveInfinity;
        }

        return
            edge.BaseCost * BaseCostWeight +
            edge.DistanceMeters * DistanceWeight +
            edge.CongestionPenalty * CongestionWeight +
            edge.ThreatPenalty * ThreatWeight;
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public enum LogisticsRouteFailureReason : byte
{
    None = 0,
    SourceNodeNotFound = 1,
    DestinationNodeNotFound = 2,
    SourceNodeDisabled = 3,
    DestinationNodeDisabled = 4,
    NoRoute = 5
}

public readonly record struct LogisticsRouteSegment(
    LogisticsEdgeId EdgeId,
    LogisticsNodeId From,
    LogisticsNodeId To,
    LogisticsTransportMode Mode,
    double DistanceMeters,
    double Cost,
    double CapacityPerSecond);

public readonly record struct LogisticsRouteDiagnostics(
    int ExpandedNodeCount,
    int ConsideredEdgeCount,
    bool CacheHit);

public sealed class LogisticsRoute
{
    private readonly LogisticsRouteSegment[] _segments;

    internal LogisticsRoute(
        LogisticsNetworkVersion version,
        LogisticsNodeId source,
        LogisticsNodeId destination,
        LogisticsRouteCostPolicy policy,
        LogisticsRouteSegment[] segments,
        double totalCost,
        double totalDistanceMeters)
    {
        Version = version;
        Source = source;
        Destination = destination;
        Policy = policy;
        _segments = segments;
        TotalCost = totalCost;
        TotalDistanceMeters = totalDistanceMeters;
    }

    public LogisticsNetworkVersion Version { get; }

    public LogisticsNodeId Source { get; }

    public LogisticsNodeId Destination { get; }

    public LogisticsRouteCostPolicy Policy { get; }

    public IReadOnlyList<LogisticsRouteSegment> Segments =>
        _segments;

    public double TotalCost { get; }

    public double TotalDistanceMeters { get; }
}

public readonly record struct LogisticsRouteSearchResult(
    LogisticsNetworkVersion Version,
    LogisticsRouteFailureReason FailureReason,
    LogisticsRoute? Route,
    LogisticsRouteDiagnostics Diagnostics)
{
    public bool Succeeded =>
        FailureReason == LogisticsRouteFailureReason.None &&
        Route is not null;
}

public readonly record struct LogisticsNetworkMetrics(
    int NodeCount,
    int EnabledNodeCount,
    int EdgeCount,
    int EnabledEdgeCount,
    int ConnectedComponentCount,
    long RouteRequestCount,
    long FailedRouteCount,
    long RouteCacheHitCount,
    double LastRouteCost,
    double LastRouteLengthMeters);
