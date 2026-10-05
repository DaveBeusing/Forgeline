using System.Numerics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TerrainSurfaceTextureTests
{
    [Fact]
    public void RepeatedSamplingWrapsAcrossTextureBoundaries()
    {
        TerrainSurfaceTexture texture =
            TerrainSurfaceTexture.FromRgba8(
                2,
                2,
                [
                    255, 0, 0, 255,
                    0, 255, 0, 255,
                    0, 0, 255, 255,
                    255, 255, 255, 255
                ]);

        Vector3 origin =
            texture.SampleWorld(
                Vector2.Zero,
                2.0f);
        Vector3 wrapped =
            texture.SampleWorld(
                new Vector2(
                    2.0f,
                    0.0f),
                2.0f);

        Assert.Equal(
            origin,
            wrapped);
        Assert.Equal(
            new Vector3(
                1.0f,
                0.0f,
                0.0f),
            origin);
    }

    [Fact]
    public void BilinearSamplingBlendsNeighboringTexels()
    {
        TerrainSurfaceTexture texture =
            TerrainSurfaceTexture.FromRgba8(
                2,
                2,
                [
                    255, 0, 0, 255,
                    0, 255, 0, 255,
                    0, 0, 255, 255,
                    255, 255, 255, 255
                ]);

        Vector3 sample =
            texture.SampleRepeat(
                0.25f,
                0.25f);

        Assert.InRange(
            sample.X,
            0.49f,
            0.51f);
        Assert.InRange(
            sample.Y,
            0.49f,
            0.51f);
        Assert.InRange(
            sample.Z,
            0.49f,
            0.51f);
    }

    [Fact]
    public void RuntimePayloadRoundTripsIntoTerrainTexture()
    {
        byte[] pixels =
        [
            16, 32, 48, 255,
            64, 80, 96, 255
        ];

        using var stream =
            new MemoryStream();
        using (var writer =
               new BinaryWriter(
                   stream,
                   System.Text.Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(2);
            writer.Write(1);
            writer.Write(4);
            writer.Write(pixels);
        }

        TerrainSurfaceTexture texture =
            TerrainSurfaceTexture.FromRuntimePayload(
                stream.ToArray());

        Assert.Equal(
            2,
            texture.Width);
        Assert.Equal(
            1,
            texture.Height);

        Vector3 first =
            texture.SampleRepeat(
                0.0f,
                0.0f);

        Assert.InRange(
            first.X,
            16.0f / 255.0f - 0.0001f,
            16.0f / 255.0f + 0.0001f);
        Assert.InRange(
            first.Y,
            32.0f / 255.0f - 0.0001f,
            32.0f / 255.0f + 0.0001f);
        Assert.InRange(
            first.Z,
            48.0f / 255.0f - 0.0001f,
            48.0f / 255.0f + 0.0001f);
    }

    [Fact]
    public void TerrainProfileUsesTextureToVarySurfaceColor()
    {
        TerrainSurfaceTexture texture =
            TerrainSurfaceTexture.FromRgba8(
                2,
                1,
                [
                    255, 96, 32, 255,
                    32, 96, 255, 255
                ]);
        var material =
            new TerrainMaterialDefinition(
                TerrainMaterialSlot.Dirt,
                "material.world.terrain.dirt",
                new Vector3(
                    0.34f,
                    0.27f,
                    0.18f),
                0.92f,
                0.0f,
                "texture.world.terrain.test",
                texture,
                100.0f);
        var profile =
            new TerrainPresentationProfile(
                [material]);

        Vector4 first =
            profile.SampleBaseColor(
                new Vector3(
                    0.0f,
                    20.0f,
                    0.0f),
                Vector3.UnitY);
        Vector4 second =
            profile.SampleBaseColor(
                new Vector3(
                    50.0f,
                    20.0f,
                    0.0f),
                Vector3.UnitY);

        Assert.NotEqual(
            first,
            second);
    }
}
