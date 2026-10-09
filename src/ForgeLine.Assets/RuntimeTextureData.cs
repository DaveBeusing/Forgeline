namespace ForgeLine.Assets;

public enum RuntimeTextureFormat
{
    Rgba8Unorm = 1,
    Bc7Unorm = 2,
}

public enum RuntimeTextureColorSpace
{
    Linear = 1,
    Srgb = 2,
}

public enum RuntimeTextureUsage
{
    BaseColor = 1,
    Color = BaseColor,
    Normal = 2,
    Orm = 3,
    Emissive = 4,
    GenericData = 5,
    TerrainControl = 6,
}

public readonly record struct RuntimeTextureMipLevel(
    int Width,
    int Height,
    int RowPitch,
    byte[] Pixels);

public sealed class RuntimeTextureData
{
    public const int CurrentPayloadVersion = 3;

    private const int LegacyPayloadVersion = 1;
    private const int PreviousPayloadVersion = 2;
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
                mip =>
                checked(
                    (long)mip.RowPitch *
                    (Format == RuntimeTextureFormat.Bc7Unorm ? (mip.Height + 3) / 4 : mip.Height)));

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

        int dataOffset =
            checked(
                sizeof(int) *
                (7 +
                 _mips.Length *
                 5));
        int runningOffset =
            dataOffset;

        foreach (RuntimeTextureMipLevel mip in _mips)
        {
            writer.Write(mip.Width);
            writer.Write(mip.Height);
            writer.Write(mip.RowPitch);
            writer.Write(runningOffset);
            writer.Write(mip.Pixels.Length);

            runningOffset =
                checked(
                    runningOffset +
                    mip.Pixels.Length);
        }

        foreach (RuntimeTextureMipLevel mip in _mips)
        {
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
            PreviousPayloadVersion =>
                ReadVersion2Payload(
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

    private static RuntimeTextureData ReadVersion2Payload(
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

    private static RuntimeTextureData ReadCurrentPayload(
        BinaryReader reader,
        Stream stream)
    {
        if (stream.Length - stream.Position <
            sizeof(int) * 6)
        {
            throw new InvalidDataException(
                "Runtime texture header is truncated.");
        }

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

        long tableBytes =
            checked(
                (long)mipCount *
                sizeof(int) *
                5);
        if (stream.Length - stream.Position <
            tableBytes)
        {
            throw new InvalidDataException(
                "Runtime texture subresource table is truncated.");
        }

        var entries =
            new SerializedMipEntry[mipCount];

        for (int index = 0;
             index < mipCount;
             index++)
        {
            entries[index] =
                new SerializedMipEntry(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
        }

        long expectedOffset =
            stream.Position;
        var mips =
            new RuntimeTextureMipLevel[mipCount];

        for (int index = 0;
             index < mipCount;
             index++)
        {
            SerializedMipEntry entry =
                entries[index];

            if (entry.Offset != expectedOffset)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} offset {entry.Offset} does not match expected offset {expectedOffset}.");
            }

            if (entry.ByteCount < 0 ||
                entry.Offset < 0 ||
                (long)entry.Offset +
                entry.ByteCount >
                stream.Length)
            {
                throw new InvalidDataException(
                    $"Runtime texture mip {index} range is invalid or truncated.");
            }

            stream.Position =
                entry.Offset;
            byte[] pixels =
                reader.ReadBytes(
                    entry.ByteCount);
            if (pixels.Length !=
                entry.ByteCount)
            {
                throw new EndOfStreamException(
                    $"Runtime texture mip {index} ended before all pixels were read.");
            }

            mips[index] =
                new RuntimeTextureMipLevel(
                    entry.Width,
                    entry.Height,
                    entry.RowPitch,
                    pixels);
            expectedOffset =
                checked(
                    (long)entry.Offset +
                    entry.ByteCount);
        }

        if (expectedOffset !=
            stream.Length)
        {
            throw new InvalidDataException(
                "Runtime texture payload contains trailing or unreferenced data.");
        }

        return new RuntimeTextureData(
            width,
            height,
            format,
            colorSpace,
            usage,
            mips);
    }

    private readonly record struct SerializedMipEntry(
        int Width,
        int Height,
        int RowPitch,
        int Offset,
        int ByteCount);

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

        if (Format is not (RuntimeTextureFormat.Rgba8Unorm or RuntimeTextureFormat.Bc7Unorm))
        {
            throw new InvalidDataException(
                $"Runtime texture format {Format} is not supported.");
        }

        if (Format == RuntimeTextureFormat.Bc7Unorm && (Width % 4 != 0 || Height % 4 != 0))
        {
            throw new InvalidDataException("BC7 top-level dimensions must be multiples of four.");
        }

        if (Usage is RuntimeTextureUsage.Normal or
            RuntimeTextureUsage.Orm or
            RuntimeTextureUsage.GenericData or
            RuntimeTextureUsage.TerrainControl &&
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
                Format == RuntimeTextureFormat.Bc7Unorm ? checked(((mip.Width + 3) / 4) * 16) : checked(
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
                    (Format == RuntimeTextureFormat.Bc7Unorm ? (mip.Height + 3) / 4 : mip.Height));

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
