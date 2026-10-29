using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class EnvironmentScatterTests
{
    private static WorldGenerationRecipe Recipe(ulong seed = 17) => new()
    { Seed = seed, Origin = new(-1, -1), WidthInChunks = 2, HeightInChunks = 2 };
    private static readonly ChunkCoordinate[] Coordinates = [new(-1,-1), new(0,-1), new(-1,0), new(0,0)];
    private static ScatterSpecies Grass => new("mesh.world.vegetation.grass_clump", ScatterClassification.Grass,
        500, 800, 500, 500, 1000, 1000, 0, new(1000,1000,1000,1000,1000));

    [Fact]
    public void VersionOnePinsSignedChunkFingerprints()
    {
        var generator = new EnvironmentScatterGenerator(Recipe(),EnvironmentScatterProfile.Temperate(),new FixedEnvironment(),new ScatterClearanceIndex([]));
        string[] expected = ["f55d2c43206224eed490d12c285773ae1890cd27e59c612dfcb761b8290013c8",
            "5af9ec0c67b4f99c71798c47e09e52b431b338fb77a179cd043eb89846fcc9e3",
            "664e4dbf8b905b7340525249569189d5da6ab95f78740675028c74164d2e16cb",
            "0fc9a58715993e60ee557129487679d777e6d26449d373c168a10bd186572d0e"];
        int[] counts=[126,111,132,103];
        for(int i=0;i<Coordinates.Length;i++)
        { var chunk=generator.GenerateChunk(Coordinates[i]);Assert.Equal(expected[i],chunk.Sha256);Assert.Equal(counts[i],chunk.Instances.Length); }
    }

    [Fact]
    public void ReversedAndParallelChunksHaveIdenticalSortedBytes()
    {
        var generator = new EnvironmentScatterGenerator(Recipe(ulong.MaxValue), EnvironmentScatterProfile.Temperate(),
            new FixedEnvironment(), new ScatterClearanceIndex([]));
        var expected = Coordinates.ToDictionary(c => c, generator.GenerateChunk);
        Parallel.ForEach(Coordinates.Reverse(), c =>
        {
            var actual = generator.GenerateChunk(c);
            Assert.Equal(expected[c].Sha256, actual.Sha256);
            Assert.Equal(expected[c].GetCanonicalBytes(), actual.GetCanonicalBytes());
            Assert.True(actual.Instances.ToArray().Select(i => i.StableId).SequenceEqual(actual.Instances.ToArray().Select(i => i.StableId).Order()));
        });
        Assert.NotEmpty(expected.Values.SelectMany(c => c.Instances.ToArray()));
        Assert.Equal(expected.Values.Sum(c => c.Instances.Length), expected.Values.SelectMany(c => c.Instances.ToArray()).Select(i => i.StableId).Distinct().Count());
    }

    [Fact]
    public void MinimumSpacingIncludesNeighborChunkFootprintsAtNegativeOrigins()
    {
        var profile = EnvironmentScatterProfile.Temperate(1000);
        var generator = new EnvironmentScatterGenerator(Recipe(), profile, new FixedEnvironment(), new ScatterClearanceIndex([]));
        var points = Coordinates.SelectMany(c => generator.GenerateChunk(c).Instances.ToArray().Select(i =>
            (X: (long)c.X * 256000 + i.LocalXMillimeters, Z: (long)c.Z * 256000 + i.LocalZMillimeters,
                Radius: profile.Species[i.SpeciesIndex].RadiusMillimeters))).ToArray();
        Assert.True(points.Length > 100);
        for (int i = 0; i < points.Length; i++)
        {
            Assert.InRange(points[i].X - points[i].Radius, -256000, 255999);
            Assert.InRange(points[i].Z - points[i].Radius, -256000, 255999);
            for (int j = i + 1; j < points.Length; j++)
            {
                long dx = points[i].X - points[j].X, dz = points[i].Z - points[j].Z, radius = points[i].Radius + points[j].Radius;
                Assert.True(dx * dx + dz * dz >= radius * radius);
            }
        }
    }

    [Fact]
    public void BlockersYardsAndAccessMarginsUseTheWholeFootprint()
    {
        var index = new ScatterClearanceIndex([
            new(ScatterAreaKind.Rectangle, -30000,-256000,30000,256000,12000),
            new(ScatterAreaKind.Corridor,-256000,30000,256000,80000,28000),
            new(ScatterAreaKind.Yard,-230000,-230000,-80000,-80000)]);
        var profile = EnvironmentScatterProfile.Temperate(1000);
        var generator = new EnvironmentScatterGenerator(Recipe(), profile, new FixedEnvironment(), index);
        var classes = new List<ScatterClassification>();
        foreach (var c in Coordinates)
        foreach (var item in generator.GenerateChunk(c).Instances)
        {
            long x = (long)c.X * 256000 + item.LocalXMillimeters, z = (long)c.Z * 256000 + item.LocalZMillimeters;
            int radius = profile.Species[item.SpeciesIndex].RadiusMillimeters;
            Assert.False(index.IsBlocked(x, z, radius));
            if (item.Classification == ScatterClassification.IndustrialProp) Assert.True(index.IsYard(x, z, radius));
            else Assert.False(index.OverlapsYard(x, z, radius));
            classes.Add(item.Classification);
        }
        Assert.Contains(ScatterClassification.IndustrialProp, classes);
        Assert.Contains(ScatterClassification.Tree, classes);
    }

    [Fact]
    public void SceneQuotaIsBoundedAndIndependentOfRequestOrder()
    {
        var profile = new EnvironmentScatterProfile("scatter.budget.v1", [Grass], densityPermille:1000, maximumTotalInstances:37);
        var generator = new EnvironmentScatterGenerator(Recipe(), profile, new FixedEnvironment(), new ScatterClearanceIndex([]));
        var first = Coordinates.Select(generator.GenerateChunk).ToArray();
        Assert.Equal(37, first.Sum(c => c.Instances.Length));
        Assert.All(first, c => Assert.InRange(c.Instances.Length, 9, 10));
        foreach (var coordinate in Coordinates.Reverse())
            Assert.Equal(first.Single(c => c.Coordinate == coordinate).Sha256, generator.GenerateChunk(coordinate).Sha256);
    }

    [Fact]
    public void MillionInstanceBudgetCanBeStreamedWithoutRetainingTheScene()
    {
        var recipe = Recipe() with { Origin=new(-15,-15),WidthInChunks=30,HeightInChunks=30 };
        var profile = new EnvironmentScatterProfile("scatter.stress.v1",[Grass with {RadiusMillimeters=400}],
            densityPermille:1000,clusterPeriodMillimeters:64000,maximumTotalInstances:1_000_000);
        var generator = new EnvironmentScatterGenerator(recipe,profile,new FixedEnvironment(),new ScatterClearanceIndex([]));
        long count=0;
        for(int z=-15;z<15;z++)for(int x=-15;x<15;x++)
        { var chunk=generator.GenerateChunk(new(x,z));Assert.InRange(chunk.Instances.Length,0,1112);count+=chunk.Instances.Length; }
        Assert.Equal(1_000_000,count);
    }

    [Theory]
    [InlineData(EnvironmentSurface.Water, 0, 50000)]
    [InlineData(EnvironmentSurface.Natural, 65535, 50000)]
    [InlineData(EnvironmentSurface.Built, 0, 50000)]
    public void UnsupportedHabitatsProduceNoDecoration(EnvironmentSurface surface, int slope, int soil)
    {
        var field = new FixedEnvironment(surface, (ushort)slope, (ushort)soil);
        var generator = new EnvironmentScatterGenerator(Recipe(), new("scatter.grass.v1", [Grass], 1000), field, new ScatterClearanceIndex([]));
        Assert.Empty(generator.GenerateChunk(new(0,0)).Instances.ToArray());
    }

    [Fact]
    public void ProfileAndOutputAreImmutableAndDecorationIdentityIsSeparate()
    {
        var source = new[] { Grass };
        var profile = new EnvironmentScatterProfile("scatter.copy.v1", source, 1000);
        source[0] = Grass with { AssetId = "mesh.other" };
        Assert.Equal(Grass, profile.Species[0]);
        var recipe = Recipe();
        var gameplay = WorldGenerationFingerprint.Gameplay(recipe, []);
        string gameplayHash = gameplay.Sha256;
        var chunk = new EnvironmentScatterGenerator(recipe, profile, new FixedEnvironment(), new ScatterClearanceIndex([])).GenerateChunk(new(0,0));
        var bytes = chunk.GetCanonicalBytes(); Array.Fill(bytes, (byte)0);
        Assert.NotEqual(bytes, chunk.GetCanonicalBytes());
        var fingerprint = chunk.CreateDecorationFingerprint(gameplay);
        Assert.Equal(WorldFingerprintKind.Decoration, fingerprint.Kind);
        Assert.Equal(gameplayHash, gameplay.Sha256);
        Assert.NotEqual(chunk.Sha256, new EnvironmentScatterGenerator(recipe with { Seed = 18 }, profile,
            new FixedEnvironment(), new ScatterClearanceIndex([])).GenerateChunk(new(0,0)).Sha256);
        Assert.NotEqual(fingerprint.Sha256, chunk.CreateDecorationFingerprint(WorldGenerationFingerprint.Gameplay(recipe with { Seed = 18 }, [])).Sha256);
        Assert.All(chunk.Instances.ToArray(), i => Assert.Equal(10000, i.HeightMillimeters));
    }

    [Fact]
    public void RealTerrainAndBiomeQueriesRemainHeadlessAndUnchanged()
    {
        var recipe = Recipe() with { TerrainProfileId = ProceduralTerrainRecipe.ProfileId, ClimateProfileId = BiomeFieldProfile.TemperateProfileId };
        var terrain = ProceduralTerrainFactory.Create(new() { Generation = recipe });
        var field = new BiomeField(recipe, terrain.Terrain);
        var generator = new EnvironmentScatterGenerator(recipe, EnvironmentScatterProfile.Temperate(), field, new ScatterClearanceIndex([]));
        var first = generator.GenerateChunk(new(0,0));
        Assert.NotEmpty(first.Instances.ToArray());
        Assert.Equal(first.Sha256, generator.GenerateChunk(new(0,0)).Sha256);
        Assert.Equal(terrain.Diagnostics.TerrainSha256, ProceduralTerrainFactory.Create(new() { Generation = recipe }).Diagnostics.TerrainSha256);
        for (int index = 0; index < first.Instances.Length; index++)
            Assert.True(float.IsFinite(first.GetTransform(index).PositionMeters.Y));
    }

    [Fact]
    public void InvalidProfilesGeometryAndBudgetsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() => new EnvironmentScatterProfile("scatter.bad.v1", [Grass,Grass]));
        Assert.Throws<ArgumentException>(() => new EnvironmentScatterProfile("scatter.bad.v1", [Grass with { RadiusMillimeters = 250 }]));
        Assert.Throws<ArgumentException>(() => new EnvironmentScatterProfile("scatter.bad.v1", [Grass], maximumInstancesPerChunk:4097));
        Assert.Throws<ArgumentException>(() => new EnvironmentScatterProfile("scatter.bad.v1", [Grass], maximumTotalInstances:4_000_001));
        Assert.Throws<ArgumentException>(() => new ScatterClearanceIndex([new(ScatterAreaKind.Rectangle,long.MinValue,0,0,0)]));
        Assert.Throws<ArgumentException>(() => new ScatterClearanceIndex([new(ScatterAreaKind.Rectangle,-1_000_000_000,-1_000_000_000,1_000_000_000,1_000_000_000)]));
        var generator = new EnvironmentScatterGenerator(Recipe(), EnvironmentScatterProfile.Temperate(), new FixedEnvironment(), new ScatterClearanceIndex([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateChunk(new(1,0)));
        Assert.Throws<ArgumentException>(() => new EnvironmentScatterGenerator(Recipe() with { SamplesPerSide = 129 },
            EnvironmentScatterProfile.Temperate(), new FixedEnvironment(), new ScatterClearanceIndex([])));
    }

    private sealed class FixedEnvironment(EnvironmentSurface surface = EnvironmentSurface.Natural, ushort slope = 0, ushort soil = 50000) : IEnvironmentQuery
    {
        public bool TrySample(long worldXMillimeters, long worldZMillimeters, out EnvironmentSample sample, EnvironmentSurface requested = EnvironmentSurface.Natural)
        { sample = new(10000,16000,slope,40000,soil,surface, surface is EnvironmentSurface.Water or EnvironmentSurface.Built ? default : new(65535,0,0,0,0)); return true; }
    }
}
