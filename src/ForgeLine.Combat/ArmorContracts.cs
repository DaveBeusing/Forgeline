using System.Numerics;

namespace ForgeLine.Combat;

public readonly record struct ArmorProfileId(uint Value) : IComparable<ArmorProfileId>
{
    public static ArmorProfileId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(ArmorProfileId other) => Value.CompareTo(other.Value);

    public static bool operator <(ArmorProfileId left, ArmorProfileId right) =>
        left.Value < right.Value;

    public static bool operator <=(ArmorProfileId left, ArmorProfileId right) =>
        left.Value <= right.Value;

    public static bool operator >(ArmorProfileId left, ArmorProfileId right) =>
        left.Value > right.Value;

    public static bool operator >=(ArmorProfileId left, ArmorProfileId right) =>
        left.Value >= right.Value;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum ArmorZone : byte
{
    Front = 0,
    Side = 1,
    Rear = 2,
    Top = 3
}

public sealed class ArmorProfileDefinition
{
    public ArmorProfileDefinition(
        ArmorProfileId id,
        double front,
        double side,
        double rear,
        double top = 0.0)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentException(
                "Armor profiles require a stable identifier.",
                nameof(id));
        }

        ValidateArmor(front, nameof(front));
        ValidateArmor(side, nameof(side));
        ValidateArmor(rear, nameof(rear));
        ValidateArmor(top, nameof(top));

        Id = id;
        Front = front;
        Side = side;
        Rear = rear;
        Top = top;
    }

    public ArmorProfileId Id { get; }

    public double Front { get; }

    public double Side { get; }

    public double Rear { get; }

    public double Top { get; }

    public double GetArmor(ArmorZone zone) =>
        zone switch
        {
            ArmorZone.Front => Front,
            ArmorZone.Side => Side,
            ArmorZone.Rear => Rear,
            ArmorZone.Top => Top,
            _ => throw new ArgumentOutOfRangeException(nameof(zone))
        };

    private static void ValidateArmor(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class ArmorCatalog
{
    private readonly Dictionary<ArmorProfileId, ArmorProfileDefinition> _definitions =
        new();

    public int Count => _definitions.Count;

    public void Add(ArmorProfileDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions.Add(definition.Id, definition);
    }

    public bool TryGet(
        ArmorProfileId id,
        out ArmorProfileDefinition? definition) =>
        _definitions.TryGetValue(id, out definition);

    public ArmorProfileDefinition GetRequired(ArmorProfileId id)
    {
        if (_definitions.TryGetValue(id, out ArmorProfileDefinition? definition))
        {
            return definition;
        }

        throw new KeyNotFoundException(
            $"Unknown armor profile '{id}'.");
    }
}

public readonly record struct ArmorState
{
    public ArmorState(ArmorProfileId profileId)
    {
        if (!profileId.IsSpecified)
        {
            throw new ArgumentException(
                "Armor state requires a stable armor profile identifier.",
                nameof(profileId));
        }

        ProfileId = profileId;
    }

    public ArmorProfileId ProfileId { get; }
}

public static class ArmorFacing
{
    private const float DirectionEpsilonSquared = 0.000001f;
    private const float TopSourceThreshold = 0.70710677f;
    private const float FrontRearThreshold = 0.70710677f;

    public static ArmorZone Classify(
        Quaternion targetRotation,
        Vector3 incomingDirection)
    {
        Vector3 direction = NormalizeOrDefault(incomingDirection);
        Vector3 sourceDirection = -direction;

        if (sourceDirection.Y >= TopSourceThreshold)
        {
            return ArmorZone.Top;
        }

        Vector3 forward =
            Vector3.Transform(
                Vector3.UnitZ,
                targetRotation);
        forward.Y = 0.0f;
        forward = NormalizeOrDefault(forward);

        Vector3 horizontalSource =
            new(
                sourceDirection.X,
                0.0f,
                sourceDirection.Z);
        horizontalSource = NormalizeOrDefault(horizontalSource);

        float alignment =
            Vector3.Dot(
                forward,
                horizontalSource);

        if (alignment >= FrontRearThreshold)
        {
            return ArmorZone.Front;
        }

        return alignment <= -FrontRearThreshold
            ? ArmorZone.Rear
            : ArmorZone.Side;
    }

    private static Vector3 NormalizeOrDefault(Vector3 value)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            value.LengthSquared() <= DirectionEpsilonSquared)
        {
            return Vector3.UnitZ;
        }

        return Vector3.Normalize(value);
    }
}

public readonly record struct ArmorDamageResult(
    ArmorZone Zone,
    double RawDamage,
    double AppliedDamage,
    double ArmorValue,
    double Penetration)
{
    public double MitigatedDamage =>
        Math.Max(0.0, RawDamage - AppliedDamage);

    public double DamageFraction =>
        RawDamage <= 0.0
            ? 0.0
            : AppliedDamage / RawDamage;
}

public static class ArmorDamageResolver
{
    public static ArmorDamageResult Resolve(
        in DamagePayload damage,
        in WeaponEffectiveness effectiveness,
        ArmorProfileDefinition armor,
        ArmorZone zone)
    {
        ArgumentNullException.ThrowIfNull(armor);

        double armorValue = armor.GetArmor(zone);
        double appliedDamage =
            armorValue <= 0.0
                ? damage.Amount
                : damage.Amount *
                  Math.Clamp(
                      effectiveness.Penetration / armorValue,
                      0.0,
                      1.0);

        return new ArmorDamageResult(
            zone,
            damage.Amount,
            appliedDamage,
            armorValue,
            effectiveness.Penetration);
    }
}
