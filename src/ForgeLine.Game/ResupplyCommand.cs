using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class ResupplyCommand : ISimulationCommand
{
    private readonly EntityId[] _targets;

    public ResupplyCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> targets,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (targets.IsEmpty)
        {
            throw new ArgumentException(
                "A resupply command requires at least one target entity.",
                nameof(targets));
        }

        Issuer = issuer;
        SubmittedAtTick = submittedAtTick;
        _targets = targets.ToArray();
    }

    public PlayerId Issuer { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ReadOnlySpan<EntityId> Targets => _targets;

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int accepted = 0;
        int rejected = 0;
        for (int index = 0; index < _targets.Length; index++)
        {
            EntityId entity = _targets[index];

            if (IsEligibleTarget(context, entity) && context.Entities.HasComponent<SupplyTruck>(entity) &&
                !context.Entities.HasComponent<CargoTransportOrder>(entity) && TryRequestLoading(context, entity))
            {
                accepted++;
                continue;
            }
            if (!IsEligibleTarget(context, entity) ||
                !BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(
                    context,
                    entity,
                    Issuer,
                    SubmittedAtTick,
                    out _))
            {
                rejected++;
                continue;
            }

            accepted++;
        }

        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;
        ExecutedAtTick = context.Tick;
    }

    private bool TryRequestLoading(SimulationContext context, EntityId truck)
    {
        if (context.Entities.HasComponent<SupplyLoadingOrder>(truck)) return true;
        var position = context.Entities.GetComponent<WorldTransform>(truck).Position;
        EntityId nearest = EntityId.Invalid;
        float distance = float.PositiveInfinity;
        foreach (EntityId source in context.Entities.Query<CompletedBuilding>(ForgeLine.Ecs.QueryIterationOrder.StableByEntityIndex))
        {
            var building = context.Entities.GetComponent<CompletedBuilding>(source);
            if (building.Owner != Issuer || !context.Entities.HasComponent<BuildingLoadingDock>(source) ||
                !context.Entities.TryGetComponent(source, out WorldTransform transform)) continue;
            bool usable = context.Entities.TryGetComponent(source, out SupplyDepot depot) && depot.State == SupplyDepotState.Operational ||
                context.Entities.HasComponent<CommandFacility>(source) &&
                context.Entities.TryGetComponent(source, out SupplyProvider provider) && provider.Enabled;
            float candidate = VehicleDockPose.HorizontalDistanceSquared(position, transform.Position);
            if (usable && candidate < distance) { nearest = source; distance = candidate; }
        }
        if (!nearest.IsValid) return false;
        VehicleDockingService.Put(context.Entities, truck, new SupplyLoadingOrder(nearest, context.Tick));
        return true;
    }

    private bool IsEligibleTarget(
        SimulationContext context,
        EntityId entity)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out ControllableEntity controllable) ||
            !controllable.IsControllable ||
            controllable.Owner != Issuer ||
            !context.Entities.HasComponent<WorldTransform>(entity) ||
            !context.Entities.HasComponent<GroundMovement>(entity))
        {
            return false;
        }

        return
            context.Entities.HasComponent<UnitFuelState>(entity) ||
            context.Entities.HasComponent<AmmunitionState>(entity);
    }

}
