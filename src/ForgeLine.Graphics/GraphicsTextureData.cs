namespace ForgeLine.Graphics;

public enum GraphicsTextureFormat
{
    Rgba8Unorm = 1,
    Bc7Unorm = 2,
}

public enum GraphicsTextureColorSpace
{
    Linear = 1,
    Srgb = 2,
}

public readonly record struct GraphicsTextureMipData(
    int Width,
    int Height,
    int RowPitch,
    byte[] Pixels);

public sealed record GraphicsTextureDescription(
    int Width,
    int Height,
    GraphicsTextureFormat Format,
    GraphicsTextureColorSpace ColorSpace,
    int MipCount)
{
    internal void Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                "Texture dimensions must be positive.");
        }

        if (!Enum.IsDefined(Format))
        {
            throw new ArgumentOutOfRangeException(nameof(Format));
        }

        if (!Enum.IsDefined(ColorSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(ColorSpace));
        }

        if (Format == GraphicsTextureFormat.Bc7Unorm && (Width % 4 != 0 || Height % 4 != 0))
        {
            throw new ArgumentException("BC7 top-level dimensions must be multiples of four.");
        }

        if (MipCount <= 0 || MipCount > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MipCount),
                "Texture mip count must be between one and 32.");
        }
    }
}

public sealed class GraphicsTextureData
{
    private const int RgbaChannelCount = 4;
    private readonly GraphicsTextureMipData[] _mips;

    public GraphicsTextureData(
        GraphicsTextureDescription description,
        IEnumerable<GraphicsTextureMipData> mips)
    {
        Description = description ??
            throw new ArgumentNullException(nameof(description));
        ArgumentNullException.ThrowIfNull(mips);

        _mips = mips.ToArray();
        Validate();
    }

    public GraphicsTextureDescription Description { get; }

    public IReadOnlyList<GraphicsTextureMipData> Mips => _mips;

    public long ResidentByteCount =>
        _mips.Sum(
            mip =>
                checked((long)mip.RowPitch * GetRowCount(mip.Height)));

    private int GetRowCount(int height) => Description.Format == GraphicsTextureFormat.Bc7Unorm ? (height + 3) / 4 : height;

    private void Validate()
    {
        Description.Validate();

        if (_mips.Length != Description.MipCount)
        {
            throw new ArgumentException(
                $"Texture declares {Description.MipCount} mips but provides {_mips.Length}.",
                nameof(_mips));
        }

        if (Description.Format is not (GraphicsTextureFormat.Rgba8Unorm or GraphicsTextureFormat.Bc7Unorm))
        {
            throw new NotSupportedException(
                $"Texture format {Description.Format} is not supported.");
        }

        int expectedWidth = Description.Width;
        int expectedHeight = Description.Height;

        for (int index = 0; index < _mips.Length; index++)
        {
            GraphicsTextureMipData mip = _mips[index];

            if (mip.Width != expectedWidth || mip.Height != expectedHeight)
            {
                throw new ArgumentException(
                    $"Texture mip {index} is {mip.Width}x{mip.Height}; expected {expectedWidth}x{expectedHeight}.",
                    nameof(_mips));
            }

            int minimumRowPitch = Description.Format == GraphicsTextureFormat.Bc7Unorm ? checked(((mip.Width + 3) / 4) * 16) : checked(mip.Width * RgbaChannelCount);
            if (mip.RowPitch < minimumRowPitch)
            {
                throw new ArgumentException(
                    $"Texture mip {index} row pitch is smaller than its format row width.",
                    nameof(_mips));
            }

            int expectedBytes = checked(mip.RowPitch * GetRowCount(mip.Height));
            if (mip.Pixels is null || mip.Pixels.Length != expectedBytes)
            {
                throw new ArgumentException(
                    $"Texture mip {index} pixel data length does not match its row pitch and height.",
                    nameof(_mips));
            }

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
        }
    }
}

public interface IGraphicsTexture : IDisposable
{
    GraphicsTextureDescription Description { get; }
}
