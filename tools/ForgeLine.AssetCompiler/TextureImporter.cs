using System.Buffers.Binary;
using System.IO.Compression;
using ForgeLine.Assets;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace ForgeLine.AssetCompiler;

internal static class TextureImporter
{
    private const int MaxDimension = 16384;

    public static ImportedAssetPayload Import(
        string path,
        RuntimeTextureColorSpace colorSpace,
        RuntimeTextureUsage usage,
        bool generateMipmaps,
        int? maxMipLevels,
        int? maxDimension = null,
        RuntimeTextureFormat format = RuntimeTextureFormat.Rgba8Unorm,
        TextureCompressionQuality compressionQuality = TextureCompressionQuality.Best)
    {
        var extension = Path.GetExtension(path);
        TextureData texture = extension.ToLowerInvariant() switch
        {
            ".png" => ImportPng(path),
            ".tga" => ImportTga(path),
            _ => throw new InvalidDataException($"Texture format '{extension}' is not supported."),
        };

        if (maxDimension is int limit)
        {
            if (limit <= 0 || limit > MaxDimension)
            {
                throw new InvalidDataException("textureMaxDimension must be between 1 and 16384.");
            }

            while (texture.Width > limit || texture.Height > limit)
            {
                texture = Downsample(texture, colorSpace, usage);
            }
        }

        RuntimeTextureMipLevel[] mips =
            GenerateMipChain(
                texture,
                colorSpace,
                usage,
                generateMipmaps,
                maxMipLevels);
        if (format == RuntimeTextureFormat.Bc7Unorm)
        {
            if (texture.Width % 4 != 0 || texture.Height % 4 != 0)
            {
                throw new InvalidDataException($"BC7 runtime top-level dimensions must be multiples of 4; actual {texture.Width}x{texture.Height}. Use RGBA8 or author aligned dimensions.");
            }
            if (usage is RuntimeTextureUsage.TerrainControl or RuntimeTextureUsage.GenericData)
            {
                throw new InvalidDataException($"Texture usage {usage} requires lossless RGBA8 runtime storage.");
            }

            var encoder = new BcEncoder(CompressionFormat.Bc7);
            encoder.OutputOptions.GenerateMipMaps = false;
            encoder.OutputOptions.Quality = compressionQuality == TextureCompressionQuality.Balanced
                ? CompressionQuality.Balanced : CompressionQuality.BestQuality;
            encoder.Options.IsParallel = false;
            mips = mips.Select(mip => new RuntimeTextureMipLevel(
                mip.Width, mip.Height, checked(((mip.Width + 3) / 4) * 16),
                encoder.EncodeToRawBytes(mip.Pixels, mip.Width, mip.Height, PixelFormat.Rgba32)[0])).ToArray();
        }
        else if (format != RuntimeTextureFormat.Rgba8Unorm)
        {
            throw new InvalidDataException($"Texture runtime format {format} is unsupported.");
        }
        var runtimeTexture =
            new RuntimeTextureData(
                texture.Width,
                texture.Height,
                format,
                colorSpace,
                usage,
                mips);

        return new ImportedAssetPayload(
            runtimeTexture.ToPayload(),
            null,
            []);
    }

    private static TextureData ImportPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < signature.Length || !bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
        {
            throw new InvalidDataException("PNG signature is invalid.");
        }

        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        var sawHeader = false;
        using var compressed = new MemoryStream();

