using System.Numerics;

namespace ForgeLine.Presentation.EffectSubmission;

internal readonly record struct ParticleEffectDefinition(ParticleEmitterDescriptor Descriptor, Vector4 Tint,
    float Reach, float MaximumDistance, int AdmissionPriority, bool Supported);

internal static class ParticleEffectDefinitions
{
    internal static readonly ParticleEffectDefinition[] All = Create();

    private static ParticleEffectDefinition[] Create()
    {
        var definitions = new ParticleEffectDefinition[Enum.GetValues<VfxEffectKind>().Max(static kind => (int)kind) + 1];
        foreach (var source in VfxPresentationCatalog.All)
        {
            bool supported = ParticleEmitterDescriptor.TryResolve(source.Kind, out var descriptor);
            if (supported) descriptor.Validate();
            Vector4 tint = source.FallbackTint;
            tint.W = descriptor.Visual switch
            {
                ParticleVisualKind.Smoke => .24f,
                ParticleVisualKind.Dust => .18f,
                ParticleVisualKind.Spark => .65f,
                ParticleVisualKind.Fire => .48f,
                _ => .6f
            };
            float reach = descriptor.SpeedMetersPerSecond * descriptor.LifetimeSeconds +
                .5f * MathF.Abs(descriptor.Gravity) * descriptor.LifetimeSeconds * descriptor.LifetimeSeconds + 16;
            if (!supported) reach = MathF.Max(reach, source.BaseScale.Length() * 2);
            definitions[(int)source.Kind] = new(descriptor, tint, reach, source.MaximumDistanceMeters,
                Priority(source.Kind), supported);
        }
        return definitions;
    }

    private static int Priority(VfxEffectKind kind) => kind switch
    {
        VfxEffectKind.MuzzleInfantry or VfxEffectKind.MuzzleMachineGun or VfxEffectKind.MuzzleTankCannon or
        VfxEffectKind.MuzzleArtillery or VfxEffectKind.ProjectileBulletTracer or VfxEffectKind.ProjectileCannonShell or
        VfxEffectKind.ProjectileArtilleryShell or VfxEffectKind.ProjectileGenericExplosive or
        VfxEffectKind.ImpactDirt or VfxEffectKind.ImpactMetal or VfxEffectKind.ImpactConcrete or VfxEffectKind.ImpactArmor or
        VfxEffectKind.ImpactExplosive or VfxEffectKind.ExplosionSmall or VfxEffectKind.ExplosionMedium or
        VfxEffectKind.ExplosionVehicle or VfxEffectKind.ExplosionBuilding or VfxEffectKind.ExplosionAmmunitionSecondary or
        VfxEffectKind.DestructionVehicleBurst or VfxEffectKind.DestructionBuildingBurst => 0,
        VfxEffectKind.PersistentLightSmoke or VfxEffectKind.PersistentHeavySmoke or VfxEffectKind.PersistentFire or
        VfxEffectKind.DestructionPersistentFire or VfxEffectKind.LogisticsLoading or VfxEffectKind.LogisticsUnloading or
        VfxEffectKind.LogisticsResourceTransfer or VfxEffectKind.LogisticsSupplyTransfer or
        VfxEffectKind.LogisticsRefuel or VfxEffectKind.LogisticsRearm => 1,
        _ => 2
    };
}
