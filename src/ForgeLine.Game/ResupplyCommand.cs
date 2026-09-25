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
        var planner =
            new BattlefieldResupplyPlanner();

        for (int index = 0; index < _targets.Length; index++)
        {
            EntityId entity = _targets[index];

            if (!IsEligibleTarget(context, entity) ||
                !planner.TryIssueNearestProviderOrder(
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
