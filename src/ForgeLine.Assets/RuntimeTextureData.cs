namespace ForgeLine.Assets;

public enum RuntimeTextureFormat
{
    Rgba8Unorm = 1,
}

public enum RuntimeTextureColorSpace
{
    Linear = 1,
    Srgb = 2,
}

public enum RuntimeTextureUsage
{
    Color = 1,
    Normal = 2,
    Orm = 3,
    Emissive = 4,
    GenericData = 5,
}

public readonly record struct RuntimeTextureMipLevel(
    int Width,
    int Height,
    int RowPitch,
    byte[] Pixels);

public sealed class RuntimeTextureData
{
    public const int CurrentPayloadVersion = 2;

    private const int LegacyPayloadVersion = 1;
    private const int RgbaChannelCount = 4;
    private const int MaximumDimension = 16_384;

    private readonly RuntimeTextureMipLevel[] _mips;

    public RuntimeTextureData(
        int width,
        int height,
        RuntimeTextureFormat format,
        RuntimeTextureColorSpace colorSpace,
        RuntimeTextureUsage usage,
        IEnumerable<RuntimeTextureMipLevel> mips)
    {
        ArgumentNullException.ThrowIfNull(mips);

        Width = width;
        Height = height;
        Format = format;
        ColorSpace = colorSpace;
        Usage = usage;
        _mips = mips.ToArray();

        Validate();
    }

    public int Width { get; }

    public int Height { get; }

    public RuntimeTextureFormat Format { get; }

    public RuntimeTextureColorSpace ColorSpace { get; }

    public RuntimeTextureUsage Usage { get; }

    public IReadOnlyList<RuntimeTextureMipLevel> Mips => _mips;

    public long ResidentByteCount =>
        _mips.Sum(
            static mip =>
                checked(
                    (long)mip.RowPitch *
                    mip.Height));

