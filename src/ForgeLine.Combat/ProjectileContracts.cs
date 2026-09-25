using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Combat;

public readonly record struct ProjectileState
{
    public ProjectileState(
        EntityId source,
        FactionId faction,
        WeaponId weapon,
        Vector3 velocity,
        int remainingTicks,
        float radiusMeters,
        DamagePayload damage,
        bool hasImpacted = false)
    {
        if (!source.IsValid)
        {
            throw new ArgumentException(
                "Projectiles require a valid source entity.",
                nameof(source));
        }

        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Projectiles require a valid faction.",
                nameof(faction));
        }

        if (!weapon.IsSpecified)
        {
            throw new ArgumentException(
                "Projectiles require a stable weapon identifier.",
                nameof(weapon));
        }

        if (!IsFinite(velocity) || velocity.LengthSquared() <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(remainingTicks, 1);

        if (!float.IsFinite(radiusMeters) || radiusMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        Source = source;
        Faction = faction;
        Weapon = weapon;
        Velocity = velocity;
        RemainingTicks = remainingTicks;
        RadiusMeters = radiusMeters;
        Damage = damage;
        HasImpacted = hasImpacted;
    }

    public EntityId Source { get; init; }

    public FactionId Faction { get; init; }

    public WeaponId Weapon { get; init; }

    public Vector3 Velocity { get; init; }

    public int RemainingTicks { get; init; }

    public float RadiusMeters { get; init; }

    public DamagePayload Damage { get; init; }

    public bool HasImpacted { get; init; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
