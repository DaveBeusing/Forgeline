using System.Numerics;

namespace ForgeLine.Game;

public readonly record struct GroundMovement
{
    public GroundMovement(
        float maximumSpeed,
        float acceleration,
        float deceleration,
        float turnRateRadiansPerSecond,
        float radius,
        float stopRadius,
        float separationRadius,
        float obstacleLookAhead,
        float maximumSlopeDegrees,
        float heightOffset = 0.0f)
    {
        RequirePositiveFinite(maximumSpeed, nameof(maximumSpeed));
        RequirePositiveFinite(acceleration, nameof(acceleration));
        RequirePositiveFinite(deceleration, nameof(deceleration));
        RequirePositiveFinite(
            turnRateRadiansPerSecond,
            nameof(turnRateRadiansPerSecond));
        RequirePositiveFinite(radius, nameof(radius));
        RequireNonNegativeFinite(stopRadius, nameof(stopRadius));
        RequirePositiveFinite(separationRadius, nameof(separationRadius));
        RequireNonNegativeFinite(obstacleLookAhead, nameof(obstacleLookAhead));

        if (!float.IsFinite(maximumSlopeDegrees) ||
            maximumSlopeDegrees < 0.0f ||
            maximumSlopeDegrees >= 90.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSlopeDegrees));
        }

        if (!float.IsFinite(heightOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(heightOffset));
        }

        MaximumSpeed = maximumSpeed;
        Acceleration = acceleration;
        Deceleration = deceleration;
        TurnRateRadiansPerSecond = turnRateRadiansPerSecond;
        Radius = radius;
        StopRadius = stopRadius;
        SeparationRadius = MathF.Max(separationRadius, radius * 2.0f);
        ObstacleLookAhead = obstacleLookAhead;
        MaximumSlopeDegrees = maximumSlopeDegrees;
        HeightOffset = heightOffset;
    }

    public float MaximumSpeed { get; }

    public float Acceleration { get; }

    public float Deceleration { get; }

    public float TurnRateRadiansPerSecond { get; }

    public float Radius { get; }

    public float StopRadius { get; }

    public float SeparationRadius { get; }

    public float ObstacleLookAhead { get; }

    public float MaximumSlopeDegrees { get; }

    public float HeightOffset { get; }

    public static GroundMovement CreateDefault(float heightOffset = 0.0f)
    {
        return new GroundMovement(
            maximumSpeed: 12.0f,
            acceleration: 8.0f,
            deceleration: 12.0f,
            turnRateRadiansPerSecond: MathF.PI,
            radius: 2.0f,
            stopRadius: 0.75f,
            separationRadius: 6.0f,
            obstacleLookAhead: 8.0f,
            maximumSlopeDegrees: 35.0f,
            heightOffset: heightOffset);
    }

    private static void RequirePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireNonNegativeFinite(
        float value,
        string parameterName)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
