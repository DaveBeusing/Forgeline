using ForgeLine.Core;

namespace ForgeLine.Game;

public enum DeveloperEffectKind : byte { None, Parameter, ResourceQuantity, SpawnedEntity, Paused, HostSpeed, AdvancedTicks, InitialProfile, HealthRestored, HealthDamage }

/// <summary>Copied actual outcome. A domain provider, not a requested payload, supplies these facts.</summary>
public readonly record struct DeveloperAppliedValue
{
    private DeveloperAppliedValue(DeveloperEffectKind kind, DeveloperTarget target, DeveloperValue value,
        DeveloperParameterId parameter = DeveloperParameterId.None, ResourceId resource = default)
    { Kind = kind; Target = target; Value = value; Parameter = parameter; Resource = resource; }
    public DeveloperEffectKind Kind { get; }
    public DeveloperTarget Target { get; }
    public DeveloperValue Value { get; }
    public DeveloperParameterId Parameter { get; }
    public ResourceId Resource { get; }
    public bool IsValid => Kind switch
    {
        DeveloperEffectKind.Parameter => !Resource.IsSpecified && DeveloperParameterCatalog.TryGet(Parameter, out var descriptor) && descriptor.Accepts(Value, Target),
        DeveloperEffectKind.ResourceQuantity => Parameter == DeveloperParameterId.None && Resource.IsSpecified && Target.IsValid &&
            Value.Kind == DeveloperValueKind.RealNumber && Value.AsDouble() >= 0,
        DeveloperEffectKind.SpawnedEntity => Target.Kind == DeveloperTargetKinds.Entity && Target.IsValid && Value == DeveloperValue.FromUInt32(1),
        DeveloperEffectKind.HealthRestored or DeveloperEffectKind.HealthDamage => Target.Kind == DeveloperTargetKinds.Entity && Target.IsValid &&
            Parameter == DeveloperParameterId.None && !Resource.IsSpecified && Value.Kind == DeveloperValueKind.RealNumber && Value.AsDouble() >= 0,
        DeveloperEffectKind.Paused => Target == DeveloperTarget.Global && Value.Kind == DeveloperValueKind.Boolean,
        DeveloperEffectKind.HostSpeed => Target == DeveloperTarget.Global && Value.Kind == DeveloperValueKind.RealNumber && Value.AsDouble() > 0,
        DeveloperEffectKind.AdvancedTicks or DeveloperEffectKind.InitialProfile => Target == DeveloperTarget.Global && Value.Kind == DeveloperValueKind.UnsignedNumber && Value.AsUInt32() > 0,
        _ => false
    };
    public static DeveloperAppliedValue ForParameter(DeveloperParameterId id, DeveloperTarget target, DeveloperValue value) =>
        Checked(new(DeveloperEffectKind.Parameter, target, value, id));
    public static DeveloperAppliedValue ForResource(ResourceId resource, DeveloperTarget target, double quantity) =>
        Checked(new(DeveloperEffectKind.ResourceQuantity, target, DeveloperValue.FromDouble(quantity), resource: resource));
    public static DeveloperAppliedValue ForSpawnedEntity(EntityId entity) =>
        Checked(new(DeveloperEffectKind.SpawnedEntity, DeveloperTarget.ForEntity(entity), DeveloperValue.FromUInt32(1)));
    public static DeveloperAppliedValue ForHealth(DeveloperEffectKind kind, EntityId entity, double amount) =>
        kind is DeveloperEffectKind.HealthRestored or DeveloperEffectKind.HealthDamage
            ? Checked(new(kind, DeveloperTarget.ForEntity(entity), DeveloperValue.FromDouble(amount)))
            : throw new ArgumentOutOfRangeException(nameof(kind));
    public static DeveloperAppliedValue ForSession(DeveloperEffectKind kind, DeveloperValue value) =>
        kind is DeveloperEffectKind.Paused or DeveloperEffectKind.HostSpeed or DeveloperEffectKind.AdvancedTicks or DeveloperEffectKind.InitialProfile
            ? Checked(new(kind, DeveloperTarget.Global, value)) : throw new ArgumentOutOfRangeException(nameof(kind));
    private static DeveloperAppliedValue Checked(DeveloperAppliedValue value) => value.IsValid ? value :
        throw new ArgumentException("Invalid applied developer value.", nameof(value));
}
