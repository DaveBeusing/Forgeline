using System.Numerics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum GroundMovementStatus : byte
{
    Idle = 0,
    Moving = 1,
    Arrived = 2,
    Stuck = 3,
    OutOfFuel = 4
}

public readonly record struct GroundMovementState(
    Vector3 Velocity,
    float HeadingRadians,
    GroundMovementStatus Status,
    SimulationTick ObservedOrderTick,
    float PreviousDistanceToTarget,
    int StalledTicks)
{
    public static GroundMovementState Stationary(float headingRadians = 0.0f)
    {
        if (!float.IsFinite(headingRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(headingRadians));
        }

        return new GroundMovementState(
            Vector3.Zero,
            NormalizeAngle(headingRadians),
            GroundMovementStatus.Idle,
            SimulationTick.Zero,
            float.PositiveInfinity,
            0);
    }

    public static float NormalizeAngle(float angle)
    {
        if (!float.IsFinite(angle))
        {
            throw new ArgumentOutOfRangeException(nameof(angle));
        }

        float normalized = MathF.IEEERemainder(angle, MathF.Tau);
        return normalized <= -MathF.PI
            ? normalized + MathF.Tau
            : normalized;
    }
}
