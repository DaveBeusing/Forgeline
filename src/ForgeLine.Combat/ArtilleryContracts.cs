using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Combat;

public sealed class ArtilleryWeaponDefinition
{
    public ArtilleryWeaponDefinition(
        WeaponId id,
        float minimumRangeMeters,
        float maximumRangeMeters,
        int fireIntervalTicks,
        int acquisitionTicks,
        double ammunitionPerShot,
        DamagePayload damage,
        float areaRadiusMeters,
        double minimumDamageFraction,
        float projectileSpeedMetersPerSecond,
        float apexHeightMeters,
        float dispersionRadiusMeters = 0.0f,
        WeaponEffectiveness? effectiveness = null)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentException(
                "Artillery definitions require a stable weapon identifier.",
                nameof(id));
        }

        if (!float.IsFinite(minimumRangeMeters) ||
            minimumRangeMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRangeMeters));
        }

        if (!float.IsFinite(maximumRangeMeters) ||
            maximumRangeMeters <= minimumRangeMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRangeMeters));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(fireIntervalTicks, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(acquisitionTicks);

        if (!double.IsFinite(ammunitionPerShot) ||
            ammunitionPerShot <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ammunitionPerShot));
        }

        if (!float.IsFinite(areaRadiusMeters) ||
            areaRadiusMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(areaRadiusMeters));
        }

        if (!double.IsFinite(minimumDamageFraction) ||
            minimumDamageFraction < 0.0 ||
            minimumDamageFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDamageFraction));
        }

        if (!float.IsFinite(projectileSpeedMetersPerSecond) ||
            projectileSpeedMetersPerSecond <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectileSpeedMetersPerSecond));
        }

        if (!float.IsFinite(apexHeightMeters) ||
            apexHeightMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(apexHeightMeters));
        }

        if (!float.IsFinite(dispersionRadiusMeters) ||
            dispersionRadiusMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(dispersionRadiusMeters));
        }

        Id = id;
        MinimumRangeMeters = minimumRangeMeters;
        MaximumRangeMeters = maximumRangeMeters;
        FireIntervalTicks = fireIntervalTicks;
        AcquisitionTicks = acquisitionTicks;
        AmmunitionPerShot = ammunitionPerShot;
        Damage = damage;
        AreaRadiusMeters = areaRadiusMeters;
        MinimumDamageFraction = minimumDamageFraction;
        ProjectileSpeedMetersPerSecond = projectileSpeedMetersPerSecond;
        ApexHeightMeters = apexHeightMeters;
        DispersionRadiusMeters = dispersionRadiusMeters;
        Effectiveness = effectiveness ?? WeaponEffectiveness.GeneralPurpose;
    }

    public WeaponId Id { get; }

    public float MinimumRangeMeters { get; }

    public float MaximumRangeMeters { get; }

    public int FireIntervalTicks { get; }

    public int AcquisitionTicks { get; }

    public double AmmunitionPerShot { get; }

    public DamagePayload Damage { get; }

    public float AreaRadiusMeters { get; }

    public double MinimumDamageFraction { get; }

    public float ProjectileSpeedMetersPerSecond { get; }

    public float ApexHeightMeters { get; }

    public float DispersionRadiusMeters { get; }

    public WeaponEffectiveness Effectiveness { get; }

    public bool IsInRange(Vector3 source, Vector3 target)
    {
        float x = target.X - source.X;
        float z = target.Z - source.Z;
        float distanceSquared = x * x + z * z;

        return distanceSquared >=
                   MinimumRangeMeters * MinimumRangeMeters &&
               distanceSquared <=
                   MaximumRangeMeters * MaximumRangeMeters;
    }
}

public sealed class ArtilleryWeaponCatalog
{
    private readonly Dictionary<WeaponId, ArtilleryWeaponDefinition> _definitions =
        new();

    public int Count => _definitions.Count;

    public void Add(ArtilleryWeaponDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions.Add(definition.Id, definition);
    }

    public bool TryGet(
        WeaponId id,
        out ArtilleryWeaponDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public ArtilleryWeaponDefinition GetRequired(WeaponId id)
    {
        if (_definitions.TryGetValue(
                id,
                out ArtilleryWeaponDefinition? definition))
        {
            return definition;
        }

        throw new KeyNotFoundException(
            $"Unknown artillery definition '{id}'.");
    }
}

public readonly record struct ArtilleryCapability
{
    public ArtilleryCapability(WeaponId weaponId)
    {
        if (!weaponId.IsSpecified)
        {
            throw new ArgumentException(
                "Artillery capability requires a weapon identifier.",
                nameof(weaponId));
        }

        WeaponId = weaponId;
    }

    public WeaponId WeaponId { get; }
}

public readonly record struct IndirectFireProjectileState
{
    public IndirectFireProjectileState(
        EntityId source,
        FactionId faction,
        WeaponId weapon,
        Vector3 launchPosition,
        Vector3 targetPosition,
        SimulationTick launchTick,
        SimulationTick impactTick,
        float apexHeightMeters,
        float areaRadiusMeters,
        double minimumDamageFraction,
        DamagePayload damage,
        bool hasImpacted = false)
    {
        if (!source.IsValid)
        {
            throw new ArgumentException(
                "Indirect projectiles require a valid source.",
                nameof(source));
        }

        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Indirect projectiles require a valid faction.",
                nameof(faction));
        }

        if (!weapon.IsSpecified)
        {
            throw new ArgumentException(
                "Indirect projectiles require a weapon identifier.",
                nameof(weapon));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            impactTick,
            launchTick);

        if (!IsFinite(launchPosition) ||
            !IsFinite(targetPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(targetPosition));
        }

        if (!float.IsFinite(apexHeightMeters) ||
            apexHeightMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(apexHeightMeters));
        }

        if (!float.IsFinite(areaRadiusMeters) ||
            areaRadiusMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(areaRadiusMeters));
        }

        if (!double.IsFinite(minimumDamageFraction) ||
            minimumDamageFraction < 0.0 ||
            minimumDamageFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDamageFraction));
        }

        Source = source;
        Faction = faction;
        Weapon = weapon;
        LaunchPosition = launchPosition;
        TargetPosition = targetPosition;
        LaunchTick = launchTick;
        ImpactTick = impactTick;
        ApexHeightMeters = apexHeightMeters;
        AreaRadiusMeters = areaRadiusMeters;
        MinimumDamageFraction = minimumDamageFraction;
        Damage = damage;
        HasImpacted = hasImpacted;
    }

    public EntityId Source { get; init; }

    public FactionId Faction { get; init; }

    public WeaponId Weapon { get; init; }

    public Vector3 LaunchPosition { get; init; }

    public Vector3 TargetPosition { get; init; }

    public SimulationTick LaunchTick { get; init; }

    public SimulationTick ImpactTick { get; init; }

    public float ApexHeightMeters { get; init; }

    public float AreaRadiusMeters { get; init; }

    public double MinimumDamageFraction { get; init; }

    public DamagePayload Damage { get; init; }

    public bool HasImpacted { get; init; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
