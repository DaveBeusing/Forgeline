using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed class MovementOrderRequest
{
    private readonly EntityId[] _entities;

    public MovementOrderRequest(
        ReadOnlySpan<EntityId> entities,
        Vector3 worldTarget)
    {
        if (entities.IsEmpty)
        {
            throw new ArgumentException(
                "A movement order request requires selected entities.",
                nameof(entities));
        }

        if (!float.IsFinite(worldTarget.X) ||
            !float.IsFinite(worldTarget.Y) ||
            !float.IsFinite(worldTarget.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(worldTarget));
        }

        _entities = entities.ToArray();
        Array.Sort(_entities);
        WorldTarget = worldTarget;
    }

    public ReadOnlySpan<EntityId> Entities => _entities;

    public Vector3 WorldTarget { get; }
}
