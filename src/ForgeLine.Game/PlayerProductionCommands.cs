using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PlayerProductionOperation : byte
{
    Queue = 1,
    SetPaused = 2,
    Cancel = 3
}

public sealed class PlayerProductionActionCommand : ISimulationCommand
{
    private PlayerProductionActionCommand(
        PlayerProductionOperation operation,
        PlayerId issuer,
        EntityId facility,
        RecipeId recipeId,
        EntityId requestEntity,
        bool paused,
        ProductionPriority priority,
        ProductionRequestMode mode,
        ResourceId desiredStockResourceId,
        double desiredStockQuantity,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Operation = operation;
        Issuer = issuer;
        Facility = facility;
        RecipeId = recipeId;
        RequestEntity = requestEntity;
        Paused = paused;
        Priority = priority;
        Mode = mode;
        DesiredStockResourceId = desiredStockResourceId;
        DesiredStockQuantity = desiredStockQuantity;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerProductionOperation Operation { get; }

    public PlayerId Issuer { get; }

    public EntityId Facility { get; }

    public RecipeId RecipeId { get; }

    public EntityId RequestEntity { get; }

    public bool Paused { get; }

    public ProductionPriority Priority { get; }

    public ProductionRequestMode Mode { get; }

    public ResourceId DesiredStockResourceId { get; }

    public double DesiredStockQuantity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public EntityId CreatedRequestEntity { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public static PlayerProductionActionCommand Queue(
        PlayerId issuer,
        EntityId facility,
        RecipeId recipeId,
        SimulationTick submittedAtTick,
        ProductionPriority priority = ProductionPriority.Normal,
        ProductionRequestMode mode = ProductionRequestMode.OneShot,
        ResourceId desiredStockResourceId = default,
        double desiredStockQuantity = 0.0) =>
        new(
            PlayerProductionOperation.Queue,
            issuer,
            facility,
            recipeId,
            EntityId.Invalid,
            paused: false,
            priority,
            mode,
            desiredStockResourceId,
            desiredStockQuantity,
            submittedAtTick);

    public static PlayerProductionActionCommand SetPaused(
        PlayerId issuer,
        EntityId requestEntity,
        bool paused,
        SimulationTick submittedAtTick) =>
        new(
            PlayerProductionOperation.SetPaused,
            issuer,
            EntityId.Invalid,
            RecipeId.None,
            requestEntity,
            paused,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            submittedAtTick);

    public static PlayerProductionActionCommand Cancel(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick submittedAtTick) =>
        new(
            PlayerProductionOperation.Cancel,
            issuer,
            EntityId.Invalid,
            RecipeId.None,
            requestEntity,
            paused: false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            submittedAtTick);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        Accepted = false;
        CreatedRequestEntity = EntityId.Invalid;

        switch (Operation)
        {
            case PlayerProductionOperation.Queue:
                ExecuteQueue(context);
                break;

            case PlayerProductionOperation.SetPaused:
                ExecuteSetPaused(context);
                break;

            case PlayerProductionOperation.Cancel:
                ExecuteCancel(context);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported production operation '{Operation}'.");
        }
    }

    private void ExecuteQueue(SimulationContext context)
    {
        if (!OwnsProductionFacility(
                context,
                Facility))
        {
            return;
        }

        var command =
            new QueueProductionCommand(
                Facility,
                RecipeId,
                SubmittedAtTick,
                Priority,
                Mode,
                DesiredStockResourceId,
                DesiredStockQuantity);
        command.Execute(context);

        Accepted = command.Accepted;
        CreatedRequestEntity = command.RequestEntity;
    }

    private void ExecuteSetPaused(SimulationContext context)
    {
        if (!OwnsProductionRequest(
                context,
                RequestEntity))
        {
            return;
        }

        var command =
            new SetProductionRequestPausedCommand(
                RequestEntity,
                Paused,
                SubmittedAtTick);
        command.Execute(context);

        Accepted = command.Accepted;
    }

    private void ExecuteCancel(SimulationContext context)
    {
        if (!OwnsProductionRequest(
                context,
                RequestEntity))
        {
            return;
        }

        var command =
            new CancelProductionRequestCommand(
                RequestEntity,
                SubmittedAtTick);
        command.Execute(context);

        Accepted = command.Accepted;
    }

    private bool OwnsProductionRequest(
        SimulationContext context,
        EntityId requestEntity)
    {
        return context.Entities.TryGetComponent(
                   requestEntity,
                   out ProductionRequest request) &&
               OwnsProductionFacility(
                   context,
                   request.Facility);
    }

    private bool OwnsProductionFacility(
        SimulationContext context,
        EntityId facility)
    {
        return facility.IsValid &&
               context.Entities.HasComponent<ProductionFacility>(
                   facility) &&
               context.Entities.TryGetComponent(
                   facility,
                   out ControllableEntity controllable) &&
               controllable.Owner == Issuer;
    }
}

public enum PlayerUnitProductionOperation : byte
{
    Queue = 1,
    Cancel = 2,
    SetRallyPoint = 3
}

public sealed class PlayerUnitProductionActionCommand : ISimulationCommand
{
    private PlayerUnitProductionActionCommand(
        PlayerUnitProductionOperation operation,
        PlayerId issuer,
        EntityId facility,
        UnitId unitId,
        EntityId requestEntity,
        ProductionPriority priority,
        Vector3 rallyPoint,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Operation = operation;
        Issuer = issuer;
        Facility = facility;
        UnitId = unitId;
        RequestEntity = requestEntity;
        Priority = priority;
        RallyPoint = rallyPoint;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerUnitProductionOperation Operation { get; }

    public PlayerId Issuer { get; }

    public EntityId Facility { get; }

    public UnitId UnitId { get; }

    public EntityId RequestEntity { get; }

    public ProductionPriority Priority { get; }

    public Vector3 RallyPoint { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public EntityId CreatedRequestEntity { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public static PlayerUnitProductionActionCommand Queue(
        PlayerId issuer,
        EntityId facility,
        UnitId unitId,
        SimulationTick submittedAtTick,
        ProductionPriority priority = ProductionPriority.Normal) =>
        new(
            PlayerUnitProductionOperation.Queue,
            issuer,
            facility,
            unitId,
            EntityId.Invalid,
            priority,
            Vector3.Zero,
            submittedAtTick);

    public static PlayerUnitProductionActionCommand Cancel(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick submittedAtTick) =>
        new(
            PlayerUnitProductionOperation.Cancel,
            issuer,
            EntityId.Invalid,
            UnitId.None,
            requestEntity,
            ProductionPriority.Normal,
            Vector3.Zero,
            submittedAtTick);

    public static PlayerUnitProductionActionCommand SetRallyPoint(
        PlayerId issuer,
        EntityId facility,
        Vector3 rallyPoint,
        SimulationTick submittedAtTick) =>
        new(
            PlayerUnitProductionOperation.SetRallyPoint,
            issuer,
            facility,
            UnitId.None,
            EntityId.Invalid,
            ProductionPriority.Normal,
            rallyPoint,
            submittedAtTick);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        Accepted = false;
        CreatedRequestEntity = EntityId.Invalid;

        switch (Operation)
        {
            case PlayerUnitProductionOperation.Queue:
            {
                var command =
                    new QueueUnitProductionCommand(
                        Issuer,
                        Facility,
                        UnitId,
                        SubmittedAtTick,
                        Priority);
                command.Execute(context);
                Accepted = command.Accepted;
                CreatedRequestEntity = command.RequestEntity;
                break;
            }

            case PlayerUnitProductionOperation.Cancel:
            {
                var command =
                    new CancelUnitProductionRequestCommand(
                        Issuer,
                        RequestEntity,
                        SubmittedAtTick);
                command.Execute(context);
                Accepted = command.Accepted;
                break;
            }

            case PlayerUnitProductionOperation.SetRallyPoint:
            {
                var command =
                    new SetUnitProductionRallyPointCommand(
                        Issuer,
                        Facility,
                        RallyPoint,
                        SubmittedAtTick);
                command.Execute(context);
                Accepted = command.Accepted;
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported unit-production operation '{Operation}'.");
        }
    }
}
