using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum LogisticsHubState : byte
{
    Operational = 0,
    Disabled = 1
}

public readonly record struct LogisticsHub
{
    public LogisticsHub(
        InventoryId inventoryId,
        FactionId owner = default,
        LogisticsHubState state = LogisticsHubState.Operational)
    {
        if (!inventoryId.IsSpecified)
        {
            throw new ArgumentException(
                "Logistics hubs require a valid inventory ID.",
                nameof(inventoryId));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        InventoryId = inventoryId;
        Owner = owner;
        State = state;
    }

    public InventoryId InventoryId { get; }

    public FactionId Owner { get; }

    public LogisticsHubState State { get; }

    public LogisticsHub WithState(LogisticsHubState state) =>
        new(InventoryId, Owner, state);
}

public enum LogisticsStockPriority : byte
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

public readonly record struct LogisticsStockPolicy
{
    public LogisticsStockPolicy(
        EntityId targetEntity,
        ResourceId resourceId,
        double desiredMinimum,
        double desiredTarget,
        double desiredMaximum,
        LogisticsStockPriority priority = LogisticsStockPriority.Normal,
        bool enabled = true)
    {
        if (!targetEntity.IsValid)
        {
            throw new ArgumentException(
                "Stock policies require a valid target entity.",
                nameof(targetEntity));
        }

        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Stock policies require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(desiredMinimum) ||
            !double.IsFinite(desiredTarget) ||
            !double.IsFinite(desiredMaximum) ||
            desiredMinimum < 0.0 ||
            desiredTarget <= 0.0 ||
            desiredMaximum <= 0.0 ||
            desiredMinimum > desiredTarget ||
            desiredTarget > desiredMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredTarget),
                "Stock thresholds must be finite and satisfy 0 <= minimum <= target <= maximum.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        TargetEntity = targetEntity;
        ResourceId = resourceId;
        DesiredMinimum = desiredMinimum;
        DesiredTarget = desiredTarget;
        DesiredMaximum = desiredMaximum;
        Priority = priority;
        Enabled = enabled;
    }

    public EntityId TargetEntity { get; }

    public ResourceId ResourceId { get; }

    public double DesiredMinimum { get; }

    public double DesiredTarget { get; }

    public double DesiredMaximum { get; }

    public LogisticsStockPriority Priority { get; }

    public bool Enabled { get; }
}

public readonly record struct LogisticsTransportRequestId(ulong Value)
    : IComparable<LogisticsTransportRequestId>
{
    public static LogisticsTransportRequestId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(LogisticsTransportRequestId other) =>
        Value.CompareTo(other.Value);

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum LogisticsTransportRequestState : byte
{
    Pending = 0,
    Assigned = 1,
    InTransit = 2,
    RetryPending = 3,
    Completed = 4,
    Failed = 5
}

public enum LogisticsTransportRequestFailureReason : byte
{
    None = 0,
    DestinationUnavailable = 1,
    NoSourceSurplus = 2,
    NoRoute = 3,
    NoTruckAvailable = 4,
    ReservationFailed = 5,
    AssignmentFailed = 6,
    TransportFailed = 7,
    RetryLimitReached = 8
}

public readonly record struct CargoTransportReservation
{
    public CargoTransportReservation(
        LogisticsTransportRequestId requestId,
        InventoryId sourceInventory,
        ResourceId resourceId,
        double quantity)
    {
        if (!requestId.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo reservations require a transport request ID.",
                nameof(requestId));
        }

        if (!sourceInventory.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo reservations require a source inventory.",
                nameof(sourceInventory));
        }

        if (!resourceId.IsSpecified)
        {
            throw new ArgumentException(
                "Cargo reservations require a stable resource ID.",
                nameof(resourceId));
        }

        if (!double.IsFinite(quantity) || quantity <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        RequestId = requestId;
        SourceInventory = sourceInventory;
        ResourceId = resourceId;
        Quantity = quantity;
    }

    public LogisticsTransportRequestId RequestId { get; }

    public InventoryId SourceInventory { get; }

    public ResourceId ResourceId { get; }

    public double Quantity { get; }
}

