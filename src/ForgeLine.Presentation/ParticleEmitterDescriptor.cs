using System.Numerics;

namespace ForgeLine.Presentation;

public enum ParticleQuality { Disabled, Low, Medium, High }
public enum ParticleVisualKind { Smoke, Dust, Spark, Explosion }

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
            default: return false;
        }
        float radius = Math.Clamp(source.BaseScale.Length() * .22f, .18f, 3);
        descriptor = new(1, effect, source.MeshAssetId, source.MaterialAssetId, visual,
            visual == ParticleVisualKind.Explosion ? 16 : 8,
            visual is ParticleVisualKind.Smoke or ParticleVisualKind.Dust ? 1.5f : .6f,
            radius, visual == ParticleVisualKind.Spark ? 9 : 2,
            visual == ParticleVisualKind.Spark ? -9.8f : .4f,
            visual == ParticleVisualKind.Explosion ? 0 : visual == ParticleVisualKind.Spark ? 2 : 1);
        return true;
    }
}

public readonly record struct ParticleSettings(ParticleQuality Quality)
{
    public static ParticleSettings Default => new(ParticleQuality.Medium);
    public int Capacity => Quality switch { ParticleQuality.Low => 512, ParticleQuality.Medium => 2048, ParticleQuality.High => 4096, _ => 0 };
    public float MaximumScreenCoverage => Quality switch { ParticleQuality.Low => .5f, ParticleQuality.Medium => 1, ParticleQuality.High => 2, _ => 0 };
    public void Validate() { if (!Enum.IsDefined(Quality)) throw new ArgumentOutOfRangeException(nameof(Quality)); }
}