    public static RuntimeTextureData FromRgba8(
        int width,
        int height,
        ReadOnlySpan<byte> pixels,
        RuntimeTextureColorSpace colorSpace,
        RuntimeTextureUsage usage)
    {
        int rowPitch =
            checked(
                width *
                RgbaChannelCount);
        int expectedLength =
            checked(
                rowPitch *
                height);

        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"RGBA8 texture data contains {pixels.Length} bytes; expected {expectedLength}.",
                nameof(pixels));
        }

        return new RuntimeTextureData(
            width,
            height,
            RuntimeTextureFormat.Rgba8Unorm,
            colorSpace,
            usage,
            [
                new RuntimeTextureMipLevel(
                    width,
                    height,
                    rowPitch,
                    pixels.ToArray())
            ]);
    }

    public byte[] ToPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(CurrentPayloadVersion);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write((int)Format);
        writer.Write((int)ColorSpace);
        writer.Write((int)Usage);
        writer.Write(_mips.Length);

        foreach (RuntimeTextureMipLevel mip in _mips)
        {
            writer.Write(mip.Width);
            writer.Write(mip.Height);
            writer.Write(mip.RowPitch);
            writer.Write(mip.Pixels.Length);
            writer.Write(mip.Pixels);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static RuntimeTextureData FromPayload(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(int) * 4)
        {
            throw new InvalidDataException(
                "Runtime texture payload is incomplete.");
        }

        using var stream =
            new MemoryStream(
                payload.ToArray(),
                writable: false);
        using var reader =
            new BinaryReader(
                stream);

        int version =
            reader.ReadInt32();

        return version switch
        {
            LegacyPayloadVersion =>
                ReadLegacyPayload(
                    reader,
                    stream),
            CurrentPayloadVersion =>
                ReadCurrentPayload(
                    reader,
                    stream),
            _ =>
                throw new InvalidDataException(
                    $"Runtime texture payload version {version} is not supported.")
        };
    }

    private static RuntimeTextureData ReadLegacyPayload(
        BinaryReader reader,
        Stream stream)
    {
        int width =
            reader.ReadInt32();
        int height =
            reader.ReadInt32();
        int channels =
            reader.ReadInt32();

        if (channels != RgbaChannelCount)
        {
            throw new InvalidDataException(
                $"Legacy runtime texture payload uses unsupported channel count {channels}.");
        }

        ValidateDimensions(
            width,
            height);

        int rowPitch =
            checked(
                width *
                RgbaChannelCount);
        int expectedLength =
            checked(
                rowPitch *
                height);

        if (stream.Length - stream.Position != expectedLength)
        {
            throw new InvalidDataException(
                "Legacy runtime texture payload length does not match its dimensions.");
        }

        byte[] pixels =
            reader.ReadBytes(
                expectedLength);
        if (pixels.Length != expectedLength)
        {
            throw new EndOfStreamException(
                "Legacy runtime texture payload ended before all pixels were read.");
        }

        return new RuntimeTextureData(
            width,
            height,
            RuntimeTextureFormat.Rgba8Unorm,
            RuntimeTextureColorSpace.Srgb,
            RuntimeTextureUsage.Color,
            [
                new RuntimeTextureMipLevel(
                    width,
                    height,
                    rowPitch,
                    pixels)
            ]);
    }

    private static RuntimeTextureData ReadCurrentPayload(
        BinaryReader reader,
        Stream stream)
    {
        int width =
            reader.ReadInt32();
        int height =
            reader.ReadInt32();
        var format =
            (RuntimeTextureFormat)reader.ReadInt32();
        var colorSpace =
            (RuntimeTextureColorSpace)reader.ReadInt32();
        var usage =
            (RuntimeTextureUsage)reader.ReadInt32();
        int mipCount =
            reader.ReadInt32();

        if (mipCount <= 0 ||
            mipCount > 32)
        {
            throw new InvalidDataException(
                $"Runtime texture payload declares invalid mip count {mipCount}.");
        }

        var mips =
            new RuntimeTextureMipLevel[mipCount];

        for (int index = 0;
             index < mipCount;
             index++)
        {
            if (stream.Length - stream.Position < sizeof(int) * 4)
            {
                throw new InvalidDataException(
                    "Runtime texture mip metadata is truncated.");
            }

            int mipWidth =
                reader.ReadInt32();
            int mipHeight =
                reader.ReadInt32();
            int rowPitch =
                reader.ReadInt32();
            int byteCount =
                reader.ReadInt32();

            if (byteCount < 0 ||
                stream.Length - stream.Position < byteCount)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} data is truncated.");
            }

            byte[] pixels =
                reader.ReadBytes(
                    byteCount);
            if (pixels.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"Runtime texture mip {index} ended before all pixels were read.");
            }

            mips[index] =
                new RuntimeTextureMipLevel(
                    mipWidth,
                    mipHeight,
                    rowPitch,
                    pixels);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException(
                "Runtime texture payload contains trailing data.");
        }

        return new RuntimeTextureData(
            width,
            height,
            format,
            colorSpace,
            usage,
            mips);
    }

    private void Validate()
    {
        ValidateDimensions(
            Width,
            Height);

        if (!Enum.IsDefined(Format))
        {
            throw new InvalidDataException(
                $"Runtime texture format {(int)Format} is invalid.");
        }

        if (!Enum.IsDefined(ColorSpace))
        {
            throw new InvalidDataException(
                $"Runtime texture color space {(int)ColorSpace} is invalid.");
        }

        if (!Enum.IsDefined(Usage))
        {
            throw new InvalidDataException(
                $"Runtime texture usage {(int)Usage} is invalid.");
        }

        if (Format != RuntimeTextureFormat.Rgba8Unorm)
        {
            throw new InvalidDataException(
                $"Runtime texture format {Format} is not supported.");
        }

        if (Usage is RuntimeTextureUsage.Normal or
            RuntimeTextureUsage.Orm or
            RuntimeTextureUsage.GenericData &&
            ColorSpace != RuntimeTextureColorSpace.Linear)
        {
            throw new InvalidDataException(
                $"Runtime texture usage {Usage} requires linear color space.");
        }

        if (_mips.Length == 0 ||
            _mips.Length > 32)
        {
            throw new InvalidDataException(
                "Runtime texture must contain between one and 32 mip levels.");
        }

        int expectedWidth =
            Width;
        int expectedHeight =
            Height;

        for (int index = 0;
             index < _mips.Length;
             index++)
        {
            RuntimeTextureMipLevel mip =
                _mips[index];

            if (mip.Width != expectedWidth ||
                mip.Height != expectedHeight)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} is {mip.Width}x{mip.Height}; expected {expectedWidth}x{expectedHeight}.");
            }

            int minimumRowPitch =
                checked(
                    mip.Width *
                    RgbaChannelCount);

            if (mip.RowPitch < minimumRowPitch)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} row pitch {mip.RowPitch} is smaller than {minimumRowPitch}.");
            }

            int expectedBytes =
                checked(
                    mip.RowPitch *
                    mip.Height);

            if (mip.Pixels is null ||
                mip.Pixels.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} contains {mip.Pixels?.Length ?? 0} bytes; expected {expectedBytes}.");
            }

            expectedWidth =
                Math.Max(
                    1,
                    expectedWidth / 2);
            expectedHeight =
                Math.Max(
                    1,
                    expectedHeight / 2);
        }
    }

    private static void ValidateDimensions(
        int width,
        int height)
    {
        if (width <= 0 ||
            height <= 0 ||
            width > MaximumDimension ||
            height > MaximumDimension)
        {
            throw new InvalidDataException(
                $"Runtime texture dimensions {width}x{height} are invalid; maximum supported dimension is {MaximumDimension}.");
        }
    }
}
