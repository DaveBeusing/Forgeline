using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Combat;

public enum TargetClass : byte
{
    Infantry = 0,
    LightVehicle = 1,
    ArmoredVehicle = 2,
    Structure = 3
}

[Flags]
public enum TargetClassMask : byte
{
    None = 0,
    Infantry = 1 << 0,
    LightVehicle = 1 << 1,
    ArmoredVehicle = 1 << 2,
    Structure = 1 << 3,
    All = Infantry | LightVehicle | ArmoredVehicle | Structure
}

public static class TargetClassRules
{
    public static TargetClassMask ToMask(TargetClass targetClass) =>
        targetClass switch
        {
            TargetClass.Infantry => TargetClassMask.Infantry,
            TargetClass.LightVehicle => TargetClassMask.LightVehicle,
            TargetClass.ArmoredVehicle => TargetClassMask.ArmoredVehicle,
            TargetClass.Structure => TargetClassMask.Structure,
            _ => throw new ArgumentOutOfRangeException(nameof(targetClass))
        };
}

public readonly record struct WeaponEffectiveness
{
    public WeaponEffectiveness(
        TargetClassMask validTargets,
        double penetration)
    {
        if (validTargets == TargetClassMask.None ||
            (validTargets & ~TargetClassMask.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(validTargets));
        }

        if (!double.IsFinite(penetration) || penetration <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(penetration));
        }

        ValidTargets = validTargets;
        Penetration = penetration;
    }

    public TargetClassMask ValidTargets { get; }

    public double Penetration { get; }

    public static WeaponEffectiveness GeneralPurpose =>
        new(TargetClassMask.All, double.MaxValue);

    public bool CanEngage(TargetClass targetClass) =>
        (ValidTargets & TargetClassRules.ToMask(targetClass)) != 0;
}

public readonly record struct Targetable(TargetClass Class);

public readonly record struct TargetPriority(int Value);

public readonly record struct AutoTargetState(bool Enabled)
{
    public static AutoTargetState EnabledByDefault => new(true);
}

public enum FirePolicy : byte
{
    HoldFire = 0,
    ReturnFire = 1,
    FireAtWill = 2
}

public readonly record struct FirePolicyState
{
    public FirePolicyState(
        FirePolicy policy,
        EntityId retaliationTarget = default)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        Policy = policy;
        RetaliationTarget = retaliationTarget;
    }

    public FirePolicy Policy { get; init; }

    public EntityId RetaliationTarget { get; init; }

    public static FirePolicyState FireAtWill =>
        new(FirePolicy.FireAtWill);

    public bool Permits(EntityId target) =>
        Policy switch
        {
            FirePolicy.HoldFire => false,
            FirePolicy.ReturnFire =>
                target.IsValid &&
                target == RetaliationTarget,
            FirePolicy.FireAtWill => true,
            _ => false
        };
}

public enum TargetRejectionReason : byte
{
    None = 0,
    Dead = 1,
    Friendly = 2,
    NotTargetable = 3,
    UnsupportedTargetClass = 4,
    OutOfRange = 5,
    UnavailableIntelligence = 6,
    BlockedLineOfFire = 7,
    FirePolicy = 8,
    MissingHealth = 9,
    MissingTransform = 10
}

public interface ITargetAvailabilityPolicy
{
    bool IsTargetAvailable(EntityId observer, EntityId target);
}

public sealed class AlwaysTargetAvailablePolicy : ITargetAvailabilityPolicy
{
    public static AlwaysTargetAvailablePolicy Instance { get; } = new();

    private AlwaysTargetAvailablePolicy()
    {
    }

    public bool IsTargetAvailable(EntityId observer, EntityId target) =>
        observer.IsValid &&
        target.IsValid;
}

public interface ILineOfFirePolicy
{
    bool HasLineOfFire(
        EntityId source,
        EntityId target,
        Vector3 sourcePosition,
        Vector3 targetPosition);
}

public sealed class UnobstructedLineOfFirePolicy : ILineOfFirePolicy
{
    public static UnobstructedLineOfFirePolicy Instance { get; } = new();

    private UnobstructedLineOfFirePolicy()
    {
    }

    public bool HasLineOfFire(
        EntityId source,
        EntityId target,
        Vector3 sourcePosition,
        Vector3 targetPosition) =>
        source.IsValid &&
        target.IsValid;
}
