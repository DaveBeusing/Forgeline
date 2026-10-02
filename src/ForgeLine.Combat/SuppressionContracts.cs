using ForgeLine.Simulation;

namespace ForgeLine.Combat;

public enum SuppressionLevel : byte
{
    Normal = 0,
    Suppressed = 1,
    Pinned = 2
}

public readonly record struct SuppressionProfile
{
    public SuppressionProfile(
        double suppressedThreshold = 0.35,
        double pinnedThreshold = 0.75,
        double decayPerSecond = 0.12,
        double impactScale = 3.0,
        float suppressedSpeedScale = 0.55f)
    {
        if (!double.IsFinite(suppressedThreshold) ||
            suppressedThreshold <= 0.0 ||
            suppressedThreshold >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(suppressedThreshold));
        }

        if (!double.IsFinite(pinnedThreshold) ||
            pinnedThreshold <= suppressedThreshold ||
            pinnedThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pinnedThreshold));
        }

        if (!double.IsFinite(decayPerSecond) ||
            decayPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decayPerSecond));
        }

        if (!double.IsFinite(impactScale) ||
            impactScale <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(impactScale));
        }

        if (!float.IsFinite(suppressedSpeedScale) ||
            suppressedSpeedScale <= 0.0f ||
            suppressedSpeedScale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(suppressedSpeedScale));
        }

        SuppressedThreshold = suppressedThreshold;
        PinnedThreshold = pinnedThreshold;
        DecayPerSecond = decayPerSecond;
        ImpactScale = impactScale;
        SuppressedSpeedScale = suppressedSpeedScale;
    }

    public double SuppressedThreshold { get; }

    public double PinnedThreshold { get; }

    public double DecayPerSecond { get; }

    public double ImpactScale { get; }

    public float SuppressedSpeedScale { get; }

    public SuppressionLevel ResolveLevel(double value) =>
        value >= PinnedThreshold
            ? SuppressionLevel.Pinned
            : value >= SuppressedThreshold
                ? SuppressionLevel.Suppressed
                : SuppressionLevel.Normal;

    public static SuppressionProfile InfantryDefault =>
        new(
            suppressedThreshold: 0.35,
            pinnedThreshold: 0.75,
            decayPerSecond: 0.12,
            impactScale: 3.0,
            suppressedSpeedScale: 0.55f);
}

public readonly record struct SuppressionState(
    double Value,
    SuppressionLevel Level,
    SimulationTick LastImpactTick,
    SimulationTick UpdatedAtTick)
{
    public static SuppressionState Clear =>
        new(
            0.0,
            SuppressionLevel.Normal,
            SimulationTick.Zero,
            SimulationTick.Zero);
}

public readonly record struct SuppressionMovementConstraint
{
    public SuppressionMovementConstraint(
        float maximumSpeedScale,
        bool canMove)
    {
        if (!float.IsFinite(maximumSpeedScale) ||
            maximumSpeedScale < 0.0f ||
            maximumSpeedScale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSpeedScale));
        }

        MaximumSpeedScale = maximumSpeedScale;
        CanMove = canMove;
    }

    public float MaximumSpeedScale { get; }

    public bool CanMove { get; }
}

public static class SuppressionRules
{
    public static SuppressionState ApplyImpact(
        in SuppressionState current,
        in SuppressionProfile profile,
        in HealthState healthBeforeDamage,
        double appliedDamage,
        SimulationTick tick)
    {
        if (!double.IsFinite(appliedDamage) ||
            appliedDamage <= 0.0)
        {
            return current;
        }

        double normalizedDamage =
            Math.Clamp(
                appliedDamage /
                healthBeforeDamage.Maximum,
                0.0,
                1.0);
        double value =
            Math.Clamp(
                current.Value +
                normalizedDamage *
                profile.ImpactScale,
                0.0,
                1.0);

        return new SuppressionState(
            value,
            profile.ResolveLevel(value),
            tick,
            tick);
    }

    public static SuppressionState Decay(
        in SuppressionState current,
        in SuppressionProfile profile,
        TimeSpan tickDuration,
        SimulationTick tick)
    {
        double seconds =
            tickDuration.TotalSeconds;

        if (!double.IsFinite(seconds) ||
            seconds <= 0.0)
        {
            return current;
        }

        double value =
            Math.Max(
                0.0,
                current.Value -
                profile.DecayPerSecond *
                seconds);

        return new SuppressionState(
            value,
            profile.ResolveLevel(value),
            current.LastImpactTick,
            tick);
    }
}
