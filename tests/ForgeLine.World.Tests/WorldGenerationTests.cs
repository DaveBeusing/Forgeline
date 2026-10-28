using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class WorldGenerationTests
{
    private static WorldGenerationRecipe MiniRecipe => new()
    {
        Seed = ulong.MaxValue, Origin = new(-1, -1), WidthInChunks = 2, HeightInChunks = 2,
        ChunkSizeMillimeters = 2000, SamplesPerSide = 3, RegionSizeInChunks = 1,
        TerrainProfileId = "t", ClimateProfileId = "c", GeographyProfileId = "g", RulesProfileId = "r"
    };

    private static WorldGenerationContent[] MiniContent()
    {
        var random = new WorldGenerationRandom(MiniRecipe);
        return new[] { (-2L, -2L, 10UL), (-1L, 0L, 20UL), (0L, -1L, 30UL) }
            .Select(p =>
            {
                var writer = new WorldCanonicalWriter();
                writer.WriteUInt64(random.At(WorldGenerationDomain.Terrain, new(p.Item1, p.Item2), 7, 3));
                return new WorldGenerationContent(new(p.Item1, p.Item2), 1, 1, p.Item3, writer.ToArray());
            }).ToArray();
    }

    [Fact]
    public void SymbolicMiniworldHasFixedCanonicalBytesAndSha256()
    {
        var fingerprint = WorldGenerationFingerprint.Gameplay(MiniRecipe, MiniContent());
        Assert.Equal("465747310100000001000000ffffffffffffffffffffffffffffffff0200000002000000d0070000030000000100000001000000740100000063010000006701000000720000000003000000fefffffffffffffffeffffffffffffff01000000010000000a000000000000000800000045043721fdb8fbc60000000000000000ffffffffffffffff01000000010000001e000000000000000800000014a903707fc0ee7dffffffffffffffff0000000000000000010000000100000014000000000000000800000019ac34fbb0cd5ee5", Convert.ToHexStringLower(fingerprint.GetCanonicalBytes()));
        Assert.Equal("97a719d04077730ffb70fef6ccbcfd84cda5e7c4603adbd555c37c6dc5da8c12", fingerprint.Sha256);
        Assert.Equal(WorldFingerprintKind.Gameplay, fingerprint.Kind);
        Parallel.For(0, 64, index =>
        {
            var content = MiniContent();
            if (index % 2 == 0) Array.Reverse(content);
            else content = [content[1], content[2], content[0]];
            var repeated = WorldGenerationFingerprint.Gameplay(MiniRecipe, content);
            Assert.Equal(fingerprint.GetCanonicalBytes(), repeated.GetCanonicalBytes());
            Assert.Equal(fingerprint.Sha256, repeated.Sha256);
        });
    }

    [Theory]
    [InlineData(WorldGenerationDomain.Terrain, 0xc6fbb8fd21370445UL)]
    [InlineData(WorldGenerationDomain.Climate, 0x5ecf103b027ad788UL)]
    [InlineData(WorldGenerationDomain.Geography, 0x54e357d80194d562UL)]
    [InlineData(WorldGenerationDomain.Vegetation, 0x98100e8c21cb65d5UL)]
    [InlineData(WorldGenerationDomain.Props, 0x8361b5582407d9b5UL)]
    public void DomainsHaveFixedIndependentReferenceValues(WorldGenerationDomain domain, ulong expected)
    {
        var random = new WorldGenerationRandom(MiniRecipe);
        Assert.Equal(expected, random.At(domain, new(-2, -2), 7, 3));
        var stream = random.CreateStream(domain, new(-2, -2), 3);
        var untouched = random.CreateStream(domain, new(-2, -2), 3);
        for (ulong index = 0; index < 32; index++)
        {
            Assert.Equal(random.At(domain, new(-2, -2), index, 3), stream.NextUInt64());
            _ = random.At(WorldGenerationDomain.Props, new(47, -91), index, 19);
        }
        Assert.Equal(random.At(domain, new(-2, -2), 0, 3), untouched.NextUInt64());
        Parallel.For(0, 64, index =>
            Assert.Equal(random.At(domain, new(index - 32, -index), 9, 7),
                random.CreateStream(domain, new(index - 32, -index), 7).NextAtNine()));
    }

    [Fact]
    public void DerivationIncludesCoordinatesStreamsAndFullSeedBits()
    {
        var random = new WorldGenerationRandom(MiniRecipe);
        Assert.Equal(0xb2a81e3a2261f47bUL,
            random.At(WorldGenerationDomain.Props, new(long.MinValue, long.MaxValue), ulong.MaxValue, ulong.MaxValue));
        var zero = new WorldGenerationRandom(MiniRecipe with { Seed = 0 });
        Assert.Equal(0x1002a55d1b2ef512UL, zero.At(WorldGenerationDomain.Terrain, new(0, 0)));
        ulong baseline = random.At(WorldGenerationDomain.Terrain, new(-2, -2), 7, 3);
        Assert.NotEqual(baseline, random.At(WorldGenerationDomain.Terrain, new(-1, -2), 7, 3));
        Assert.NotEqual(baseline, random.At(WorldGenerationDomain.Terrain, new(-2, -1), 7, 3));
        Assert.NotEqual(baseline, random.At(WorldGenerationDomain.Terrain, new(-2, -2), 7, 4));
        Assert.NotEqual(baseline, zero.At(WorldGenerationDomain.Terrain, new(-2, -2), 7, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.CreateStream((WorldGenerationDomain)0, default));
    }

    [Fact]
    public void NegativeChunksSharedEdgesAndExtremeCoordinatesAreCanonical()
    {
        Assert.Equal(new WorldSampleCoordinate(-1, -64),
            WorldSampleCoordinate.FromChunk(new(-1, -1), 63, 0, 65));
        Assert.Equal(new WorldSampleLocation(new(-1, -1), 63, 0), new WorldSampleCoordinate(-1, -64).ToChunk(65));
        Assert.Equal(WorldSampleCoordinate.FromChunk(new(-1, -1), 64, 64, 65),
            WorldSampleCoordinate.FromChunk(new(0, 0), 0, 0, 65));
        Assert.Equal(new WorldSampleLocation(new(0, 0), 0, 0), new WorldSampleCoordinate(0, 0).ToChunk(65));
        foreach (int coordinate in new[] { int.MinValue, -1, 0, int.MaxValue })
        {
            var sample = WorldSampleCoordinate.FromChunk(new(coordinate, coordinate), 0, 0, 129);
            Assert.Equal(new WorldSampleLocation(new(coordinate, coordinate), 0, 0), sample.ToChunk(129));
        }
        Assert.Throws<OverflowException>(() => new WorldSampleCoordinate(long.MinValue, long.MaxValue).ToChunk(65));
        Assert.Throws<OverflowException>(() => WorldSampleCoordinate.FromChunk(new(int.MaxValue, 0), 64, 0, 65).ToChunk(65));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSampleCoordinate.FromChunk(default, -1, 0, 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSampleCoordinate.FromChunk(default, 65, 0, 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSampleCoordinate().ToChunk(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSampleCoordinate().ToChunk(130));
    }

    [Fact]
    public void InvalidRecipesFailWithDefinedDiagnosticsBeforeGeneration()
    {
        Assert.Equal(WorldGenerationError.UnknownGeneratorVersion,
            (MiniRecipe with { GeneratorVersion = 99 }).GetValidationError()!.Value.Code);
        foreach (var invalid in new[]
        {
            MiniRecipe with { WidthInChunks = 0 }, MiniRecipe with { HeightInChunks = 257 },
            MiniRecipe with { SamplesPerSide = 1 }, MiniRecipe with { SamplesPerSide = int.MaxValue },
            MiniRecipe with { ChunkSizeMillimeters = 0 }, MiniRecipe with { ChunkSizeMillimeters = 2001 },
            MiniRecipe with { RegionSizeInChunks = 33 },
            MiniRecipe with { WidthInChunks = 256, HeightInChunks = 256, SamplesPerSide = 129 },
            MiniRecipe with { Origin = new(int.MaxValue, 0) },
            MiniRecipe with { Origin = new(0, int.MaxValue) }
        })
        {
            Assert.Equal(WorldGenerationError.InvalidLimits, invalid.GetValidationError()!.Value.Code);
            Assert.Throws<InvalidOperationException>(() => new WorldGenerationRandom(invalid));
            Assert.Throws<InvalidOperationException>(() => WorldGenerationFingerprint.Gameplay(invalid, []));
        }
        foreach (string value in new[] { "", "UPPER", "ä", "space id", new string('a', 97) })
            Assert.Equal(WorldGenerationError.InvalidProfileId,
                (MiniRecipe with { TerrainProfileId = value }).GetValidationError()!.Value.Code);
        Assert.Equal(WorldGenerationError.InvalidProfileId,
            (MiniRecipe with { ClimateProfileId = null! }).GetValidationError()!.Value.Code);
        Assert.Equal(WorldGenerationError.UnknownOptions, (MiniRecipe with { Options = 1 }).GetValidationError()!.Value.Code);
        Assert.Null((MiniRecipe with { Seed = 0, Origin = new(int.MinValue, int.MinValue) }).GetValidationError());
        Assert.Null((MiniRecipe with { Origin = new(int.MaxValue - 1, int.MaxValue - 1) }).GetValidationError());
        Assert.Null((MiniRecipe with { WidthInChunks = 256, HeightInChunks = 256, SamplesPerSide = 2 }).GetValidationError());
    }

    [Fact]
    public void GameplayIdentityIncludesEveryRecipeInputAndContentSchema()
    {
        string original = WorldGenerationFingerprint.Gameplay(MiniRecipe, MiniContent()).Sha256;
        foreach (var changed in new[]
        {
            MiniRecipe with { Seed = 0 }, MiniRecipe with { Origin = new(0, -1) },
            MiniRecipe with { WidthInChunks = 3 }, MiniRecipe with { HeightInChunks = 3 },
            MiniRecipe with { ChunkSizeMillimeters = 4000 }, MiniRecipe with { SamplesPerSide = 2 },
            MiniRecipe with { RegionSizeInChunks = 2 }, MiniRecipe with { TerrainProfileId = "t2" },
            MiniRecipe with { ClimateProfileId = "c2" }, MiniRecipe with { GeographyProfileId = "g2" },
            MiniRecipe with { RulesProfileId = "r2" }
        })
            Assert.NotEqual(original, WorldGenerationFingerprint.Gameplay(changed, MiniContent()).Sha256);
        var content = MiniContent();
        content[0] = new(new(-2, -2), 1, 2, 10, [1, 2, 3]);
        Assert.NotEqual(original, WorldGenerationFingerprint.Gameplay(MiniRecipe, content).Sha256);
    }

    [Fact]
    public void DecorationIdentityIsSeparateAndBindsToGameplay()
    {
        var gameplay = WorldGenerationFingerprint.Gameplay(MiniRecipe, MiniContent());
        var visual = WorldGenerationFingerprint.Decoration(gameplay, new(), MiniContent());
        Assert.Equal(WorldFingerprintKind.Decoration, visual.Kind);
        Assert.NotEqual(gameplay.Sha256, visual.Sha256);
        foreach (var changed in new[]
        {
            new WorldDecorationRecipe { VegetationProfileId = "vegetation.other.v1" },
            new WorldDecorationRecipe { PropsProfileId = "props.other.v1" },
            new WorldDecorationRecipe { RulesProfileId = "decoration.other.v1" }
        })
            Assert.NotEqual(visual.Sha256, WorldGenerationFingerprint.Decoration(gameplay, changed, MiniContent()).Sha256);
        var changedGameplay = WorldGenerationFingerprint.Gameplay(MiniRecipe with { Seed = 1 }, MiniContent());
        Assert.NotEqual(visual.Sha256, WorldGenerationFingerprint.Decoration(changedGameplay, new(), MiniContent()).Sha256);
        Assert.NotEqual(visual.Sha256, WorldGenerationFingerprint.Decoration(gameplay, new(), []).Sha256);
        Assert.Equal(gameplay.Sha256, WorldGenerationFingerprint.Gameplay(MiniRecipe, MiniContent()).Sha256);
        Assert.Throws<ArgumentException>(() => WorldGenerationFingerprint.Decoration(visual, new(), []));
        Assert.Throws<InvalidOperationException>(() => WorldGenerationFingerprint.Decoration(gameplay, new() { Version = 2 }, []));
    }

    [Fact]
    public void ContentIsCopiedKeysAreUniqueAndBudgetsAreEnforced()
    {
        byte[] payload = [1, 2, 3];
        var record = new WorldGenerationContent(default, 1, 1, 0, payload);
        var original = WorldGenerationFingerprint.Gameplay(MiniRecipe, [record]);
        payload[0] = 99;
        original.GetCanonicalBytes()[0] = 99;
        Assert.Equal(original.Sha256, WorldGenerationFingerprint.Gameplay(MiniRecipe, [record]).Sha256);
        Assert.Equal((byte)'F', original.GetCanonicalBytes()[0]);
        Assert.Throws<ArgumentException>(() => WorldGenerationFingerprint.Gameplay(MiniRecipe, [record, record]));
        Assert.Throws<ArgumentException>(() => WorldGenerationFingerprint.Gameplay(MiniRecipe,
            [record, new(default, 1, 2, 0, [])]));
        Assert.Throws<ArgumentException>(() => WorldGenerationFingerprint.Gameplay(MiniRecipe, [null!]));
        Assert.Throws<ArgumentException>(() => WorldGenerationFingerprint.Gameplay(MiniRecipe,
            Enumerable.Repeat(record, WorldGenerationFingerprint.MaximumRecords + 1)));
        Assert.Throws<ArgumentException>(() => new WorldGenerationContent(default, 1, 1, 0,
            new byte[WorldGenerationContent.MaximumPayloadBytes + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldGenerationContent(default, 0, 1, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldGenerationContent(default, 1, 0, 0, []));
        byte[] largePayload = new byte[WorldGenerationContent.MaximumPayloadBytes];
        Assert.Throws<InvalidOperationException>(() => WorldGenerationFingerprint.Gameplay(MiniRecipe,
            Enumerable.Range(0, 256).Select(i => new WorldGenerationContent(default, 1, 1, (ulong)i, largePayload))));
    }

    [Fact]
    public void WriterUsesExplicitLittleEndianTwosComplementAndLengths()
    {
        var writer = new WorldCanonicalWriter();
        writer.WriteUInt32(0x01020304);
        writer.WriteInt32(-1);
        writer.WriteUInt64(0x0102030405060708);
        writer.WriteInt64(-2);
        writer.WriteIdentifier("a");
        writer.WriteBytes([]);
        Assert.Equal("04030201ffffffff0807060504030201feffffffffffffff010000006100000000",
            Convert.ToHexStringLower(writer.ToArray()));
        Assert.Throws<ArgumentException>(() => writer.WriteIdentifier("Ä"));
        var bounded = new WorldCanonicalWriter();
        bounded.WriteBytes(new byte[WorldCanonicalWriter.MaximumBytes - 4]);
        Assert.Throws<InvalidOperationException>(() => bounded.WriteUInt32(1));
    }

    [Fact]
    public void ResultDistinguishesFailureFromCompletedValue()
    {
        var completed = new WorldGenerationResult<WorldGenerationRecipe>(MiniRecipe);
        Assert.True(completed.Succeeded);
        Assert.NotNull(completed.Value);
        Assert.Null(completed.Diagnostic);
        var error = new WorldGenerationDiagnostic(WorldGenerationError.InvalidContent, "Invalid content.");
        var failed = new WorldGenerationResult<WorldGenerationRecipe>(error);
        Assert.False(failed.Succeeded);
        Assert.Null(failed.Value);
        Assert.Equal(error, failed.Diagnostic);
        Assert.Throws<ArgumentNullException>(() => new WorldGenerationResult<WorldGenerationRecipe>(null!));
        Assert.Throws<ArgumentException>(() => new WorldGenerationResult<WorldGenerationRecipe>(default(WorldGenerationDiagnostic)));
    }
}

internal static class WorldGenerationSequenceTestExtensions
{
    internal static ulong NextAtNine(this WorldGenerationSequence stream)
    {
        for (int i = 0; i < 9; i++) _ = stream.NextUInt64();
        return stream.NextUInt64();
    }
}
