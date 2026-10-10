using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ParticleEmitterDescriptorTests
{
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
