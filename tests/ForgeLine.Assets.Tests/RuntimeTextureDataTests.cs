using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RuntimeTextureDataTests
{
    [Fact]
    public void MultiMipPayloadRoundTripsMetadataAndSubresources()
    {
        var texture =
            new RuntimeTextureData(
                4,
                4,
                RuntimeTextureFormat.Rgba8Unorm,
                RuntimeTextureColorSpace.Linear,
                RuntimeTextureUsage.Orm,
                [
                    Mip(4, 4, 16, 0x11),
                    Mip(2, 2, 8, 0x22),
                    Mip(1, 1, 4, 0x33)
                ]);

        RuntimeTextureData decoded =
            RuntimeTextureData.FromPayload(
                texture.ToPayload());

        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(RuntimeTextureFormat.Rgba8Unorm, decoded.Format);
        Assert.Equal(RuntimeTextureColorSpace.Linear, decoded.ColorSpace);
        Assert.Equal(RuntimeTextureUsage.Orm, decoded.Usage);
        Assert.Equal(3, decoded.Mips.Count);
        Assert.Equal(84, decoded.ResidentByteCount);
        Assert.Equal(0x22, decoded.Mips[1].Pixels[0]);
    }

    [Fact]
    public void RejectsInconsistentMipDimensions()
    {
        Assert.Throws<InvalidDataException>(
            static () =>
                new RuntimeTextureData(
                    4,
                    4,
                    RuntimeTextureFormat.Rgba8Unorm,
                    RuntimeTextureColorSpace.Srgb,
                    RuntimeTextureUsage.Color,
                    [
                        Mip(4, 4, 16, 0),
                        Mip(3, 2, 12, 0)
                    ]));
    }

    [Theory]
    [InlineData(RuntimeTextureUsage.Normal)]
    [InlineData(RuntimeTextureUsage.Orm)]
    [InlineData(RuntimeTextureUsage.GenericData)]
    public void DataTexturesRequireLinearColorSpace(
        RuntimeTextureUsage usage)
    {
        Assert.Throws<InvalidDataException>(
            () =>
                RuntimeTextureData.FromRgba8(
                    1,
                    1,
                    [0, 0, 0, 255],
                    RuntimeTextureColorSpace.Srgb,
                    usage));
    }

    [Fact]
    public void LegacyRgbaPayloadRemainsReadableAsColorContent()
    {
        using var stream =
            new MemoryStream();
        using (var writer =
               new BinaryWriter(
                   stream,
                   System.Text.Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(1);
            writer.Write(1);
            writer.Write(4);
            writer.Write(
                new byte[]
                {
                    10,
                    20,
                    30,
                    255
                });
        }

        RuntimeTextureData decoded =
            RuntimeTextureData.FromPayload(
                stream.ToArray());

        Assert.Equal(RuntimeTextureColorSpace.Srgb, decoded.ColorSpace);
        Assert.Equal(RuntimeTextureUsage.Color, decoded.Usage);
        Assert.Equal(10, decoded.Mips[0].Pixels[0]);
    }

    private static RuntimeTextureMipLevel Mip(
        int width,
        int height,
        int rowPitch,
        byte value) =>
        new(
            width,
            height,
            rowPitch,
            Enumerable.Repeat(
                    value,
                    checked(rowPitch * height))
                .ToArray());
}
