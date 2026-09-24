using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class MoveEntitiesCommand : ISimulationCommand
{
    private readonly EntityId[] _targets;

    public MoveEntitiesCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> targets,
        Vector3 worldTarget,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!float.IsFinite(worldTarget.X) ||
            !float.IsFinite(worldTarget.Y) ||
            !float.IsFinite(worldTarget.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(worldTarget));
        }

        if (targets.IsEmpty)
        {
            throw new ArgumentException(
                "A movement command requires at least one target entity.",
                nameof(targets));
        }

        Issuer = issuer;
        WorldTarget = worldTarget;
        SubmittedAtTick = submittedAtTick;
        _targets = targets.ToArray();
    }

    public PlayerId Issuer { get; }

    public Vector3 WorldTarget { get; }

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

            if (!context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != Issuer)
            {
                rejected++;
                continue;
            }

            var order = new MovementOrder(
                Issuer,
                WorldTarget,
                SubmittedAtTick,
                context.Tick);

            if (context.Entities.HasComponent<MovementOrder>(entity))
            {
                context.Entities.SetComponent(entity, order);
            }
            else
            {
                context.Entities.AddComponent(entity, order);
            }

            accepted++;
        }

        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;
        ExecutedAtTick = context.Tick;
    }
}
