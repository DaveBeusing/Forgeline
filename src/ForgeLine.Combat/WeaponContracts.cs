using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Combat;

public readonly record struct WeaponId(uint Value) : IComparable<WeaponId>
{
    public static WeaponId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(WeaponId other) => Value.CompareTo(other.Value);

    public static bool operator <(WeaponId left, WeaponId right) =>
        left.Value < right.Value;

    public static bool operator <=(WeaponId left, WeaponId right) =>
        left.Value <= right.Value;

    public static bool operator >(WeaponId left, WeaponId right) =>
        left.Value > right.Value;

    public static bool operator >=(WeaponId left, WeaponId right) =>
        left.Value >= right.Value;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum WeaponDeliveryModel : byte
{
    Hitscan = 0,
    PhysicalProjectile = 1
}

public sealed class WeaponDefinition
{
    public WeaponDefinition(
        WeaponId id,
        float rangeMeters,
        int fireIntervalTicks,
        double ammunitionPerShot,
        DamagePayload damage,
        WeaponDeliveryModel deliveryModel,
        int magazineSize = 1,
        int reloadTicks = 0,
        float projectileSpeedMetersPerSecond = 0.0f,
        float projectileRadiusMeters = 0.0f,
        int projectileLifetimeTicks = 0,
        WeaponEffectiveness? effectiveness = null)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentException(
                "Weapon definitions require a stable identifier.",
                nameof(id));
        }

        if (!float.IsFinite(rangeMeters) || rangeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeMeters));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(fireIntervalTicks, 1);

        if (!double.IsFinite(ammunitionPerShot) ||
            ammunitionPerShot <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ammunitionPerShot));
        }

        if (!Enum.IsDefined(deliveryModel))
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryModel));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(magazineSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(reloadTicks);

        if (deliveryModel == WeaponDeliveryModel.PhysicalProjectile)
        {
            if (!float.IsFinite(projectileSpeedMetersPerSecond) ||
                projectileSpeedMetersPerSecond <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(projectileSpeedMetersPerSecond));
            }

            if (!float.IsFinite(projectileRadiusMeters) ||
                projectileRadiusMeters <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(projectileRadiusMeters));
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(
                projectileLifetimeTicks,
                1);
        }

        Id = id;
        RangeMeters = rangeMeters;
        FireIntervalTicks = fireIntervalTicks;
        AmmunitionPerShot = ammunitionPerShot;
        Damage = damage;
        DeliveryModel = deliveryModel;
        MagazineSize = magazineSize;
        ReloadTicks = reloadTicks;
        ProjectileSpeedMetersPerSecond = projectileSpeedMetersPerSecond;
        ProjectileRadiusMeters = projectileRadiusMeters;
        ProjectileLifetimeTicks = projectileLifetimeTicks;
        Effectiveness = effectiveness ?? WeaponEffectiveness.GeneralPurpose;
    }

    public WeaponId Id { get; }

    public float RangeMeters { get; }

    public int FireIntervalTicks { get; }

    public double AmmunitionPerShot { get; }

    public DamagePayload Damage { get; }

    public WeaponDeliveryModel DeliveryModel { get; }

    public int MagazineSize { get; }

    public int ReloadTicks { get; }

    public float ProjectileSpeedMetersPerSecond { get; }

    public float ProjectileRadiusMeters { get; }

    public int ProjectileLifetimeTicks { get; }

    public WeaponEffectiveness Effectiveness { get; }
}

public sealed class WeaponCatalog
{
    private readonly Dictionary<WeaponId, WeaponDefinition> _definitions =
        new();

    public int Count => _definitions.Count;

    public void Add(WeaponDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions.Add(definition.Id, definition);
    }

    public bool TryGet(
        WeaponId id,
        out WeaponDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public WeaponDefinition GetRequired(WeaponId id)
    {
        if (_definitions.TryGetValue(id, out WeaponDefinition? definition))
        {
            return definition;
        }

        throw new KeyNotFoundException(
            $"Unknown weapon definition '{id}'.");
    }
}

public readonly record struct WeaponState
{
    public WeaponState(
        WeaponId weaponId,
        EntityId target,
        bool fireEnabled = true)
    {
        if (!weaponId.IsSpecified)
        {
            throw new ArgumentException(
                "Weapon state requires a stable weapon identifier.",
                nameof(weaponId));
        }

        WeaponId = weaponId;
        Target = target;
        FireEnabled = fireEnabled;
        NextFireTick = SimulationTick.Zero;
        ReloadUntilTick = SimulationTick.Zero;
        MagazineShotsRemaining = 0;
        MagazineInitialized = false;
    }

    public WeaponId WeaponId { get; init; }

    public EntityId Target { get; init; }

    public bool FireEnabled { get; init; }

    public SimulationTick NextFireTick { get; init; }

    public SimulationTick ReloadUntilTick { get; init; }

    public int MagazineShotsRemaining { get; init; }

    public bool MagazineInitialized { get; init; }

    public bool CanAttemptFire(SimulationTick tick) =>
        FireEnabled &&
        Target.IsValid &&
        tick >= NextFireTick &&
        tick >= ReloadUntilTick;
}

public readonly record struct Combatant
{
    public Combatant(FactionId faction)
    {
        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Combatants require a valid faction.",
                nameof(faction));
        }

        Faction = faction;
    }

    public FactionId Faction { get; }
}
