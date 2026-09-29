using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PlayerLogisticsActionOperation : byte
{
    SetStockPolicy = 1,
    RemoveStockPolicy = 2,
    SetAutomaticResupplyPolicy = 3,
    RequestResupply = 4
}

public enum PlayerLogisticsActionFailureReason : byte
{
    None = 0,
    InvalidTarget = 1,
    ForeignOwnership = 2,
    UnsupportedTarget = 3,
    MissingPolicy = 4,
    InvalidThresholds = 5,
    ResupplyUnavailable = 6
}

public sealed class PlayerLogisticsActionCommand : ISimulationCommand
{
    private PlayerLogisticsActionCommand(
        PlayerLogisticsActionOperation operation,
        PlayerId issuer,
        EntityId target,
        EntityId policyEntity,
        ResourceId resourceId,
        double desiredMinimum,
        double desiredTarget,
        double desiredMaximum,
        LogisticsStockPriority priority,
        bool enabled,
        double ammunitionThreshold,
        double fuelThreshold,
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
        Target = target;
        PolicyEntity = policyEntity;
        ResourceId = resourceId;
        DesiredMinimum = desiredMinimum;
        DesiredTarget = desiredTarget;
        DesiredMaximum = desiredMaximum;
        Priority = priority;
        Enabled = enabled;
        AmmunitionThreshold = ammunitionThreshold;
        FuelThreshold = fuelThreshold;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerLogisticsActionOperation Operation { get; }

    public PlayerId Issuer { get; }

    public EntityId Target { get; }

    public EntityId PolicyEntity { get; }

    public ResourceId ResourceId { get; }

    public double DesiredMinimum { get; }

    public double DesiredTarget { get; }

    public double DesiredMaximum { get; }

    public LogisticsStockPriority Priority { get; }

    public bool Enabled { get; }

    public double AmmunitionThreshold { get; }

    public double FuelThreshold { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public PlayerLogisticsActionFailureReason FailureReason { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public static PlayerLogisticsActionCommand SetStockPolicy(
        PlayerId issuer,
        EntityId target,
        ResourceId resourceId,
        double desiredMinimum,
        double desiredTarget,
        double desiredMaximum,
        LogisticsStockPriority priority,
        bool enabled,
        SimulationTick submittedAtTick) =>
        new(
            PlayerLogisticsActionOperation.SetStockPolicy,
            issuer,
            target,
            EntityId.Invalid,
            resourceId,
            desiredMinimum,
            desiredTarget,
            desiredMaximum,
            priority,
            enabled,
            0.0,
            0.0,
            submittedAtTick);

    public static PlayerLogisticsActionCommand RemoveStockPolicy(
        PlayerId issuer,
        EntityId policyEntity,
        SimulationTick submittedAtTick) =>
        new(
            PlayerLogisticsActionOperation.RemoveStockPolicy,
            issuer,
            EntityId.Invalid,
            policyEntity,
            ResourceId.None,
            0.0,
            0.0,
            0.0,
            LogisticsStockPriority.Normal,
            false,
            0.0,
            0.0,
            submittedAtTick);

    public static PlayerLogisticsActionCommand SetAutomaticResupplyPolicy(
        PlayerId issuer,
        EntityId target,
        double ammunitionThreshold,
        double fuelThreshold,
        bool enabled,
        SimulationTick submittedAtTick) =>
        new(
            PlayerLogisticsActionOperation.SetAutomaticResupplyPolicy,
            issuer,
            target,
            EntityId.Invalid,
            ResourceId.None,
            0.0,
            0.0,
            0.0,
            LogisticsStockPriority.Normal,
            enabled,
            ammunitionThreshold,
            fuelThreshold,
            submittedAtTick);

    public static PlayerLogisticsActionCommand RequestResupply(
        PlayerId issuer,
        EntityId target,
        SimulationTick submittedAtTick) =>
        new(
            PlayerLogisticsActionOperation.RequestResupply,
            issuer,
            target,
            EntityId.Invalid,
            ResourceId.None,
            0.0,
            0.0,
            0.0,
            LogisticsStockPriority.Normal,
            true,
            0.0,
            0.0,
            submittedAtTick);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        Accepted = false;
        FailureReason = PlayerLogisticsActionFailureReason.None;

        switch (Operation)
        {
            case PlayerLogisticsActionOperation.SetStockPolicy:
                ExecuteSetStockPolicy(context);
                break;

            case PlayerLogisticsActionOperation.RemoveStockPolicy:
                ExecuteRemoveStockPolicy(context);
                break;

            case PlayerLogisticsActionOperation.SetAutomaticResupplyPolicy:
                ExecuteSetAutomaticResupplyPolicy(context);
                break;

            case PlayerLogisticsActionOperation.RequestResupply:
                ExecuteRequestResupply(context);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported logistics operation '{Operation}'.");
        }
    }

    private void ExecuteSetStockPolicy(SimulationContext context)
    {
        if (!ValidateOwnedTarget(context, Target) ||
            !SupportsStockPolicy(context, Target))
        {
            if (FailureReason == PlayerLogisticsActionFailureReason.None)
            {
                FailureReason =
                    PlayerLogisticsActionFailureReason.UnsupportedTarget;
            }

            return;
        }

        try
        {
            var command =
                new SetLogisticsStockPolicyCommand(
                    Target,
                    ResourceId,
                    DesiredMinimum,
                    DesiredTarget,
                    DesiredMaximum,
                    SubmittedAtTick,
                    Priority,
                    Enabled);
            command.Execute(context);
            Accepted = command.Accepted;
            FailureReason =
                Accepted
                    ? PlayerLogisticsActionFailureReason.None
                    : PlayerLogisticsActionFailureReason.InvalidTarget;
        }
        catch (ArgumentException)
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.InvalidThresholds;
        }
    }

    private void ExecuteRemoveStockPolicy(SimulationContext context)
    {
        if (!context.Entities.TryGetComponent(
                PolicyEntity,
                out LogisticsStockPolicy policy))
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.MissingPolicy;
            return;
        }

        if (!ValidateOwnedTarget(
                context,
                policy.TargetEntity))
        {
            return;
        }

        var command =
            new RemoveLogisticsStockPolicyCommand(
                PolicyEntity,
                SubmittedAtTick);
        command.Execute(context);

        Accepted = command.Accepted;
        FailureReason =
            Accepted
                ? PlayerLogisticsActionFailureReason.None
                : PlayerLogisticsActionFailureReason.MissingPolicy;
    }