        var offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || offset + 12L + length > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk length is invalid.");
            }

            var type = bytes.AsSpan(offset + 4, 4);
            var data = bytes.AsSpan(offset + 8, length);

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    throw new InvalidDataException("PNG IHDR chunk has an invalid size.");
                }

                width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                bitDepth = data[8];
                colorType = data[9];
                var compression = data[10];
                var filtering = data[11];
                interlace = data[12];

                if (compression != 0 || filtering != 0)
                {
                    throw new InvalidDataException("PNG uses an unsupported compression or filtering method.");
                }

                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += 12 + length;
        }

        ValidateDimensions(width, height);
        if (!sawHeader || bitDepth != 8 || (colorType != 2 && colorType != 6) || interlace != 0)
        {
            throw new InvalidDataException(
                "PNG import supports non-interlaced 8-bit RGB and RGBA images only.");
        }

        var sourceBytesPerPixel = colorType == 6 ? 4 : 3;
        var rowBytes = checked(width * sourceBytesPerPixel);
        var expectedLength = checked((rowBytes + 1) * height);
        compressed.Position = 0;

        byte[] filtered;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        using (var decoded = new MemoryStream(expectedLength))
        {
            zlib.CopyTo(decoded);
            filtered = decoded.ToArray();
        }

        if (filtered.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"PNG decompressed to {filtered.Length} bytes; expected {expectedLength} bytes.");
        }

        var scanlines = new byte[checked(rowBytes * height)];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (rowBytes + 1)];
            var sourceRow = filtered.AsSpan(y * (rowBytes + 1) + 1, rowBytes);
            var targetRow = scanlines.AsSpan(y * rowBytes, rowBytes);
            var previousRow = y == 0
                ? ReadOnlySpan<byte>.Empty
                : scanlines.AsSpan((y - 1) * rowBytes, rowBytes);

            UnfilterRow(filter, sourceRow, targetRow, previousRow, sourceBytesPerPixel);
        }

        var rgba = new byte[checked(width * height * 4)];
        var sourceOffset = 0;
        var targetOffset = 0;
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            rgba[targetOffset++] = scanlines[sourceOffset++];
            rgba[targetOffset++] = scanlines[sourceOffset++];
            rgba[targetOffset++] = scanlines[sourceOffset++];
            rgba[targetOffset++] = sourceBytesPerPixel == 4 ? scanlines[sourceOffset++] : (byte)255;
        }

        return new TextureData(width, height, rgba);
    }

    private static TextureData ImportTga(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 18)
        {
            throw new InvalidDataException("TGA header is incomplete.");
        }

        var idLength = bytes[0];
        var colorMapType = bytes[1];
        var imageType = bytes[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
        var pixelDepth = bytes[16];
        var descriptor = bytes[17];

        ValidateDimensions(width, height);
        if (colorMapType != 0 || imageType != 2 || (pixelDepth != 24 && pixelDepth != 32))
        {
            throw new InvalidDataException("TGA import supports uncompressed 24-bit and 32-bit true-color images only.");
        }

        var sourceBytesPerPixel = pixelDepth / 8;
        var pixelDataOffset = 18 + idLength;
        var requiredBytes = checked(width * height * sourceBytesPerPixel);
        if (pixelDataOffset + requiredBytes > bytes.Length)
        {
            throw new InvalidDataException("TGA pixel data is truncated.");
        }

        var topOrigin = (descriptor & 0x20) != 0;
        var rgba = new byte[checked(width * height * 4)];

        for (var y = 0; y < height; y++)
        {
            var sourceY = topOrigin ? y : height - 1 - y;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = pixelDataOffset + ((sourceY * width + x) * sourceBytesPerPixel);
                var targetIndex = (y * width + x) * 4;
                rgba[targetIndex] = bytes[sourceIndex + 2];
                rgba[targetIndex + 1] = bytes[sourceIndex + 1];
                rgba[targetIndex + 2] = bytes[sourceIndex];
                rgba[targetIndex + 3] = sourceBytesPerPixel == 4 ? bytes[sourceIndex + 3] : (byte)255;
            }
        }

        return new TextureData(width, height, rgba);
    }

    private static RuntimeTextureMipLevel[] GenerateMipChain(
        TextureData texture,
        RuntimeTextureColorSpace colorSpace,
        RuntimeTextureUsage usage,
        bool generateMipmaps,
        int? maxMipLevels)
    {
        if (maxMipLevels is <= 0)
        {
            throw new InvalidDataException(
                "Texture mip level limit must be positive.");
        }

        int levelLimit =
            generateMipmaps
                ? maxMipLevels ?? 32
                : 1;
        var levels =
            new List<RuntimeTextureMipLevel>(
                Math.Min(
                    levelLimit,
                    16));
        TextureData current =
            texture;

        while (true)
        {
            levels.Add(
                new RuntimeTextureMipLevel(
                    current.Width,
                    current.Height,
                    checked(current.Width * 4),
                    current.Pixels));

            if (!generateMipmaps ||
                levels.Count >= levelLimit ||
                current.Width == 1 &&
                current.Height == 1)
            {
                break;
            }

            current =
                Downsample(
                    current,
                    colorSpace,
                    usage);
        }

        return levels.ToArray();
    }

    private static TextureData Downsample(
        TextureData source,
        RuntimeTextureColorSpace colorSpace,
        RuntimeTextureUsage usage)
    {
        int width =
            Math.Max(
                1,
                source.Width / 2);
        int height =
            Math.Max(
                1,
                source.Height / 2);
        var pixels =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        for (int y = 0; y < height; y++)
        {
            int sourceTop =
                y *
                source.Height /
                height;
            int sourceBottom =
                Math.Max(
                    sourceTop + 1,
                    (y + 1) *
                    source.Height /
                    height);

            for (int x = 0; x < width; x++)
            {
                int sourceLeft =
                    x *
                    source.Width /
                    width;
                int sourceRight =
                    Math.Max(
                        sourceLeft + 1,
                        (x + 1) *
                        source.Width /
                        width);
                int destinationOffset =
                    checked(
                        (y *
                         width +
                         x) *
                        4);

                if (usage == RuntimeTextureUsage.Normal)
                {
                    WriteNormalPixel(
                        source,
                        sourceLeft,
                        sourceTop,
                        sourceRight,
                        sourceBottom,
                        pixels,
                        destinationOffset);
                }
                else if (usage ==
                         RuntimeTextureUsage.TerrainControl)
                {
                    WriteTerrainControlPixel(
                        source,
                        sourceLeft,
                        sourceTop,
                        sourceRight,
                        sourceBottom,
                        pixels,
                        destinationOffset);
                }
                else if (colorSpace ==
                         RuntimeTextureColorSpace.Srgb)
                {
                    WriteSrgbPixel(
                        source,
                        sourceLeft,
                        sourceTop,
                        sourceRight,
                        sourceBottom,
                        pixels,
                        destinationOffset);
                }
                else
                {
                    WriteLinearPixel(
                        source,
                        sourceLeft,
                        sourceTop,
                        sourceRight,
                        sourceBottom,
                        pixels,
                        destinationOffset);
                }
            }
        }

        return new TextureData(
            width,
            height,
            pixels);
    }

    private static void WriteLinearPixel(
        TextureData source,
        int left,
        int top,
        int right,
        int bottom,
        byte[] destination,
        int destinationOffset)
    {
        Span<long> sums =
            stackalloc long[4];
        int count =
            0;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset =
                    checked(
                        (y *
                         source.Width +
                         x) *
                        4);
                sums[0] += source.Pixels[offset];
                sums[1] += source.Pixels[offset + 1];
                sums[2] += source.Pixels[offset + 2];
                sums[3] += source.Pixels[offset + 3];
                count++;
            }
        }

        for (int channel = 0; channel < 4; channel++)
        {
            destination[destinationOffset + channel] =
                AverageByte(
                    sums[channel],
                    count);
        }
    }

    private static void WriteSrgbPixel(
        TextureData source,
        int left,
        int top,
        int right,
        int bottom,
        byte[] destination,
        int destinationOffset)
    {
        Span<double> linearSums =
            stackalloc double[3];
        long alphaSum =
            0;
        int count =
            0;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset =
                    checked(
                        (y *
                         source.Width +
                         x) *
                        4);
                linearSums[0] +=
                    SrgbToLinear(
                        source.Pixels[offset]);
                linearSums[1] +=
                    SrgbToLinear(
                        source.Pixels[offset + 1]);
                linearSums[2] +=
                    SrgbToLinear(
                        source.Pixels[offset + 2]);
                alphaSum +=
                    source.Pixels[offset + 3];
                count++;
            }
        }

        destination[destinationOffset] =
            LinearToSrgbByte(
                linearSums[0] /
                count);
        destination[destinationOffset + 1] =
            LinearToSrgbByte(
                linearSums[1] /
                count);
        destination[destinationOffset + 2] =
            LinearToSrgbByte(
                linearSums[2] /
                count);
        destination[destinationOffset + 3] =
            AverageByte(
                alphaSum,
                count);
    }

    private static void WriteNormalPixel(
        TextureData source,
        int left,
        int top,
        int right,
        int bottom,
        byte[] destination,
        int destinationOffset)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        long alphaSum =
            0;
        int count =
            0;

        for (int sourceY = top; sourceY < bottom; sourceY++)
        {
            for (int sourceX = left; sourceX < right; sourceX++)
            {
                int offset =
                    checked(
                        (sourceY *
                         source.Width +
                         sourceX) *
                        4);
                x +=
                    DecodeNormalChannel(
                        source.Pixels[offset]);
                y +=
                    DecodeNormalChannel(
                        source.Pixels[offset + 1]);
                z +=
                    DecodeNormalChannel(
                        source.Pixels[offset + 2]);
                alphaSum +=
                    source.Pixels[offset + 3];
                count++;
            }
        }

        double length =
            Math.Sqrt(
                x * x +
                y * y +
                z * z);

        if (length <= 1e-12)
        {
            x = 0.0;
            y = 0.0;
            z = 1.0;
        }
        else
        {
            x /= length;
            y /= length;
            z /= length;
        }

        destination[destinationOffset] =
            EncodeNormalChannel(
                x);
        destination[destinationOffset + 1] =
            EncodeNormalChannel(
                y);
        destination[destinationOffset + 2] =
            EncodeNormalChannel(
                z);
        destination[destinationOffset + 3] =
            AverageByte(
                alphaSum,
                count);
    }

    private static void WriteTerrainControlPixel(
        TextureData source,
        int left,
        int top,
        int right,
        int bottom,
        byte[] destination,
        int destinationOffset)
    {
        Span<long> sums =
            stackalloc long[4];

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset =
                    checked(
                        (y *
                         source.Width +
                         x) *
                        4);
                sums[0] += source.Pixels[offset];
                sums[1] += source.Pixels[offset + 1];
                sums[2] += source.Pixels[offset + 2];
                sums[3] += source.Pixels[offset + 3];
            }
        }

        long total =
            sums[0] +
            sums[1] +
            sums[2] +
            sums[3];

        if (total == 0)
        {
            destination[destinationOffset] =
                255;
            destination[destinationOffset + 1] =
                0;
            destination[destinationOffset + 2] =
                0;
            destination[destinationOffset + 3] =
                0;
            return;
        }

        Span<int> values =
            stackalloc int[4];
        Span<double> remainders =
            stackalloc double[4];
        int assigned =
            0;

        for (int channel = 0; channel < 4; channel++)
        {
            double scaled =
                sums[channel] *
                255.0 /
                total;
            int whole =
                (int)Math.Floor(
                    scaled);
            values[channel] =
                whole;
            remainders[channel] =
                scaled -
                whole;
            assigned +=
                whole;
        }

        int remaining =
            255 -
            assigned;

        while (remaining > 0)
        {
            int selected =
                0;

            for (int channel = 1; channel < 4; channel++)
            {
                if (remainders[channel] >
                    remainders[selected])
                {
                    selected =
                        channel;
                }
            }

            values[selected]++;
            remainders[selected] =
                -1.0;
            remaining--;
        }

        for (int channel = 0; channel < 4; channel++)
        {
            destination[destinationOffset + channel] =
                checked(
                    (byte)values[channel]);
        }
    }

    private static byte AverageByte(
        long sum,
        int count) =>
        checked(
            (byte)(
                (sum +
                 count / 2) /
                count));

    private static double SrgbToLinear(
        byte value)
    {
        double normalized =
            value /
            255.0;

        return normalized <= 0.04045
            ? normalized /
              12.92
            : Math.Pow(
                (normalized + 0.055) /
                1.055,
                2.4);
    }

    private static byte LinearToSrgbByte(
        double value)
    {
        double clamped =
            Math.Clamp(
                value,
                0.0,
                1.0);
        double srgb =
            clamped <= 0.0031308
                ? clamped *
                  12.92
                : 1.055 *
                  Math.Pow(
                      clamped,
                      1.0 /
                      2.4) -
                  0.055;

        return UnitToByte(
            srgb);
    }

    private static double DecodeNormalChannel(
        byte value) =>
        value /
        127.5 -
        1.0;

    private static byte EncodeNormalChannel(
        double value) =>
        UnitToByte(
            value *
            0.5 +
            0.5);

    private static byte UnitToByte(
        double value) =>
        checked(
            (byte)Math.Clamp(
                (int)Math.Round(
                    value *
                    255.0,
                    MidpointRounding.AwayFromZero),
                0,
                255));

    private static void UnfilterRow(
        byte filter,
        ReadOnlySpan<byte> source,
        Span<byte> target,
        ReadOnlySpan<byte> previous,
        int bytesPerPixel)
    {
        for (var x = 0; x < source.Length; x++)
        {
            var left = x >= bytesPerPixel ? target[x - bytesPerPixel] : (byte)0;
            var up = previous.IsEmpty ? (byte)0 : previous[x];
            var upLeft = previous.IsEmpty || x < bytesPerPixel ? (byte)0 : previous[x - bytesPerPixel];

            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"PNG filter type {filter} is not supported."),
            };

            target[x] = unchecked((byte)(source[x] + predictor));
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var prediction = left + up - upLeft;
        var leftDistance = Math.Abs(prediction - left);
        var upDistance = Math.Abs(prediction - up);
        var upLeftDistance = Math.Abs(prediction - upLeft);

        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
        {
            return left;
        }

        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new InvalidDataException(
                $"Texture dimensions {width}x{height} are invalid; maximum supported dimension is {MaxDimension}.");
        }
    }

    private sealed record TextureData(int Width, int Height, byte[] Pixels);
}
