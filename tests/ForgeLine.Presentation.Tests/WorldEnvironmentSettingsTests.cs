using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldEnvironmentSettingsTests
{
    [Fact]
    public void NeutralAppearanceIsExactAndDaylightRetainsBoundedVisibility()
    {
        var neutral = new WorldEnvironmentSettings();
        Assert.Equal(SceneLightingSettings.Default, neutral.ResolveLighting(SceneLightingSettings.Default));
        var dawn = (neutral with { Daylight = 0, CloudCover = 1, Wetness = 1 }).ResolveLighting(SceneLightingSettings.Default);
        dawn.Validate();
        Assert.InRange(dawn.DirectionalIntensity, .6f, 1.1f);
        Assert.InRange(dawn.AmbientIntensity, .4f, .52f);
        Assert.InRange(dawn.DirectionToLight.Y, .33f, .35f);
        Assert.Equal(SceneLightingSettings.Default.Exposure, dawn.Exposure);
        var requested = new RenderQualitySettings { Quality = RenderQuality.Ultra, Environment = new() { CloudCover = 1, Wetness = 1 } };
        var software = requested.Resolve(true, true, 640, 360, 3);
        Assert.Equal(0, software.Environment.CloudCover);
        Assert.Equal(0, software.Environment.Wetness);
        Assert.Equal(requested, software.RequestedSettings);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-.01f)] [InlineData(1.01f)]
    public void InvalidEnvironmentCannotReachShaderConstruction(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldEnvironmentSettings { Wetness = value }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldEnvironmentSettings { CloudCover = value }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldEnvironmentSettings { Daylight = value }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new VegetationRenderSettings { Density = value }.Validate());
    }

    [Fact]
    public void DensityIsStablePerIdentityAndNeverFiltersUnitsOrChangesSnapshots()
    {
        var reduced = new VegetationRenderSettings { Density = .5f, MaximumDistanceMeters = 1000 };
        int included = 0;
        for (uint identity = 1; identity <= 1000; identity++)
        {
            var plant = new RenderInstance(new EntityId(identity, 1), new(Vector3.Zero, Quaternion.Identity, Vector3.One),
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, identity,
                WorldFeature: new(WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, default, false));
            RenderInstance original = plant;
            bool first = reduced.Includes(plant, Vector3.Zero);
            Assert.Equal(first, reduced.Includes(plant, Vector3.Zero));
            Assert.Equal(original, plant);
            if (first) included++;
            Assert.True(reduced.Includes(plant with { WorldFeature = default }, Vector3.Zero));
            Assert.False(reduced.Includes(plant with { Transform = plant.Transform with { Position = new(2000, 0, 0) } }, Vector3.Zero));
        }
        Assert.InRange(included, 450, 550);
    }

    [Fact]
    public void AuthoredGroundWearIsCopiedPrioritizedAtChunkEdgesAndNormalized()
    {
        TerrainSurfaceMark[] input = [new(TerrainSurfaceMarkKind.RoadWear, new(240, 128), new(272, 128), 6)];
        var marks = new TerrainSurfaceMarks(input);
        input[0] = default;
        var original = TerrainPresentationProfile.CreateCentralDivide();
        var profile = original.WithSurfaceMarks(marks);
        Assert.Equal(0, original.SurfaceMarkCount);
        Assert.Equal(1, profile.SurfaceMarkCount);
        for (int x = 0; x <= 1; x++)
        {
            var chunk = new TerrainChunk(new(x, 0), new TerrainHeightfield(2, 256, new float[4]));
            var splat = TerrainSplatMapBuilder.Build(chunk, profile);
            Assert.Contains(TerrainMaterialSlot.IndustrialGround, splat.Palette);
            Assert.All(splat.Mips, mip =>
            {
                for (int index = 0; index < mip.Pixels.Length; index += 4)
                    Assert.Equal(255, mip.Pixels[index] + mip.Pixels[index + 1] + mip.Pixels[index + 2] + mip.Pixels[index + 3]);
            });
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainSurfaceMarks(new TerrainSurfaceMark[65]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainSurfaceMarks([new(TerrainSurfaceMarkKind.TireTrack, Vector2.Zero, Vector2.One, float.NaN)]));
    }
}
