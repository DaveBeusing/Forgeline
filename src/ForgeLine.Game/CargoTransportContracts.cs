using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum CargoTransportLifecycleState : byte
{
    Idle = 0,
    ToOrigin = 1,
    Loading = 2,
    ToDestination = 3,
    Unloading = 4,
    Waiting = 5,
    Failed = 6
}

public enum CargoTransportWaitReason : byte
{
    None = 0,
    OriginUnavailable = 1,
    OriginResourceUnavailable = 2,
    RouteUnavailable = 3,
    RouteInvalidated = 4,
    DestinationUnavailable = 5,
    DestinationCapacity = 6
}

public enum CargoTransportFailureReason : byte
{
    None = 0,
    InvalidTransport = 1,
    InvalidOrigin = 2,
    InvalidDestination = 3,
    InvalidRouteAnchor = 4,
    CargoInventoryUnavailable = 5,
    SourceInventoryUnavailable = 6,
    DestinationInventoryUnavailable = 7,
    CargoCapacityInsufficient = 8,
    TransferFailed = 9,
    NavigationFailed = 10
}

public enum CargoPartialLoadPolicy : byte
{
    AllowPartial = 0,
    RequireRequestedQuantity = 1
}

public sealed class CargoTruckDefinition
{
    public CargoTruckDefinition(
        string key,
        double cargoCapacity,
        GroundMovement movement,
        System.Numerics.Vector3 visualScale,
        uint visualId = 1)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                "Cargo truck definitions require a stable key.",
                nameof(key));
        }

        if (!double.IsFinite(cargoCapacity) ||
            cargoCapacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cargoCapacity));
        }

        if (!IsFinitePositive(visualScale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(visualScale));
        }

        Key = key;
        CargoCapacity = cargoCapacity;
        Movement = movement;
        VisualScale = visualScale;
        VisualId = visualId;
    }

    public string Key { get; }

    public double CargoCapacity { get; }

    public GroundMovement Movement { get; }

    public System.Numerics.Vector3 VisualScale { get; }

    public uint VisualId { get; }

    public static CargoTruckDefinition Default { get; } =
        new(
            "unit.cargo_truck",
            cargoCapacity: 100.0,
            movement: new GroundMovement(
                maximumSpeed: 14.0f,
                acceleration: 7.0f,
                deceleration: 10.0f,
                turnRateRadiansPerSecond: 2.5f,
                radius: 1.8f,
                stopRadius: 1.0f,
                separationRadius: 5.0f,
                obstacleLookAhead: 8.0f,
                maximumSlopeDegrees: 25.0f,
                heightOffset: 1.0f),
            visualScale: new System.Numerics.Vector3(
                3.0f,
                2.0f,
                6.0f));

    private static bool IsFinitePositive(
        System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        value.X > 0.0f &&
        value.Y > 0.0f &&
        value.Z > 0.0f;
}

public readonly record struct CargoTransport
{
    public CargoTransport(
        InventoryId cargoInventory,
        double capacity,
        PlayerId owner)
    {
        if (!cargoInventory.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo transports require a valid cargo inventory.",
                nameof(cargoInventory));
        }

        if (!double.IsFinite(capacity) ||
            capacity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo transports require a valid owner.",
                nameof(owner));
        }

        CargoInventory = cargoInventory;
        Capacity = capacity;
        Owner = owner;
    }

    public InventoryId CargoInventory { get; }

    public double Capacity { get; }

    public PlayerId Owner { get; }
}

public readonly record struct CargoTransportOrder
{
    public CargoTransportOrder(
        LogisticsNodeId origin,
        LogisticsNodeId destination,
        ResourceId resourceId,
        double requestedQuantity,
        SimulationTick submittedAtTick,
        CargoPartialLoadPolicy partialLoadPolicy =
            CargoPartialLoadPolicy.AllowPartial)
    {
        if (!origin.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo transport orders require a valid origin node.",
                nameof(origin));
        }

        if (!destination.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo transport orders require a valid destination node.",
                nameof(destination));
        }

        if (origin == destination)
        {
            throw new ArgumentException(
                "Cargo transport origin and destination must be distinct.",
                nameof(destination));
        }

        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo transport orders require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(requestedQuantity) ||
            requestedQuantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity));
        }

        if (!Enum.IsDefined(partialLoadPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(partialLoadPolicy));
        }

        Origin = origin;
        Destination = destination;
        ResourceId = resourceId;
        RequestedQuantity = requestedQuantity;
        SubmittedAtTick = submittedAtTick;
        PartialLoadPolicy = partialLoadPolicy;
    }

    public LogisticsNodeId Origin { get; }

    public LogisticsNodeId Destination { get; }

    public ResourceId ResourceId { get; }

    public double RequestedQuantity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public CargoPartialLoadPolicy PartialLoadPolicy { get; }
}

public readonly record struct CargoTransportRuntimeState(
    CargoTransportLifecycleState Lifecycle,
    CargoTransportWaitReason WaitReason,
    CargoTransportFailureReason FailureReason,
    LogisticsNodeId AnchorNode,
    LogisticsNetworkVersion ObservedNetworkVersion,
    double LoadedQuantity,
    double DeliveredQuantity,
    SimulationTick StateChangedAtTick)
{
    public static CargoTransportRuntimeState Idle =>
        new(
            CargoTransportLifecycleState.Idle,
            CargoTransportWaitReason.None,
            CargoTransportFailureReason.None,
            LogisticsNodeId.None,
            default,
            0.0,
            0.0,
            SimulationTick.Zero);
}

public readonly record struct CargoTransportRouteState(
    LogisticsRoute? Route,
    int NextSegmentIndex);

public readonly record struct CargoTransportMovementTarget(
    LogisticsNodeId NodeId,
    System.Numerics.Vector3 WorldPosition,
    SimulationTick IssuedAtTick);

public readonly record struct CargoTransportMetrics(
    int TransportCount,
    int ActiveTransportCount,
    int WaitingTransportCount,
    int FailedTransportCount,
    double CargoInTransitQuantity,
    double DeliveredQuantity,
    double LostQuantity,
    long CompletedOrderCount,
    long RerouteCount,
    long RouteFailureCount);

public readonly record struct CargoTransportReadModel(
    EntityId Entity,
    CargoTransportLifecycleState Lifecycle,
    CargoTransportWaitReason WaitReason,
    CargoTransportFailureReason FailureReason,
    ResourceId ResourceId,
    double RequestedQuantity,
    double CargoQuantity,
    double DeliveredQuantity,
    LogisticsNodeId Origin,
    LogisticsNodeId Destination,
    LogisticsNodeId AnchorNode,
    LogisticsNetworkVersion RouteVersion,
    System.Numerics.Vector3 WorldPosition,
    bool HasMovementTarget,
    System.Numerics.Vector3 MovementTarget);

public sealed class CargoTransportDebugSnapshot
{
    private readonly CargoTransportReadModel[] _transports;

    internal CargoTransportDebugSnapshot(
        CargoTransportMetrics metrics,
        CargoTransportReadModel[] transports)
    {
        Metrics = metrics;
        _transports = transports;
    }

    public static CargoTransportDebugSnapshot Empty { get; } =
        new(default, []);

    public CargoTransportMetrics Metrics { get; }

    public IReadOnlyList<CargoTransportReadModel> Transports =>
        _transports;
}
