using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum UnitProductionStatus : byte
{
    Idle = 0,
    Running = 1,
    NoInput = 2,
    NoPower = 3,
    Paused = 4
}

public enum UnitProductionBlockReason : byte
{
    None = 0,
    NoInput = 1,
    NoPower = 2,
    Paused = 3,
    UnsupportedUnit = 4,
    InvalidFacility = 5
}

public struct UnitProductionFacility
{
    public UnitProductionFacility(
        InventoryId inputInventory,
        UnitProductionCapability capabilities,
        PlayerId owner,
        Vector3 spawnOffset,
        SimulationTick activatedAtTick)
    {
        this = default;

        if (!inputInventory.IsSpecified)
        {
            throw new ArgumentException(
                "Unit production facilities require a valid input inventory.",
                nameof(inputInventory));
        }

        if (capabilities == UnitProductionCapability.None ||
            (capabilities & ~UnitProductionCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Unit production facilities require a valid owner.",
                nameof(owner));
        }

        if (!float.IsFinite(spawnOffset.X) ||
            !float.IsFinite(spawnOffset.Y) ||
            !float.IsFinite(spawnOffset.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(spawnOffset));
        }

        InputInventory = inputInventory;
        Capabilities = capabilities;
        Owner = owner;
        SpawnOffset = spawnOffset;
        ActivatedAtTick = activatedAtTick;
    }

    public InventoryId InputInventory { get; }

    public UnitProductionCapability Capabilities { get; }

    public PlayerId Owner { get; }

    public Vector3 SpawnOffset { get; }

    public SimulationTick ActivatedAtTick { get; }

    public EntityId ActiveRequest { get; internal set; }

    public UnitId ActiveUnit { get; internal set; }

    public uint ProgressTicks { get; internal set; }

    public bool InputsReserved { get; internal set; }

    public UnitProductionStatus Status { get; internal set; }

    public UnitProductionBlockReason BlockReason { get; internal set; }

    public ulong CompletedUnits { get; internal set; }

    public SimulationTick LastCompletedTick { get; internal set; }

    public bool Supports(UnitProductionCapability capability) =>
        capability != UnitProductionCapability.None &&
        (Capabilities & capability) == capability;

    internal void ActivateRequest(
        EntityId request,
        UnitId unitId)
    {
        ActiveRequest = request;
        ActiveUnit = unitId;
        ProgressTicks = 0;
        InputsReserved = false;
        Status = UnitProductionStatus.Idle;
        BlockReason = UnitProductionBlockReason.None;
    }

    internal void ClearActive(
        UnitProductionStatus status = UnitProductionStatus.Idle,
        UnitProductionBlockReason reason = UnitProductionBlockReason.None)
    {
        ActiveRequest = EntityId.Invalid;
        ActiveUnit = UnitId.None;
        ProgressTicks = 0;
        InputsReserved = false;
        Status = status;
        BlockReason = reason;
    }
}

public readonly record struct UnitProductionRequest
{
    public UnitProductionRequest(
        EntityId facility,
        UnitId unitId,
        ProductionPriority priority,
        SimulationTick submittedAtTick,
        bool paused = false)
    {
        if (!facility.IsValid)
        {
            throw new ArgumentException(
                "Unit production requests require a valid facility.",
                nameof(facility));
        }

        if (!unitId.IsSpecified)
        {
            throw new ArgumentException(
                "Unit production requests require a stable unit ID.",
                nameof(unitId));
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        Facility = facility;
        UnitId = unitId;
        Priority = priority;
        SubmittedAtTick = submittedAtTick;
        Paused = paused;
    }

    public EntityId Facility { get; }

    public UnitId UnitId { get; }

    public ProductionPriority Priority { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Paused { get; }

    public UnitProductionRequest WithPaused(bool paused) =>
        new(
            Facility,
            UnitId,
            Priority,
            SubmittedAtTick,
            paused);
}

public readonly record struct UnitProductionCancellationRequest;

public sealed class QueueUnitProductionCommand : ISimulationCommand
{
    public QueueUnitProductionCommand(
        PlayerId issuer,
        EntityId facility,
        UnitId unitId,
        SimulationTick submittedAtTick,
        ProductionPriority priority = ProductionPriority.Normal)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!facility.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(facility));
        }

        if (!unitId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(unitId));
        }

        Issuer = issuer;
        Facility = facility;
        UnitId = unitId;
        SubmittedAtTick = submittedAtTick;
        Priority = priority;
    }

    public PlayerId Issuer { get; }

    public EntityId Facility { get; }

    public UnitId UnitId { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ProductionPriority Priority { get; }

    public EntityId RequestEntity { get; private set; }

    public bool Accepted { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.TryGetComponent(
                Facility,
                out UnitProductionFacility? production) ||
            production.Owner != Issuer)
        {
            Accepted = false;
            RequestEntity = EntityId.Invalid;
            return;
        }

        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new UnitProductionRequest(
                Facility,
                UnitId,
                Priority,
                SubmittedAtTick));

        RequestEntity = request;
        Accepted = true;
    }
}

public sealed class CancelUnitProductionRequestCommand : ISimulationCommand
{
    public CancelUnitProductionRequestCommand(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!requestEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(requestEntity));
        }

        Issuer = issuer;
        RequestEntity = requestEntity;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerId Issuer { get; }

    public EntityId RequestEntity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExecutedAtTick = context.Tick;

        if (!context.Entities.TryGetComponent(
                RequestEntity,
                out UnitProductionRequest request) ||
            !context.Entities.TryGetComponent(
                request.Facility,
                out UnitProductionFacility? facility) ||
            facility.Owner != Issuer)
        {
            Accepted = false;
            return;
        }

        if (!context.Entities.HasComponent<UnitProductionCancellationRequest>(
                RequestEntity))
        {
            context.Entities.AddComponent(
                RequestEntity,
                new UnitProductionCancellationRequest());
        }

        Accepted = true;
    }
}

public readonly record struct UnitProductionMetrics(
    int FacilityCount,
    int RunningFacilityCount,
    int BlockedFacilityCount,
    long CompletedUnits,
    long CancelledRequests,
    long RejectedRequests);

public readonly record struct UnitProductionFacilityReadModel(
    EntityId Entity,
    InventoryId InputInventory,
    UnitId ActiveUnit,
    UnitProductionStatus Status,
    UnitProductionBlockReason BlockReason,
    double Progress,
    PowerOperationalState PowerState,
    ulong CompletedUnits);