    private void ExecuteSetAutomaticResupplyPolicy(
        SimulationContext context)
    {
        if (!ValidateOwnedTarget(context, Target))
        {
            return;
        }

        if (!context.Entities.HasComponent<GroundMovement>(Target) ||
            (!context.Entities.HasComponent<UnitFuelState>(Target) &&
             !context.Entities.HasComponent<AmmunitionState>(Target)))
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.UnsupportedTarget;
            return;
        }

        AutomaticResupplyPolicy policy;

        try
        {
            policy =
                new AutomaticResupplyPolicy(
                    AmmunitionThreshold,
                    FuelThreshold,
                    Enabled);
        }
        catch (ArgumentOutOfRangeException)
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.InvalidThresholds;
            return;
        }

        if (context.Entities.HasComponent<AutomaticResupplyPolicy>(Target))
        {
            context.Entities.SetComponent(
                Target,
                policy);
        }
        else
        {
            context.Entities.AddComponent(
                Target,
                policy);
        }

        Accepted = true;
    }

    private void ExecuteRequestResupply(SimulationContext context)
    {
        if (!ValidateOwnedTarget(context, Target))
        {
            return;
        }

        var command =
            new ResupplyCommand(
                Issuer,
                [Target],
                SubmittedAtTick);
        command.Execute(context);

        Accepted =
            command.AcceptedTargetCount > 0;
        FailureReason =
            Accepted
                ? PlayerLogisticsActionFailureReason.None
                : PlayerLogisticsActionFailureReason.ResupplyUnavailable;
    }

    private bool ValidateOwnedTarget(
        SimulationContext context,
        EntityId target)
    {
        if (!target.IsValid ||
            !context.Entities.IsAlive(target))
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.InvalidTarget;
            return false;
        }

        if (!context.Entities.TryGetComponent(
                target,
                out ControllableEntity controllable) ||
            controllable.Owner != Issuer)
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.ForeignOwnership;
            return false;
        }

        return true;
    }

    private static bool SupportsStockPolicy(
        SimulationContext context,
        EntityId target) =>
        context.Entities.HasComponent<ProductionFacility>(target) ||
        context.Entities.HasComponent<UnitProductionFacility>(target) ||
        context.Entities.HasComponent<InventoryStorage>(target) ||
        context.Entities.HasComponent<LogisticsHub>(target) ||
        context.Entities.HasComponent<SupplyDepot>(target) ||
        context.Entities.HasComponent<StorageDepot>(target) ||
        context.Entities.HasComponent<ResourceExtractor>(target);
}
