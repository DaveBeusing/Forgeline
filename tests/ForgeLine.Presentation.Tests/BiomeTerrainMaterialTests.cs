using System.Numerics;
using ForgeLine.Game;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BiomeTerrainMaterialTests
{
    private const ushort Unit = ushort.MaxValue;
    internal static TerrainWorld Flat(params ChunkCoordinate[] coordinates) =>
        new(new WorldGridSettings(), coordinates.Select(c => new TerrainChunk(c,
            new TerrainHeightfield(65, 256, Enumerable.Repeat(10f, 65 * 65).ToArray()))));

    internal sealed class Field(Func<long, long, EnvironmentSample>? sample = null, bool available = true) : IEnvironmentQuery
    {
        private long _calls;
        internal long Calls => Interlocked.Read(ref _calls);
        public bool TrySample(long x, long z, out EnvironmentSample result, EnvironmentSurface surface = EnvironmentSurface.Natural)
        {
            Interlocked.Increment(ref _calls);
            result = sample?.Invoke(x, z) ?? Sample(new(0, Unit, 0, 0, 0));
            return available;
        }
    }

    internal static EnvironmentSample Sample(BiomeWeights weights, ushort moisture = 30000) =>
        new(10000, 18000, 0, moisture, 40000, EnvironmentSurface.Natural, weights);

    private static TerrainPresentationProfile Resolve(TerrainWorld world, IEnvironmentQuery field, TerrainInfrastructureMask? masks = null) =>
        TerrainVisualProfileResolver.Resolve(TerrainVisualProfileResolver.CreateBiomeDefinition(), world, field, masks);

    [Fact]
    public void AllBiomeFamiliesMapToFiniteNormalizedExistingMaterials()
    {
        var world = Flat(default(ChunkCoordinate));
        var weights = new[] { new BiomeWeights(Unit, 0, 0, 0, 0), new(0, Unit, 0, 0, 0),
            new(0, 0, Unit, 0, 0), new(0, 0, 0, Unit, 0), new(0, 0, 0, 0, Unit) };
        var expected = new[] { TerrainMaterialSlot.Dirt, TerrainMaterialSlot.GrassGround,
            TerrainMaterialSlot.Dirt, TerrainMaterialSlot.Rock, TerrainMaterialSlot.Mud };
        for (int index = 0; index < weights.Length; index++)
        {
            var source = new Field((x, z) => Sample(weights[index]));
            var profile = Resolve(world, source);
            float[] actual = Weights(profile, new(128, 10, 128));
            Assert.Equal(expected[index], (TerrainMaterialSlot)Array.IndexOf(actual, actual.Max()));
            Assert.Equal(8, profile.Materials.Count);
            var splat = TerrainSplatMapBuilder.Build(world.Chunks[0], profile);
            Assert.Contains(expected[index], splat.Palette);
            Normalized(splat);
        }
    }

    [Fact]
    public void NarrowOffGridRoadAtNegativeChunkEdgeCannotDisappear()
    {
        var world = Flat(new(-1, 0), new(0, 0));
        var mask = new TerrainInfrastructureMask([TerrainInfrastructureMask.Road(new(3, 90), new(3, 170), .5f, 0)]);
        var profile = Resolve(world, new Field(), mask);
        foreach (var chunk in world.Chunks)
        {
            var splat = TerrainSplatMapBuilder.Build(chunk, profile);
            Assert.Contains(TerrainMaterialSlot.Gravel, splat.Palette);
            int x = chunk.Coordinate.X == -1 ? 32 : 0;
            Assert.Equal(255, Decode(splat, x, 16)[(int)TerrainMaterialSlot.Gravel]);
            var diagnostic = profile.CaptureBiomePalette(chunk.Coordinate)!;
            Assert.True((diagnostic.RequiredMaterialMask & (1u << (int)TerrainMaterialSlot.Gravel)) != 0);
            Normalized(splat);
        }
    }

    [Fact]
    public void DifferentNeighborPalettesDecodeToIdenticalSemanticEdgeWeights()
    {
        var world = Flat(new(-1, 0), new(0, 0));
        var field = new Field((x, z) => Sample(x < -128000 ? new(Unit, 0, 0, 0, 0) :
            x > 128000 ? new(0, 0, 0, Unit, 0) :
            Math.Abs(x) < 64000 ? new(0, 55000, 0, 0, 10535) : new(0, Unit, 0, 0, 0)));
        var mask = new TerrainInfrastructureMask([TerrainInfrastructureMask.Road(new(3, 115), new(3, 140), .5f, 0)]);
        var profile = Resolve(world, field, mask);
        var left = TerrainSplatMapBuilder.Build(world.Chunks[0], profile);
        var right = TerrainSplatMapBuilder.Build(world.Chunks[1], profile);
        Assert.NotEqual(left.Palette.ToArray(), right.Palette.ToArray());
        for (int z = 0; z < 33; z++) Assert.Equal(Decode(left, 32, z), Decode(right, 0, z));
        Assert.True(Weights(profile, new(0, 10, 0))[(int)TerrainMaterialSlot.Mud] > .1f);
        Assert.Equal(0, Decode(left, 32, 0)[(int)TerrainMaterialSlot.Mud]);
        Assert.Equal(0, Decode(right, 0, 0)[(int)TerrainMaterialSlot.Mud]);
    }

    [Fact]
    public void OneTexelRoadFootprintSurvivesEveryMipIncludingOneByOne()
    {
        var world = Flat(default(ChunkCoordinate));
        var masks = new TerrainInfrastructureMask(
            [new(TerrainMaterialSlot.Gravel, new(128, 128), new(.01f, .01f), 0, Strength: 1)]);
        var profile = Resolve(world, new Field(), masks);
        var splat = TerrainSplatMapBuilder.Build(world.Chunks[0], profile);
        int channel = splat.Palette.ToList().IndexOf(TerrainMaterialSlot.Gravel);
        Assert.True(channel >= 0);
        foreach (var mip in splat.Mips)
            Assert.Contains(Enumerable.Range(0, mip.Width * mip.Height), pixel => mip.Pixels[pixel * 4 + channel] > 0);
        Assert.Equal(1, splat.Mips[^1].Pixels[channel]);
        Normalized(splat);
    }

    [Fact]
    public void FourWayNegativeCornerAndParallelChunkOrderAreStable()
    {
        var world = Flat(new(-1, -1), new(0, -1), new(-1, 0), new(0, 0));
        var profile = Resolve(world, new Field());
        byte[][] expected = world.Chunks.Select(c => TerrainSplatMapBuilder.Build(c, profile).Mips[0].Pixels).ToArray();
        Parallel.For(0, 32, index =>
        {
            int which = index % 4;
            Assert.Equal(expected[which], TerrainSplatMapBuilder.Build(world.Chunks[which], profile).Mips[0].Pixels);
        });
        var corners = world.Chunks.Select(c => Decode(TerrainSplatMapBuilder.Build(c, profile),
            c.Coordinate.X == -1 ? 32 : 0, c.Coordinate.Z == -1 ? 32 : 0)).ToArray();
        Assert.All(corners, c => Assert.Equal(corners[0], c));
    }

    [Fact]
    public void InfrastructurePriorityIsStableAndConcreteWinsOverRoads()
    {
        var world = Flat(default(ChunkCoordinate));
        TerrainBlendRegion[] regions = [TerrainInfrastructureMask.Road(new(0, 128), new(256, 128), 2),
            new(TerrainMaterialSlot.Concrete, new(128, 128), new(16, 16), 4, Strength: 1)];
        var a = Resolve(world, new Field(), new(regions));
        var b = Resolve(world, new Field(), new(regions.Reverse()));
        Assert.Equal(1f, Weights(a, new(128, 10, 128))[(int)TerrainMaterialSlot.Concrete]);
        var first = TerrainSplatMapBuilder.Build(world.Chunks[0], a);
        var second = TerrainSplatMapBuilder.Build(world.Chunks[0], b);
        Assert.Equal(first.Palette, second.Palette);
        for (int i = 0; i < first.Mips.Count; i++) Assert.Equal(first.Mips[i].Pixels, second.Mips[i].Pixels);
        Assert.Equal(255, Decode(first, 16, 16)[(int)TerrainMaterialSlot.Concrete]);
    }

    [Fact]
    public void MoreThanFourRequiredMaterialsFailsInsteadOfDiscardingOne()
    {
        var world = Flat(default(ChunkCoordinate));
        var field = new Field((x, z) => Sample(x < 64000 ? new(0, Unit, 0, 0, 0) :
            x < 128000 ? new(Unit, 0, 0, 0, 0) : x < 192000 ? new(0, 0, 0, Unit, 0) : new(0, 0, 0, 0, Unit)));
        var roads = new TerrainInfrastructureMask([TerrainInfrastructureMask.Road(new(128, 50), new(128, 200), 1)]);
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(world, field, roads));
        Assert.Contains("more than four", error.Message);
    }

    [Fact]
    public void AuthoredProfileIdsKeepExactLegacyControlBytes()
    {
        var world = Flat(default(ChunkCoordinate));
        foreach (var definition in new[] { CentralDivideBattlefield.Create().TerrainVisual, SplitBasinBattlefield.Create().TerrainVisual })
        {
            var legacy = TerrainPresentationProfile.CreateCentralDivide();
            var resolved = TerrainVisualProfileResolver.Resolve(definition, world);
            var a = TerrainSplatMapBuilder.Build(world.Chunks[0], legacy);
            var b = TerrainSplatMapBuilder.Build(world.Chunks[0], resolved);
            Assert.Equal(a.Palette, b.Palette);
            for (int index = 0; index < a.Mips.Count; index++) Assert.Equal(a.Mips[index].Pixels, b.Mips[index].Pixels);
            Assert.Null(resolved.CaptureBiomePalette(default));
            Assert.Equal(legacy.SampleBaseColor(new(128, 10, 128), Vector3.UnitY), resolved.SampleBaseColor(new(128, 10, 128), Vector3.UnitY));
        }
    }

    [Fact]
    public void ActualProceduralTerrainAndBiomeQueryIncludeStoredOuterBorders()
    {
        var generation = new WorldGenerationRecipe
        {
            Origin = new(-2, -2), WidthInChunks = 2, HeightInChunks = 2, Seed = 17,
            TerrainProfileId = ProceduralTerrainRecipe.ProfileId, ClimateProfileId = BiomeFieldProfile.TemperateProfileId
        };
        var terrain = ProceduralTerrainFactory.Create(new() { Generation = generation }).Terrain;
        var query = TerrainVisualProfileResolver.CreateEnvironment(terrain, generation);
        Assert.False(terrain.TrySampleHeight(0, 0, out _));
        Assert.True(query.TrySample(0, 0, out var sample));
        Assert.True(terrain.TryGetChunk(new(-1, -1), out var last));
        Assert.Equal((int)Math.Round(last.Heightfield.GetHeight(64, 64) * 1000.0, MidpointRounding.ToEven), sample.HeightMillimeters);
        var profile = Resolve(terrain, query);
        foreach (var chunk in terrain.Chunks) Normalized(TerrainSplatMapBuilder.Build(chunk, profile));
        Assert.False(query.TrySample(1, 1, out _));
        Assert.Throws<ArgumentException>(() => TerrainVisualProfileResolver.CreateEnvironment(terrain, generation with { Seed = 2, WidthInChunks = 1 }));
    }

    [Fact]
    public void MasksVersionsMissingSamplesAndUnsupportedGridsFailExplicitly()
    {
        var world = Flat(default(ChunkCoordinate));
        var definition = TerrainVisualProfileResolver.CreateBiomeDefinition();
        Assert.Throws<InvalidOperationException>(() => TerrainVisualProfileResolver.Resolve(definition with { ProfileId = "unknown" }, world));
        Assert.Throws<ArgumentNullException>(() => TerrainVisualProfileResolver.Resolve(definition, world));
        Assert.Throws<InvalidOperationException>(() => Resolve(world, new Field(available: false)));
        Assert.Throws<InvalidOperationException>(() => Resolve(world, new Field((x, z) => Sample(default))));
        Assert.Throws<InvalidOperationException>(() => TerrainVisualProfileResolver.Resolve(definition with { ControlSamplesPerSide = 65 }, world, new Field()));
        Assert.Throws<ArgumentException>(() => TerrainVisualProfileResolver.Resolve(CentralDivideBattlefield.Create().TerrainVisual, world, new Field()));
        Assert.Throws<ArgumentException>(() => Resolve(Flat(new(0, 0), new(2, 0)), new Field()));
        Assert.Throws<ArgumentException>(() => new TerrainInfrastructureMask([new(TerrainMaterialSlot.Scorched, default, Vector2.One, 1)]));
    }

    [Fact]
    public void SlopeElevationAndStableQuantizationAreExplicitVisualInputs()
    {
        var world = Flat(default(ChunkCoordinate));
        var meadow = Sample(new(0, Unit, 0, 0, 0));
        var ordinary = Resolve(world, new Field((x, z) => meadow));
        foreach (var changed in new[] { meadow with { Slope = 30000 }, meadow with { HeightMillimeters = 1500000 } })
        {
            var exposed = Resolve(world, new Field((x, z) => changed));
            Assert.True(Weights(exposed, new(128, 10, 128))[(int)TerrainMaterialSlot.Rock] >
                Weights(ordinary, new(128, 10, 128))[(int)TerrainMaterialSlot.Rock]);
        }
        Assert.Equal(10f, world.Chunks[0].Heightfield.GetHeight(0, 0));
        Span<byte> bytes = stackalloc byte[4];
        TerrainSplatMapBuilder.NormalizeControlWeights([.25f, .25f, .25f, .25f], bytes);
        Assert.Equal(new byte[] { 64, 64, 64, 63 }, bytes.ToArray());
    }

    [Fact]
    public void AuthoredMapControlReferencesStayPinnedAcrossEveryChunkAndMip()
    {
        foreach (bool central in new[] { true, false })
        {
            var definition = central ? CentralDivideBattlefield.Create() : SplitBasinBattlefield.Create();
            var terrain = BattlefieldTerrainFactory.Create(definition,
                central ? BattlefieldTerrainRecipe.CentralDivide() : SplitBasinBattlefield.CreateTerrainRecipe());
            var profile = TerrainVisualProfileResolver.Resolve(definition.TerrainVisual, terrain);
            var writer = new WorldCanonicalWriter();
            foreach (var chunk in terrain.Chunks)
            {
                writer.WriteInt32(chunk.Coordinate.X); writer.WriteInt32(chunk.Coordinate.Z);
                var splat = TerrainSplatMapBuilder.Build(chunk, profile);
                foreach (var slot in splat.Palette) writer.WriteUInt32((uint)slot);
                foreach (var mip in splat.Mips)
                {
                    writer.WriteInt32(mip.Width); writer.WriteInt32(mip.Height); writer.WriteBytes(mip.Pixels);
                }
            }
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(writer.ToArray()));
            Assert.Equal(central ? "9276b7effd34e3520eae35453cd31b4c7a167fb6e7569bba37c8dba03252032f" :
                "2d93e94abe550fd76f3eea3480e13636f9b2d50d34c37ec59de4fd9672629273", hash);
        }
    }

    [Fact]
    public void SurfaceMarksRebuildOnlyTheirOwnImmutablePaletteGeneration()
    {
        var world = Flat(default(ChunkCoordinate));
        var profile = Resolve(world, new Field());
        var before = TerrainSplatMapBuilder.Build(world.Chunks[0], profile);
        var marked = profile.WithSurfaceMarks(new TerrainSurfaceMarks(
            [new(TerrainSurfaceMarkKind.TireTrack, new(32, 128), new(224, 128), 8)]));
        Assert.Equal(1, marked.SurfaceMarkCount);
        Assert.True((marked.CaptureBiomePalette(default)!.RequiredMaterialMask & (1u << (int)TerrainMaterialSlot.Dirt)) != 0);
        Assert.True(Weights(marked, new(128, 10, 128))[(int)TerrainMaterialSlot.Dirt] > Weights(profile, new(128, 10, 128))[(int)TerrainMaterialSlot.Dirt]);
        Assert.Equal(before.Mips[0].Pixels, TerrainSplatMapBuilder.Build(world.Chunks[0], profile).Mips[0].Pixels);
    }

    [Fact]
    public void SpatialAndInfrastructureLimitsBoundPreflightWork()
    {
        var world = Flat(default(ChunkCoordinate));
        var many = Enumerable.Range(0, 65).Select(i => new TerrainBlendRegion(TerrainMaterialSlot.Concrete,
            new(16 + i * .01f, 16), Vector2.One, 1)).ToArray();
        Assert.Throws<InvalidOperationException>(() => Resolve(world, new Field(), new(many)));
        Assert.Throws<ArgumentException>(() => new TerrainInfrastructureMask(Enumerable.Repeat(many[0], 4097)));
        Assert.Throws<ArgumentException>(() => new TerrainInfrastructureMask([many[0], many[0]]));
        Assert.Throws<ArgumentException>(() => new TerrainInfrastructureMask([many[0] with { Strength = float.NaN }]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainInfrastructureMask.Road(Vector2.Zero, Vector2.Zero, 1));
        Assert.Throws<ArgumentException>(() => Resolve(Flat(Enumerable.Range(0, 257).Select(i => new ChunkCoordinate(i, 0)).ToArray()), new Field()));
        var definition = TerrainVisualProfileResolver.CreateBiomeDefinition();
        Assert.Throws<InvalidOperationException>(() => TerrainVisualProfileResolver.Resolve(
            definition with { MaterialAssetIds = definition.MaterialAssetIds.Reverse().ToArray() }, world, new Field()));
    }

    internal static byte[] Decode(TerrainChunkSplatData splat, int x, int z)
    {
        byte[] values = new byte[8];
        for (int i = 0; i < 4; i++) values[(int)splat.Palette[i]] = splat.Mips[0].Pixels[(z * 33 + x) * 4 + i];
        return values;
    }
    private static float[] Weights(TerrainPresentationProfile profile, Vector3 point)
    {
        float[] weights = new float[8];
        profile.CalculateMaterialWeights(point, Vector3.UnitY, weights);
        Assert.All(weights, w => { Assert.True(float.IsFinite(w)); Assert.InRange(w, 0, 1); });
        Assert.InRange(weights.Sum(), .99999f, 1.00001f);
        return weights;
    }
    internal static void Normalized(TerrainChunkSplatData splat)
    {
        Assert.Equal(4, splat.Palette.Count);
        Assert.Equal(4, splat.Palette.Distinct().Count());
        Assert.Equal(6, splat.Mips.Count);
        Assert.Equal(5720, splat.Mips.Sum(m => m.Pixels.Length));
        foreach (var mip in splat.Mips)
            for (int i = 0; i < mip.Pixels.Length; i += 4)
                Assert.Equal(255, mip.Pixels[i] + mip.Pixels[i + 1] + mip.Pixels[i + 2] + mip.Pixels[i + 3]);
    }
}
