using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class ProceduralTerrainTests
{
    [Fact]
    public void VersionOneHasFixedHeightMaskAndTerrainDigestVectors()
    {
        var recipe = Recipe();
        var evaluator = new ProceduralTerrainEvaluator(recipe);
        Assert.Equal(new ProceduralTerrainSample(25688, 915, 0), evaluator.Evaluate(new(0, 0)));
        Assert.Equal(new ProceduralTerrainSample(25689, 915, 0), evaluator.Evaluate(new(-1, 0)));
        Assert.Equal(new ProceduralTerrainSample(27474, 901, 0), evaluator.Evaluate(new(-65, 17)));
        Assert.Equal(new ProceduralTerrainSample(22186, 82, 0), evaluator.Evaluate(new(262144, -262144)));
        Assert.Equal("13253f1ba5c44650be7c6e02af3f6ba4fe4768b3509badd0f6beef05cb66a1e3",
            ProceduralTerrainFactory.Create(recipe).Diagnostics.TerrainSha256);
    }

    [Theory]
    [InlineData(-4096)]
    [InlineData(4095)]
    public void ExtremeSupportedOriginsRemainQueryable(int origin)
    {
        var recipe = Recipe(0) with
        {
            Generation = Recipe(0).Generation with { Origin = new(origin, origin), WidthInChunks = 1, HeightInChunks = 1 }
        };
        var output = ProceduralTerrainFactory.Create(recipe);
        float x = origin * 256f + 128;
        Assert.True(output.Terrain.TrySampleHeight(x, x, out float height));
        Assert.True(float.IsFinite(height));
        Assert.InRange(output.Diagnostics.MaximumSampleGradient, 0, recipe.MaximumGradient);
    }

    private static ProceduralTerrainRecipe Recipe(ulong seed = 17) => new()
    {
        Generation = new() { TerrainProfileId = ProceduralTerrainRecipe.ProfileId,
            Seed = seed, Origin = new(-2, -2), WidthInChunks = 4, HeightInChunks = 4 }
    };

    [Fact]
    public void NegativeAndPositiveChunkEdgesAreBitIdenticalAndQueryable()
    {
        var output = ProceduralTerrainFactory.Create(Recipe());
        Assert.Equal(0, output.Diagnostics.SeamMismatchCount);
        foreach (var chunk in output.Terrain.Chunks)
        {
            if (output.Terrain.TryGetChunk(new(chunk.Coordinate.X + 1, chunk.Coordinate.Z), out var right))
                for (int i = 0; i < 65; i++)
                    Assert.Equal(BitConverter.SingleToInt32Bits(chunk.Heightfield.GetHeight(64, i)),
                        BitConverter.SingleToInt32Bits(right.Heightfield.GetHeight(0, i)));
            if (output.Terrain.TryGetChunk(new(chunk.Coordinate.X, chunk.Coordinate.Z + 1), out var bottom))
                for (int i = 0; i < 65; i++)
                    Assert.Equal(BitConverter.SingleToInt32Bits(chunk.Heightfield.GetHeight(i, 64)),
                        BitConverter.SingleToInt32Bits(bottom.Heightfield.GetHeight(i, 0)));
        }
        var query = output.Terrain;
        Assert.True(query.TrySampleHeight(-0.5f, -256, out float height));
        Assert.True(float.IsFinite(height));
        Assert.True(query.TrySampleNormal(-0.5f, -256, out var normal));
        Assert.True(normal.Y > 0.9f);
    }

    [Fact]
    public void ParallelReverseChunkGenerationPreservesEveryHeightBit()
    {
        var recipe = Recipe(ulong.MaxValue);
        var expected = ProceduralTerrainFactory.Create(recipe);
        var coordinates = expected.Terrain.Chunks.Select(c => c.Coordinate).Reverse().ToArray();
        var actual = new TerrainChunk[coordinates.Length];
        Parallel.For(0, actual.Length, i => actual[i] = ProceduralTerrainFactory.CreateChunk(recipe, coordinates[i]));
        var world = new TerrainWorld(expected.Terrain.Settings, actual);
        for (int i = 0; i < world.Chunks.Count; i++)
        for (int z = 0; z < 65; z++)
        for (int x = 0; x < 65; x++)
            Assert.Equal(BitConverter.SingleToInt32Bits(expected.Terrain.Chunks[i].Heightfield.GetHeight(x, z)),
                BitConverter.SingleToInt32Bits(world.Chunks[i].Heightfield.GetHeight(x, z)));
        Assert.Equal(expected.Diagnostics.TerrainSha256, ProceduralTerrainFactory.Create(recipe).Diagnostics.TerrainSha256);
        Assert.NotEqual(expected.Diagnostics.TerrainSha256, ProceduralTerrainFactory.Create(Recipe(0)).Diagnostics.TerrainSha256);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(17UL)]
    [InlineData(18446744073709551615UL)]
    public void HeightsMasksAndBilinearGradientsStayBounded(ulong seed)
    {
        var recipe = Recipe(seed);
        var output = ProceduralTerrainFactory.Create(recipe);
        Assert.InRange(output.Diagnostics.MinimumHeightMeters, -50, 150);
        Assert.InRange(output.Diagnostics.MaximumHeightMeters, -50, 150);
        Assert.InRange(output.Diagnostics.MaximumSampleGradient, 0, recipe.MaximumGradient);
        Assert.InRange(output.Diagnostics.GenerationMilliseconds, 0, double.MaxValue);
        var evaluator = new ProceduralTerrainEvaluator(recipe);
        for (int z = -128; z <= 128; z += 7)
        for (int x = -128; x <= 128; x += 7)
        {
            var sample = evaluator.Evaluate(new(x, z));
            Assert.InRange(sample.FlatPermille, (ushort)0, (ushort)1000);
            Assert.InRange(sample.DrainagePermille, (ushort)0, (ushort)1000);
        }
    }

    [Fact]
    public void InvalidVersionsProfilesParametersAndAllocationLimitsFailBeforeGeneration()
    {
        var recipe = Recipe();
        ProceduralTerrainRecipe[] invalid =
        [
            recipe with { Version = 2 }, recipe with { Generation = null! },
            recipe with { HillHeightMillimeters = -1 }, recipe with { BaseHeightMillimeters = int.MaxValue },
            recipe with { DetailHeightMillimeters = 1001 }, recipe with { FlatStrengthPermille = 1001 },
            recipe with { RidgeHeightMillimeters = 40000, HillHeightMillimeters = 40000, DrainageDepthMillimeters = 8000 },
            recipe with { Generation = recipe.Generation with { TerrainProfileId = "terrain.default.v1" } },
            recipe with { Generation = recipe.Generation with { GeneratorVersion = 2 } },
            recipe with { Generation = recipe.Generation with { SamplesPerSide = 129 } },
            recipe with { Generation = recipe.Generation with { WidthInChunks = 256, HeightInChunks = 256 } },
            recipe with { Generation = recipe.Generation with { Origin = new(int.MinValue, 0) } },
            recipe with { Generation = recipe.Generation with { Origin = new(4095, 0) } }
        ];
        foreach (var value in invalid)
            Assert.Throws<InvalidOperationException>(() => ProceduralTerrainFactory.Create(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralTerrainFactory.CreateChunk(recipe, new(2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProceduralTerrainEvaluator(recipe).Evaluate(new(long.MinValue, 0)));
    }

    [Fact]
    public void ZeroReliefIsExactlyFlatAndParameterIdentityIsExplicit()
    {
        var recipe = Recipe() with { HillHeightMillimeters = 0, RidgeHeightMillimeters = 0,
            DetailHeightMillimeters = 0, DrainageDepthMillimeters = 0 };
        var output = ProceduralTerrainFactory.Create(recipe);
        Assert.Equal(20f, output.Diagnostics.MinimumHeightMeters);
        Assert.Equal(20f, output.Diagnostics.MaximumHeightMeters);
        Assert.Equal(0, output.Diagnostics.MaximumSampleGradient);
        Assert.NotEqual(output.Diagnostics.TerrainSha256,
            ProceduralTerrainFactory.Create(recipe with { FlatStrengthPermille = 0 }).Diagnostics.TerrainSha256);
    }

    [Fact]
    public void NeighboringSubmapsReproduceTheSameGlobalChunk()
    {
        var recipe = Recipe();
        var subset = recipe with { Generation = recipe.Generation with { Origin = new(-1, -1), WidthInChunks = 1, HeightInChunks = 1 } };
        var left = ProceduralTerrainFactory.CreateChunk(recipe, new(-1, -1));
        var right = ProceduralTerrainFactory.Create(subset).Terrain.Chunks[0];
        for (int z = 0; z < 65; z++)
        for (int x = 0; x < 65; x++)
            Assert.Equal(left.Heightfield.GetHeight(x, z), right.Heightfield.GetHeight(x, z));
    }
}
