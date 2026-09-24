using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Game;

public readonly record struct GroundMovementDiagnosticsSnapshot(
    int GroundUnitCount,
    int OrderedUnitCount,
    int MovingUnitCount,
    int StuckUnitCount,
    int ArrivedUnitCount,
    int TerrainBlockedUnitCount,
    int NeighborAdjustmentCount,
    int ObstacleAdjustmentCount);

public readonly record struct GroundMovementDebugAgent(
    EntityId Entity,
    Vector3 Position,
    Vector3 Velocity,
    Vector3 Target,
    float Radius,
    float SeparationRadius,
    GroundMovementStatus Status,
    bool HasTarget);

public sealed class GroundMovementDebugSnapshot
{
    private readonly GroundMovementDebugAgent[] _agents;

    public GroundMovementDebugSnapshot(
        ReadOnlySpan<GroundMovementDebugAgent> agents)
    {
        _agents = agents.ToArray();
    }

    public IReadOnlyList<GroundMovementDebugAgent> Agents => _agents;

    public static GroundMovementDebugSnapshot Empty { get; } =
        new(ReadOnlySpan<GroundMovementDebugAgent>.Empty);
}

public sealed record GroundMovementSystemOptions
{
    public const int DefaultStuckTickThreshold = 40;
    public const float DefaultProgressEpsilonMeters = 0.025f;

    public int StuckTickThreshold { get; init; } =
        DefaultStuckTickThreshold;

    public float ProgressEpsilonMeters { get; init; } =
        DefaultProgressEpsilonMeters;

    public float SeparationWeight { get; init; } = 1.75f;

    public float ObstacleSteeringWeight { get; init; } = 2.25f;

    public float ObstacleClearanceMeters { get; init; } = 0.25f;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            StuckTickThreshold,
            1);

        RequirePositiveFinite(
            ProgressEpsilonMeters,
            nameof(ProgressEpsilonMeters));
        RequireNonNegativeFinite(
            SeparationWeight,
            nameof(SeparationWeight));
        RequireNonNegativeFinite(
            ObstacleSteeringWeight,
            nameof(ObstacleSteeringWeight));
        RequireNonNegativeFinite(
            ObstacleClearanceMeters,
            nameof(ObstacleClearanceMeters));
    }

    private static void RequirePositiveFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void RequireNonNegativeFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
