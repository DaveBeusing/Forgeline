using System.Numerics;

namespace ForgeLine.Presentation;

public enum ParticleQuality { Disabled, Low, Medium, High }
public enum ParticleVisualKind { Smoke, Dust, Spark, Explosion, Fire, Muzzle }

public readonly record struct ParticleEmitterDescriptor(int Version, VfxEffectKind Effect, string MeshAssetId,
    string MaterialAssetId, ParticleVisualKind Visual, int Count, float LifetimeSeconds, float RadiusMeters,
    float SpeedMetersPerSecond, float Gravity, int Priority)
{
    public void Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Visual) || Count is < 1 or > 32 || Priority is < 0 or > 2)
            throw new ArgumentException("Unsupported particle descriptor version or budget.");
        VfxPresentationDefinition source = VfxPresentationCatalog.Get(Effect);
        if (source.MeshAssetId != MeshAssetId || source.MaterialAssetId != MaterialAssetId)
            throw new ArgumentException("Particle descriptors must retain the existing effect asset identity.");
        if (!float.IsFinite(LifetimeSeconds) || LifetimeSeconds is < .05f or > 4 ||
            !float.IsFinite(RadiusMeters) || RadiusMeters is <= 0 or > 8 ||
            !float.IsFinite(SpeedMetersPerSecond) || SpeedMetersPerSecond is < 0 or > 40 ||
            !float.IsFinite(Gravity) || MathF.Abs(Gravity) > 20)
            throw new ArgumentException("Particle motion parameters exceed the visual budget.");
    }

    public static bool TryResolve(VfxEffectKind effect, out ParticleEmitterDescriptor descriptor)
    {
        descriptor = default;
        if (!VfxPresentationCatalog.TryGet(effect, out var source)) return false;
        ParticleVisualKind visual;
        switch (source.MaterialAssetId)
        {
            case "material.vfx.smoke": visual = ParticleVisualKind.Smoke; break;
            case "material.vfx.dust": case "material.vfx.impact.dirt": visual = ParticleVisualKind.Dust; break;
            case "material.vfx.spark": case "material.vfx.impact.metal": visual = ParticleVisualKind.Spark; break;
            case "material.vfx.explosion": visual = ParticleVisualKind.Explosion; break;
            case "material.vfx.fire": visual = ParticleVisualKind.Fire; break;
            case "material.vfx.muzzle" when effect is VfxEffectKind.MuzzleTankCannon or VfxEffectKind.MuzzleArtillery:
                visual = ParticleVisualKind.Muzzle; break;
            default: return false;
        }
        float radius = Math.Clamp(source.BaseScale.Length() * .22f, .18f, 3);
        (float life, float speed, float gravity) = visual switch
        {
            ParticleVisualKind.Smoke => (2.8f, 1.2f, .3f),
            ParticleVisualKind.Dust => (1.5f, 1.4f, .05f),
            ParticleVisualKind.Spark => (.45f, 7f, -9.8f),
            ParticleVisualKind.Fire => (1.2f, 1.8f, .2f),
            ParticleVisualKind.Muzzle => (.15f, 12f, 0f),
            _ => (.6f, 4f, .3f)
        };
        descriptor = new(1, effect, source.MeshAssetId, source.MaterialAssetId, visual,
            visual == ParticleVisualKind.Explosion ? 16 : 8, life, radius, speed, gravity,
            visual is ParticleVisualKind.Explosion or ParticleVisualKind.Muzzle ? 0 : visual == ParticleVisualKind.Spark ? 2 : 1);
        return true;
    }

    internal Vector3 InitialVelocity(float angle, float spread, Quaternion rotation)
    {
        Vector3 direction = Visual switch
        {
            ParticleVisualKind.Dust => new(MathF.Cos(angle), .08f + spread * .12f, MathF.Sin(angle)),
            ParticleVisualKind.Smoke or ParticleVisualKind.Fire => new(MathF.Cos(angle) * .18f, 1, MathF.Sin(angle) * .18f),
            ParticleVisualKind.Muzzle => Vector3.Transform(new Vector3(MathF.Cos(angle) * .18f, MathF.Sin(angle) * .18f, 1), rotation),
            _ => new(MathF.Cos(angle), .2f + spread, MathF.Sin(angle))
        };
        // Speed is a magnitude limit, also used by conservative culling/fill estimates.
        return Vector3.Normalize(direction) * SpeedMetersPerSecond * (.4f + spread * .6f);
    }
}

public readonly record struct ParticleSettings(ParticleQuality Quality)
{
    public static ParticleSettings Default => new(ParticleQuality.Medium);
    public int Capacity => Quality switch { ParticleQuality.Low => 512, ParticleQuality.Medium => 2048, ParticleQuality.High => 4096, _ => 0 };
    public float MaximumScreenCoverage => Quality switch { ParticleQuality.Low => .5f, ParticleQuality.Medium => 1, ParticleQuality.High => 2, _ => 0 };
    public void Validate() { if (!Enum.IsDefined(Quality)) throw new ArgumentOutOfRangeException(nameof(Quality)); }
}
