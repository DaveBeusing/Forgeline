using Xunit;
using System.Numerics;

namespace ForgeLine.Presentation.Tests;

public sealed class ParticleEmitterDescriptorTests
{
    [Fact]
    public void SpawnDirectionsRespectConservativeReachAndHeavyMuzzleAim()
    {
        foreach (var source in VfxPresentationCatalog.All)
        {
            if (!ParticleEmitterDescriptor.TryResolve(source.Kind, out var descriptor)) continue;
            for (int i = 0; i < 32; i++)
            {
                Vector3 velocity = descriptor.InitialVelocity(i * .3f, i / 31f, Quaternion.Identity);
                Assert.InRange(velocity.Length(), 0, descriptor.SpeedMetersPerSecond + .0001f);
                if (descriptor.Visual == ParticleVisualKind.Dust) Assert.InRange(velocity.Y, 0, .3f);
            }
        }
        Assert.True(ParticleEmitterDescriptor.TryResolve(VfxEffectKind.MuzzleTankCannon, out var muzzle));
        Vector3 aimed = muzzle.InitialVelocity(0, 1, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        Assert.True(aimed.X > MathF.Abs(aimed.Z) * 4);
        Assert.True(ParticleEmitterDescriptor.TryResolve(VfxEffectKind.PersistentFire, out var fire));
        Assert.Equal(ParticleVisualKind.Fire, fire.Visual);
    }

    [Fact]
    public void DescriptorsReuseExistingAssetsAndBoundMotion()
    {
        int count = 0;
        foreach (var source in VfxPresentationCatalog.All)
        {
            if (!ParticleEmitterDescriptor.TryResolve(source.Kind, out var descriptor)) continue;
            descriptor.Validate(); count++;
            Assert.Equal(source.MeshAssetId, descriptor.MeshAssetId);
            Assert.Equal(source.MaterialAssetId, descriptor.MaterialAssetId);
            Assert.Throws<ArgumentException>(() => (descriptor with { Version = 2 }).Validate());
            Assert.Throws<ArgumentException>(() => (descriptor with { Count = 33 }).Validate());
            Assert.Throws<ArgumentException>(() => (descriptor with { LifetimeSeconds = float.NaN }).Validate());
            Assert.Throws<ArgumentException>(() => (descriptor with { MaterialAssetId = "material.missing" }).Validate());
        }
        Assert.InRange(count, 15, 30);
        Assert.False(ParticleEmitterDescriptor.TryResolve(VfxEffectKind.ProjectileBulletTracer, out _));
        Assert.Equal(0, new ParticleSettings(ParticleQuality.Disabled).Capacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParticleSettings((ParticleQuality)255).Validate());
    }
}