public readonly record struct LogisticsTransportRequestReadModel(
    LogisticsTransportRequestId RequestId,
    EntityId PolicyEntity,
    LogisticsTransportRequestState State,
    LogisticsTransportRequestFailureReason FailureReason,
    LogisticsStockPriority Priority,
    ResourceId ResourceId,
    LogisticsNodeId Origin,
    LogisticsNodeId Destination,
    System.Numerics.Vector3 OriginPosition,
    System.Numerics.Vector3 DestinationPosition,
    double RequestedQuantity,
    double ReservedQuantity,
    EntityId AssignedTruck,
    uint AttemptCount,
    SimulationTick CreatedAtTick,
    SimulationTick StateChangedAtTick);

public readonly record struct AutomatedDistributionMetrics(
    int PolicyCount,
    int PendingRequestCount,
    int AssignedRequestCount,
    int InTransitRequestCount,
    int RetryPendingRequestCount,
    int UnservedDeficitCount,
    int IdleTruckCount,
    int ActiveTruckCount,
    double ReservedCargoQuantity,
    long CompletedRequestCount,
    long FailedRequestCount,
    double AverageDeliveryLatencyTicks,
    ulong MaximumDeliveryLatencyTicks);

public sealed class AutomatedDistributionDebugSnapshot
{
    private readonly LogisticsTransportRequestReadModel[] _requests;

    internal AutomatedDistributionDebugSnapshot(
        AutomatedDistributionMetrics metrics,
        LogisticsTransportRequestReadModel[] requests)
    {
        Metrics = metrics;
        _requests = requests;
    }

    public static AutomatedDistributionDebugSnapshot Empty { get; } =
        new(default, []);

    public AutomatedDistributionMetrics Metrics { get; }

    public IReadOnlyList<LogisticsTransportRequestReadModel> Requests =>
        _requests;
}

public sealed class SetLogisticsStockPolicyCommand : ISimulationCommand
{
    public SetLogisticsStockPolicyCommand(
        EntityId targetEntity,
        ResourceId resourceId,
        double desiredMinimum,
        double desiredTarget,
        double desiredMaximum,
        SimulationTick submittedAtTick,
        LogisticsStockPriority priority = LogisticsStockPriority.Normal,
        bool enabled = true)
    {
        Policy = new LogisticsStockPolicy(
            targetEntity,
            resourceId,
            desiredMinimum,
            desiredTarget,
            desiredMaximum,
            priority,
            enabled);
        SubmittedAtTick = submittedAtTick;
    }

    public LogisticsStockPolicy Policy { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public EntityId PolicyEntity { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.IsAlive(Policy.TargetEntity))
        {
            Accepted = false;
            PolicyEntity = EntityId.Invalid;
            return;
        }

        foreach (EntityId entity in
                 context.Entities.Query<LogisticsStockPolicy>(
                     ForgeLine.Ecs.QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy existing =
                context.Entities.GetComponent<LogisticsStockPolicy>(entity);

            if (existing.TargetEntity != Policy.TargetEntity ||
                existing.ResourceId != Policy.ResourceId)
            {
                continue;
            }

            context.Entities.SetComponent(entity, Policy);
            PolicyEntity = entity;
            Accepted = true;
            return;
        }

        EntityId policyEntity = context.Entities.CreateEntity();
        context.Entities.AddComponent(policyEntity, Policy);
        PolicyEntity = policyEntity;
        Accepted = true;
    }
}

public sealed class RemoveLogisticsStockPolicyCommand : ISimulationCommand
{
    public RemoveLogisticsStockPolicyCommand(
        EntityId policyEntity,
        SimulationTick submittedAtTick)
    {
        if (!policyEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(policyEntity));
        }

        PolicyEntity = policyEntity;
        SubmittedAtTick = submittedAtTick;
    }

    public EntityId PolicyEntity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.IsAlive(PolicyEntity) ||
            !context.Entities.HasComponent<LogisticsStockPolicy>(PolicyEntity))
        {
            Accepted = false;
            return;
        }

        Accepted = context.Entities.DestroyEntity(PolicyEntity);
    }
}
