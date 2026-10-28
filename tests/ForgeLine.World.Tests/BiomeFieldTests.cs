using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class BiomeFieldTests
{
    [Fact]
    public void MiniRasterHasFixedSamplesCanonicalBytesAndDiagnosticIdentity()
    {
        var recipe = new WorldGenerationRecipe
        {
            Seed = 0, Origin = new(-1, -1), WidthInChunks = 2, HeightInChunks = 2,
            ChunkSizeMillimeters = 2000, SamplesPerSide = 3, RegionSizeInChunks = 1,
            TerrainProfileId = "t", ClimateProfileId = BiomeFieldProfile.TemperateProfileId,
            GeographyProfileId = "g", RulesProfileId = "r"
        };
        var raster = BiomeFieldRaster.Create(new(recipe, new PlaneTerrain(1)), new(-1, -1));
        Assert.True(raster.TryGetSample(0, 0, out var first));
        Assert.Equal(new EnvironmentSample(1000, 22576, 0, 36535, 19842, EnvironmentSurface.Natural,
            new(11421, 54018, 0, 0, 96)), first);
        Assert.True(raster.TryGetSample(2, 2, out var last));
        Assert.Equal(new EnvironmentSample(1000, 22576, 0, 36534, 19841, EnvironmentSurface.Natural,
            new(11420, 54020, 0, 0, 95)), last);
        var fingerprint = raster.ComputeFingerprint();
        Assert.Equal(549, fingerprint.GetCanonicalBytes().Length);
        Assert.Equal("ec6346db297d4d10e12e73805eed053cbebe2a25d7bb2ba740b5008f77bb3b14", fingerprint.Sha256);
        var report = raster.CaptureDiagnostics();
        Assert.Equal(fingerprint.Sha256, report.Sha256);
        Assert.Equal(9, report.Statistics.AvailableCount);
        Assert.Equal(9, report.Statistics.DominantCounts.Meadow);
        Assert.Equal(report.ToJson(), raster.CaptureDiagnostics().ToJson());
        using var json = System.Text.Json.JsonDocument.Parse(report.ToJson());
        Assert.Equal(1, json.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal(0UL, json.RootElement.GetProperty("Recipe").GetProperty("Seed").GetUInt64());
    }

    private static WorldGenerationRecipe Recipe(ulong seed = 42) => new()
    {
        Seed = seed, Origin = new(-2, -2), WidthInChunks = 4, HeightInChunks = 4,
        SamplesPerSide = 17, ClimateProfileId = BiomeFieldProfile.TemperateProfileId
    };

    private sealed class PlaneTerrain(float height = 100, Vector3? normal = null,
        bool available = true, bool normalAvailable = true) : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-1_000_000), new(1_000_000));
        public bool TrySampleHeight(float worldX, float worldZ, out float result)
        {
            result = height;
            return available;
        }
        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 result)
        {
            result = normal ?? Vector3.UnitY;
            return normalAvailable;
        }
    }

    [Fact]
    public void NegativeSharedChunkEdgesAndCornersHaveIdenticalSamples()
    {
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        var left = BiomeFieldRaster.Create(field, new(-1, -1));
        var right = BiomeFieldRaster.Create(field, new(0, -1));
        var below = BiomeFieldRaster.Create(field, new(-1, 0));
        for (int index = 0; index < 17; index++)
        {
            Assert.True(left.TryGetSample(16, index, out var a));
            Assert.True(right.TryGetSample(0, index, out var b));
            Assert.Equal(a, b);
            Assert.True(left.TryGetSample(index, 16, out a));
            Assert.True(below.TryGetSample(index, 0, out b));
            Assert.Equal(a, b);
        }
        foreach (long boundary in new long[] { -256_000, 0, 256_000 })
        {
            Assert.True(field.TrySample(boundary - 1, 17_321, out var before));
            Assert.True(field.TrySample(boundary + 1, 17_321, out var after));
            Assert.InRange(Math.Abs(before.TemperatureMilliCelsius - after.TemperatureMilliCelsius), 0, 4);
            Assert.InRange(Math.Abs(before.Moisture - after.Moisture), 0, 8);
            Assert.InRange(Math.Abs(before.SoilFertility - after.SoilFertility), 0, 8);
            for (int index = 0; index < 5; index++)
                Assert.InRange(Math.Abs(before.Weights[(BiomeKind)index] - after.Weights[(BiomeKind)index]), 0, 16);
        }
    }

    [Fact]
    public void CacheEvictionPermutationAndParallelQueriesPreserveBytes()
    {
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        var coordinates = new[] { new ChunkCoordinate(-1, -1), new(0, -1), new(-1, 0), new(0, 0) };
        var expected = coordinates.ToDictionary(c => c, c => BiomeFieldRaster.Create(field, c).ComputeFingerprint());
        var cache = new BiomeFieldCache(field, 2);
        Parallel.For(0, 32, index =>
        {
            var coordinate = coordinates[(index * 3 + 1) % coordinates.Length];
            var actual = cache.GetRaster(coordinate).ComputeFingerprint();
            Assert.Equal(expected[coordinate].Sha256, actual.Sha256);
            Assert.Equal(expected[coordinate].GetCanonicalBytes(), actual.GetCanonicalBytes());
        });
        Assert.InRange(cache.Count, 1, 2);
        var raster = cache.GetRaster(default);
        Assert.Same(raster, cache.GetRaster(default));
        Assert.NotSame(raster, cache.GetRaster(default, EnvironmentSurface.Built));
        Assert.Equal(expected[default].Sha256, cache.GetRaster(default).ComputeFingerprint().Sha256);
    }

    [Fact]
    public void DisplaySubsamplingDoesNotChangeEnvironment()
    {
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        var raster = BiomeFieldRaster.Create(field, new(-1, -1));
        foreach (int step in new[] { 1, 2, 4, 8 })
        for (int z = 0; z < 17; z += step)
        for (int x = 0; x < 17; x += step)
        {
            Assert.True(field.TrySample(-256_000 + x * 16_000, -256_000 + z * 16_000, out var direct));
            Assert.True(raster.TryGetSample(x, z, out var cached));
            Assert.Equal(direct, cached);
        }
    }

    [Fact]
    public void ClimateRespondsToSeedAndCoordinates()
    {
        var first = new BiomeField(Recipe(), new PlaneTerrain());
        var repeated = new BiomeField(Recipe(), new PlaneTerrain());
        var changed = new BiomeField(Recipe(42UL + (1UL << 63)), new PlaneTerrain());
        var values = new HashSet<EnvironmentSample>();
        for (long x = -500_000; x <= 500_000; x += 10_000)
        {
            Assert.True(first.TrySample(x, -x, out var a));
            Assert.True(repeated.TrySample(x, -x, out var b));
            Assert.Equal(a, b);
            Assert.Equal(ushort.MaxValue, a.Weights.Sum);
            values.Add(a);
        }
        Assert.True(values.Count > 50);
        Assert.NotEqual(BiomeFieldRaster.Create(first, default).ComputeFingerprint().Sha256,
            BiomeFieldRaster.Create(changed, default).ComputeFingerprint().Sha256);
    }

    [Fact]
    public void ElevationCoolsAndSlopeDriesAndExposesRock()
    {
        var profile = new BiomeFieldProfile { TemperatureAmplitudeMilliCelsius = 0 };
        var low = new BiomeField(Recipe(), new PlaneTerrain(100), profile);
        var high = new BiomeField(Recipe(), new PlaneTerrain(1100), profile);
        Assert.True(low.TrySample(100, 200, out var a));
        Assert.True(high.TrySample(100, 200, out var b));
        Assert.Equal(6500, a.TemperatureMilliCelsius - b.TemperatureMilliCelsius);
        var steep = new BiomeField(Recipe(), new PlaneTerrain(100, new(1, .1f, 0)), profile);
        Assert.True(steep.TrySample(100, 200, out var c));
        Assert.True(c.Moisture <= a.Moisture);
        Assert.Equal((ushort)0, c.SoilFertility);
        Assert.Equal(new BiomeWeights(0, 0, 0, ushort.MaxValue, 0), c.Weights);
    }

    [Fact]
    public void LowGroundIsMoisterAndCanFavorWetDepressions()
    {
        var low = new BiomeField(Recipe(), new PlaneTerrain(1));
        var high = new BiomeField(Recipe(), new PlaneTerrain(100));
        bool foundWet = false;
        for (long x = -500_000; x <= 500_000; x += 10_000)
        {
            Assert.True(low.TrySample(x, 0, out var a));
            Assert.True(high.TrySample(x, 0, out var b));
            Assert.True(a.Moisture > b.Moisture);
            Assert.Equal((ushort)0, b.Weights.WetDepression);
            foundWet |= a.Weights.WetDepression > 0;
        }
        Assert.True(foundWet);
    }

    [Theory]
    [InlineData(EnvironmentSurface.Water)]
    [InlineData(EnvironmentSurface.Built)]
    [InlineData(EnvironmentSurface.Rock)]
    public void ExplicitSurfaceConstraintsHaveDefinedEcology(EnvironmentSurface surface)
    {
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        Assert.True(field.TrySample(0, 0, out var sample, surface));
        Assert.Equal(surface, sample.Surface);
        Assert.Equal((ushort)0, sample.SoilFertility);
        Assert.Equal(surface == EnvironmentSurface.Rock ? ushort.MaxValue : 0, sample.Weights.Sum);
        Assert.Equal(surface == EnvironmentSurface.Rock ? BiomeKind.RockyGround : (BiomeKind?)null, sample.Weights.DominantBiome);
        if (surface == EnvironmentSurface.Water) Assert.Equal(ushort.MaxValue, sample.Moisture);
        var statistics = BiomeFieldRaster.Create(field, default, surface).GetStatistics();
        Assert.Equal(289, statistics.AvailableCount);
        Assert.Equal(surface == EnvironmentSurface.Water ? 289 : 0, statistics.WaterCount);
        Assert.Equal(surface == EnvironmentSurface.Built ? 289 : 0, statistics.BuiltCount);
        Assert.Equal(surface == EnvironmentSurface.Rock ? 289 : 0, statistics.EcologicalCount);
    }

    [Fact]
    public void WaterLevelAndBuiltOverrideAreExplicit()
    {
        var field = new BiomeField(Recipe(), new PlaneTerrain(-1));
        Assert.True(field.TrySample(0, 0, out var water));
        Assert.Equal(EnvironmentSurface.Water, water.Surface);
        Assert.Equal(0, water.Weights.Sum);
        Assert.True(field.TrySample(0, 0, out var built, EnvironmentSurface.Built));
        Assert.Equal(EnvironmentSurface.Built, built.Surface);
        Assert.Equal(0, built.Weights.Sum);
        Assert.Equal(water.HeightMillimeters, built.HeightMillimeters);
        Assert.Equal(water.TemperatureMilliCelsius, built.TemperatureMilliCelsius);
    }

    [Fact]
    public void InvalidAndMissingTerrainAreUnavailableWithoutFallback()
    {
        foreach (var terrain in new ITerrainQuery[]
        {
            new PlaneTerrain(available: false), new PlaneTerrain(normalAvailable: false),
            new PlaneTerrain(float.NaN), new PlaneTerrain(float.PositiveInfinity), new PlaneTerrain(float.MaxValue),
            new PlaneTerrain(normal: Vector3.Zero), new PlaneTerrain(normal: new(float.NaN, 1, 0)),
            new PlaneTerrain(normal: -Vector3.UnitY)
        })
        {
            var field = new BiomeField(Recipe(), terrain);
            Assert.False(field.TrySample(0, 0, out _));
            var raster = BiomeFieldRaster.Create(field, default);
            Assert.False(raster.TryGetSample(0, 0, out _));
            var statistics = raster.GetStatistics();
            Assert.Equal(289, statistics.SampleCount);
            Assert.Equal(0, statistics.AvailableCount);
            Assert.Equal(0, statistics.EcologicalCount);
            Assert.Equal(default, statistics.MeanWeights);
            Assert.Equal(0, statistics.MeanTemperatureMilliCelsius);
        }
    }

    [Fact]
    public void ProfileGridCacheAndEnumLimitsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new BiomeField(new(), new PlaneTerrain()));
        Assert.Throws<InvalidOperationException>(() => new BiomeField(Recipe() with { GeneratorVersion = 2 }, new PlaneTerrain()));
        foreach (var profile in new[]
        {
            new BiomeFieldProfile { Version = 2 }, new BiomeFieldProfile { Id = "invalid ID" },
            new BiomeFieldProfile { TemperaturePeriodMillimeters = 0 },
            new BiomeFieldProfile { MoisturePeriodMillimeters = int.MaxValue },
            new BiomeFieldProfile { SoilPeriodMillimeters = -1 },
            new BiomeFieldProfile { TemperatureAmplitudeMilliCelsius = -1 },
            new BiomeFieldProfile { LapseMilliCelsiusPerKilometer = 20_001 },
            new BiomeFieldProfile { BaseTemperatureMilliCelsius = 50_001 },
            new BiomeFieldProfile { WaterLevelMillimeters = int.MinValue },
            new BiomeFieldProfile { WetlandHeightMillimeters = 0 }
        })
            Assert.Throws<InvalidOperationException>(() => new BiomeField(Recipe(), new PlaneTerrain(), profile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BiomeField(Recipe() with { Origin = new(int.MinValue, 0) }, new PlaneTerrain()));
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        Assert.False(field.TrySample(long.MinValue, long.MaxValue, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.TrySample(0, 0, out _, (EnvironmentSurface)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => BiomeFieldRaster.Create(field, new(2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BiomeFieldRaster.Create(field, new(-3, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BiomeFieldCache(field, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BiomeFieldCache(field, 33));
        var raster = BiomeFieldRaster.Create(field, default);
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.TryGetSample(17, 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.TryGetSample(0, -1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BiomeWeights()[(BiomeKind)9]);
    }

    [Fact]
    public void PackedMaximumRasterAndDistributionAreBoundedAndNormalized()
    {
        var field = new BiomeField(Recipe() with { SamplesPerSide = 129 }, new PlaneTerrain());
        var raster = BiomeFieldRaster.Create(field, default);
        Assert.Equal(129 * 129 * 26, raster.PackedByteCount);
        var statistics = raster.GetStatistics();
        Assert.Equal(129 * 129, statistics.SampleCount);
        Assert.Equal(statistics.SampleCount, statistics.AvailableCount);
        Assert.Equal(statistics.AvailableCount, statistics.EcologicalCount);
        var weights = statistics.MeanWeights;
        Assert.InRange(weights.Forest + weights.Meadow + weights.DrySlope + weights.Rock + weights.WetDepression, .999999999, 1.000000001);
        var counts = statistics.DominantCounts;
        Assert.Equal(statistics.EcologicalCount, counts.Forest + counts.Meadow + counts.DrySlope + counts.Rock + counts.WetDepression);
        Assert.InRange(statistics.MeanMoisture, 0, 1);
        Assert.InRange(statistics.MeanSoilFertility, 0, 1);
        Assert.Equal(64, raster.ComputeFingerprint().Sha256.Length);
    }

    [Fact]
    public void AllFiveNaturalBiomeClassesCanBecomeDominant()
    {
        var found = new HashSet<BiomeKind>();
        foreach (int height in new[] { 1, 100 })
        foreach (var normal in new[] { Vector3.UnitY, new Vector3(.55f, .835f, 0), new Vector3(1, .1f, 0) })
        for (ulong seed = 0; seed < 256; seed++)
        {
            var field = new BiomeField(Recipe(seed), new PlaneTerrain(height, normal));
            Assert.True(field.TrySample(0, 0, out var sample));
            Assert.Equal(ushort.MaxValue, sample.Weights.Sum);
            found.Add(sample.Weights.DominantBiome!.Value);
        }
        Assert.Equal(5, found.Count);
    }

    [Fact]
    public void EveryProfileParameterParticipatesInCanonicalIdentity()
    {
        var baseline = BiomeFieldRaster.Create(new(Recipe(), new PlaneTerrain()), default).ComputeFingerprint();
        foreach (var profile in new[]
        {
            new BiomeFieldProfile { TemperaturePeriodMillimeters = 1_000_000 },
            new BiomeFieldProfile { MoisturePeriodMillimeters = 700_000 },
            new BiomeFieldProfile { SoilPeriodMillimeters = 250_000 },
            new BiomeFieldProfile { BaseTemperatureMilliCelsius = 17_000 },
            new BiomeFieldProfile { TemperatureAmplitudeMilliCelsius = 13_000 },
            new BiomeFieldProfile { LapseMilliCelsiusPerKilometer = 6_000 },
            new BiomeFieldProfile { WaterLevelMillimeters = -1 },
            new BiomeFieldProfile { WetlandHeightMillimeters = 30_001 }
        })
            Assert.NotEqual(baseline.Sha256, BiomeFieldRaster.Create(new(Recipe(), new PlaneTerrain(), profile), default).ComputeFingerprint().Sha256);
        var field = new BiomeField(Recipe(), new PlaneTerrain());
        Assert.NotEqual(baseline.Sha256, BiomeFieldRaster.Create(field, default, EnvironmentSurface.Built).ComputeFingerprint().Sha256);
        baseline.GetCanonicalBytes()[0] = 0;
        Assert.Equal((byte)'F', baseline.GetCanonicalBytes()[0]);
    }

    [Fact]
    public void ExistingTerrainWorldNeedsNoEcsOrRenderer()
    {
        var settings = new WorldGridSettings { HeightSamplesPerSide = 17 };
        var heights = Enumerable.Repeat(100f, 17 * 17).ToArray();
        var terrain = new TerrainWorld(settings,
        [
            new(new(-1, 0), new(17, 256, heights)),
            new(new(0, 0), new(17, 256, heights))
        ]);
        var field = new BiomeField(Recipe(), terrain);
        var left = BiomeFieldRaster.Create(field, new(-1, 0));
        var right = BiomeFieldRaster.Create(field, default);
        for (int z = 0; z < 16; z++)
        {
            Assert.True(left.TryGetSample(16, z, out var a));
            Assert.True(right.TryGetSample(0, z, out var b));
            Assert.Equal(a, b);
        }
        Assert.False(right.TryGetSample(16, 0, out _));
        Assert.Equal(16 * 16, right.GetStatistics().AvailableCount);
    }
}
