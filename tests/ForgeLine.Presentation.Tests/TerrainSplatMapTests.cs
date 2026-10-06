using ForgeLine.Graphics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TerrainSplatMapTests
{
    [Fact]
    public void ZeroControlWeightsUseDeterministicFirstLayerFallback()
    {
        Span<byte> encoded =
            stackalloc byte[4];

        TerrainSplatMapBuilder.NormalizeControlWeights(
            [0.0f, 0.0f, 0.0f, 0.0f],
            encoded);

        Assert.Equal(
            new byte[] { 255, 0, 0, 0 },
            encoded.ToArray());
    }

    [Fact]
    public void ChunkControlMapKeepsEveryTexelNormalized()
    {
        TerrainChunk chunk =
            CreateFlatChunk(
                new ChunkCoordinate(
                    0,
                    0));
        TerrainChunkSplatData splat =
            TerrainSplatMapBuilder.Build(
                chunk,
                TerrainPresentationProfile.CreateCentralDivide());

        Assert.Equal(
            TerrainChunkSplatData.LayerCount,
            splat.Palette.Count);
        Assert.Equal(
            splat.Palette.Count,
            splat.Palette.Distinct().Count());

        foreach (GraphicsTextureMipData mip in
                 splat.Mips)
        {
            for (int offset = 0;
                 offset < mip.Pixels.Length;
                 offset += 4)
            {
                Assert.Equal(
                    255,
                    mip.Pixels[offset] +
                    mip.Pixels[offset + 1] +
                    mip.Pixels[offset + 2] +
                    mip.Pixels[offset + 3]);
            }
        }
    }

    [Fact]
    public void NaturalNeighborChunksPreserveControlBoundaryContinuity()
    {
        TerrainPresentationProfile profile =
            TerrainPresentationProfile.CreateCentralDivide();
        TerrainChunk west =
            CreateFlatChunk(
                new ChunkCoordinate(
                    0,
                    0));
        TerrainChunk east =
            CreateFlatChunk(
                new ChunkCoordinate(
                    1,
                    0));

        TerrainChunkSplatData westSplat =
            TerrainSplatMapBuilder.Build(
                west,
                profile);
        TerrainChunkSplatData eastSplat =
            TerrainSplatMapBuilder.Build(
                east,
                profile);

        Assert.Equal(
            westSplat.Palette,
            eastSplat.Palette);

        GraphicsTextureMipData westLevel =
            westSplat.Mips[0];
        GraphicsTextureMipData eastLevel =
            eastSplat.Mips[0];
        int side =
            TerrainChunkSplatData.ControlSamplesPerSide;

        for (int z = 0;
             z < side;
             z++)
        {
            int westOffset =
                checked(
                    (z *
                     side +
                     side -
                     1) *
                    4);
            int eastOffset =
                checked(
                    z *
                    side *
                    4);

            Assert.Equal(
                westLevel.Pixels.AsSpan(
                    westOffset,
                    4)
                .ToArray(),
                eastLevel.Pixels.AsSpan(
                    eastOffset,
                    4)
                .ToArray());
        }
    }

    [Fact]
    public void MaterialWeightsRemainNormalized()
    {
        TerrainPresentationProfile profile =
            TerrainPresentationProfile.CreateCentralDivide();
        Span<float> weights =
            stackalloc float[
                TerrainPresentationProfile.MaterialSlotCount];

        profile.CalculateMaterialWeights(
            new System.Numerics.Vector3(
                1_536.0f,
                12.0f,
                920.0f),
            System.Numerics.Vector3.UnitY,
            weights);

        Assert.InRange(
            weights.ToArray().Sum(),
            0.9999f,
            1.0001f);
        Assert.All(
            weights.ToArray(),
            weight =>
                Assert.InRange(
                    weight,
                    0.0f,
                    1.0f));
    }

    [Fact]
    public void MacroVariationIsDeterministicAndReadabilityBounded()
    {
        System.Numerics.Vector3[] positions =
        [
            new(0.0f, 0.0f, 0.0f),
            new(384.0f, 0.0f, 256.0f),
            new(1_024.0f, 0.0f, 1_536.0f),
            new(2_560.0f, 0.0f, 768.0f)
        ];

        float[] values =
            positions
                .Select(
                    TerrainRenderer.CalculateMacroVariation)
                .ToArray();

        Assert.All(
            values,
            value =>
                Assert.InRange(
                    value,
                    0.90f,
                    1.08f));
        Assert.True(
            values.Distinct().Count() >
            1);
        Assert.Equal(
            values[2],
            TerrainRenderer.CalculateMacroVariation(
                positions[2]));
    }

    private static TerrainChunk CreateFlatChunk(
        ChunkCoordinate coordinate)
    {
        var settings =
            new WorldGridSettings();
        int count =
            settings.HeightSamplesPerSide;
        var heights =
            new float[
                checked(
                    count *
                    count)];

        return new TerrainChunk(
            coordinate,
            new TerrainHeightfield(
                count,
                settings.ChunkSizeMeters,
                heights));
    }
}
