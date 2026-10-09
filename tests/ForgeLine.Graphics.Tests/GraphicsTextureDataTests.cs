using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class GraphicsTextureDataTests
{
    [Fact]
    public void Bc7UsesBlockRowsIncludingSmallMips()
    {
        var description = new GraphicsTextureDescription(8, 4, GraphicsTextureFormat.Bc7Unorm, GraphicsTextureColorSpace.Srgb, 4);
        var data = new GraphicsTextureData(description,
            [new(8, 4, 32, new byte[32]), new(4, 2, 16, new byte[16]),
             new(2, 1, 16, new byte[16]), new(1, 1, 16, new byte[16])]);
        Assert.Equal(80, data.ResidentByteCount);
        Assert.Throws<ArgumentException>(() => new GraphicsTextureData(description,
            [new(8, 4, 32, new byte[31]), new(4, 2, 16, new byte[16]),
             new(2, 1, 16, new byte[16]), new(1, 1, 16, new byte[16])]));
    }
    [Fact]
    public void AcceptsCompleteSuppliedMipChain()
    {
        var data =
            new GraphicsTextureData(
                new GraphicsTextureDescription(
                    4,
                    4,
                    GraphicsTextureFormat.Rgba8Unorm,
                    GraphicsTextureColorSpace.Srgb,
                    3),
                [
                    Mip(4, 4),
                    Mip(2, 2),
                    Mip(1, 1)
                ]);

        Assert.Equal(3, data.Mips.Count);
        Assert.Equal(84, data.ResidentByteCount);
    }

    [Fact]
    public void RejectsTruncatedMipData()
    {
        Assert.Throws<ArgumentException>(
            static () =>
                new GraphicsTextureData(
                    new GraphicsTextureDescription(
                        2,
                        2,
                        GraphicsTextureFormat.Rgba8Unorm,
                        GraphicsTextureColorSpace.Linear,
                        1),
                    [
                        new GraphicsTextureMipData(
                            2,
                            2,
                            8,
                            new byte[8])
                    ]));
    }

    private static GraphicsTextureMipData Mip(int width, int height)
    {
        int rowPitch = checked(width * 4);

        return new GraphicsTextureMipData(
            width,
            height,
            rowPitch,
            new byte[checked(rowPitch * height)]);
    }
}
