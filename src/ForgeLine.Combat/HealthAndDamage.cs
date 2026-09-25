using System.Numerics;

namespace ForgeLine.Combat;

public readonly record struct DamagePayload
{
    public DamagePayload(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        Amount = amount;
    }

    public double Amount { get; }
}

public readonly record struct HealthState
{
    public HealthState(
        double current,
        double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        if (!double.IsFinite(current) ||
            current < 0.0 ||
            current > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(current));
        }

        Current = current;
        Maximum = maximum;
    }

    public double Current { get; }

    public double Maximum { get; }

    public double Fraction => Current / Maximum;

    public bool IsDepleted => Current <= 0.0;

    public static HealthState Full(double maximum) =>
        new(maximum, maximum);

    public HealthState ApplyDamage(
        in DamagePayload damage,
        out double appliedDamage)
    {
        appliedDamage = Math.Min(Current, damage.Amount);

        return appliedDamage <= 0.0
            ? this
            : new HealthState(
                Math.Max(0.0, Current - appliedDamage),
                Maximum);
    }
}

public readonly record struct CombatHitbox
{
    public CombatHitbox(Vector3 halfExtents)
    {
        if (!IsFiniteNonNegative(halfExtents))
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents));
        }

        HalfExtents = halfExtents;
    }

    public Vector3 HalfExtents { get; }

    public static CombatHitbox Default =>
        new(new Vector3(0.5f, 0.5f, 0.5f));

    private static bool IsFiniteNonNegative(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        value.X >= 0.0f &&
        value.Y >= 0.0f &&
        value.Z >= 0.0f;
}
