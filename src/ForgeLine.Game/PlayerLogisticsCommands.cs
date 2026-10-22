using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PlayerLogisticsActionOperation : byte
{
    SetStockPolicy = 1,
    RemoveStockPolicy = 2,
    SetAutomaticResupplyPolicy = 3,
    RequestResupply = 4,
    SetSupplyPriority = 5,
    CreateCargoConvoy = 6,
    DissolveCargoConvoy = 7
}

public enum PlayerLogisticsActionFailureReason : byte
{
    None = 0,
    InvalidTarget = 1,
    ForeignOwnership = 2,
    UnsupportedTarget = 3,
    MissingPolicy = 4,
    InvalidThresholds = 5,
    ResupplyUnavailable = 6,
    InvalidPriority = 7,
    ConvoyIneligible = 8
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
        BattlefieldSupplyPriority supplyPriority,
        SimulationTick submittedAtTick,
        EntityId[]? convoyTrucks = null,
        EntityId[]? convoyEscorts = null)
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
        if (!Enum.IsDefined(supplyPriority))
        {
            throw new ArgumentOutOfRangeException(nameof(supplyPriority));
        }

        AmmunitionThreshold = ammunitionThreshold;
        FuelThreshold = fuelThreshold;
        SupplyPriority = supplyPriority;
        SubmittedAtTick = submittedAtTick;
        _convoyTrucks = convoyTrucks?.ToArray() ?? [];
        _convoyEscorts = convoyEscorts?.ToArray() ?? [];
    }

    private readonly EntityId[] _convoyTrucks;
    private readonly EntityId[] _convoyEscorts;

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

    public BattlefieldSupplyPriority SupplyPriority { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public PlayerLogisticsActionFailureReason FailureReason { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public EntityId CreatedConvoy { get; private set; }

    public ReadOnlySpan<EntityId> ConvoyTrucks => _convoyTrucks;

    public ReadOnlySpan<EntityId> ConvoyEscorts => _convoyEscorts;

    public static PlayerLogisticsActionCommand CreateCargoConvoy(
        PlayerId issuer,
        ReadOnlySpan<EntityId> trucks,
        ReadOnlySpan<EntityId> escorts,
        SimulationTick submittedAtTick) =>
        new(PlayerLogisticsActionOperation.CreateCargoConvoy, issuer,
            EntityId.Invalid, EntityId.Invalid, ResourceId.None,
            0, 0, 0, LogisticsStockPriority.Normal, true, 0, 0,
            BattlefieldSupplyPriority.Normal, submittedAtTick,
            trucks.ToArray(), escorts.ToArray());

    public static PlayerLogisticsActionCommand DissolveCargoConvoy(
        PlayerId issuer, EntityId group, SimulationTick submittedAtTick) =>
        new(PlayerLogisticsActionOperation.DissolveCargoConvoy, issuer,
            group, EntityId.Invalid, ResourceId.None,
            0, 0, 0, LogisticsStockPriority.Normal, true, 0, 0,
            BattlefieldSupplyPriority.Normal, submittedAtTick);

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
            BattlefieldSupplyPriority.Normal,
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
            BattlefieldSupplyPriority.Normal,
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
            BattlefieldSupplyPriority.Normal,
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
            BattlefieldSupplyPriority.Normal,
            submittedAtTick);

    public static PlayerLogisticsActionCommand SetSupplyPriority(
        PlayerId issuer,
        EntityId target,
        BattlefieldSupplyPriority priority,
        SimulationTick submittedAtTick) =>
        new(
            PlayerLogisticsActionOperation.SetSupplyPriority,
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
            priority,
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

            case PlayerLogisticsActionOperation.SetSupplyPriority:
                ExecuteSetSupplyPriority(context);
                break;

            case PlayerLogisticsActionOperation.CreateCargoConvoy:
                Accepted = CargoConvoyCommands.TryCreate(context, Issuer,
                    _convoyTrucks, _convoyEscorts,
                    out EntityId created, out PlayerLogisticsActionFailureReason createFailure);
                CreatedConvoy = created;
                FailureReason = createFailure;
                break;

            case PlayerLogisticsActionOperation.DissolveCargoConvoy:
                Accepted = CargoConvoyCommands.TryDissolve(
                    context, Issuer, Target, out PlayerLogisticsActionFailureReason dissolveFailure);
                FailureReason = dissolveFailure;
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

        if (!Enum.IsDefined(Priority))
        {
            FailureReason = PlayerLogisticsActionFailureReason.InvalidPriority;
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

    private void ExecuteSetSupplyPriority(
        SimulationContext context)
    {
        if (!ValidateOwnedTarget(context, Target))
        {
            return;
        }

        if (!context.Entities.HasComponent<UnitFuelState>(Target) &&
            !context.Entities.HasComponent<AmmunitionState>(Target))
        {
            FailureReason =
                PlayerLogisticsActionFailureReason.UnsupportedTarget;
            return;
        }

        var priority =
            new UnitSupplyPriority(
                SupplyPriority);

        if (context.Entities.HasComponent<UnitSupplyPriority>(Target))
        {
            context.Entities.SetComponent(
                Target,
                priority);
        }
        else
        {
            context.Entities.AddComponent(
                Target,
                priority);
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
