using System.Buffers.Binary;
using System.IO.Compression;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class TextureImporter
{
    private const int MaxDimension = 16384;

    public static ImportedAssetPayload Import(string path)
    {
        var extension = Path.GetExtension(path);
        TextureData texture = extension.ToLowerInvariant() switch
        {
            ".png" => ImportPng(path),
            ".tga" => ImportTga(path),
            _ => throw new InvalidDataException($"Texture format '{extension}' is not supported."),
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(texture.Width);
        writer.Write(texture.Height);
        writer.Write(4);
        writer.Write(texture.Pixels);
        writer.Flush();

        return new ImportedAssetPayload(stream.ToArray(), null, []);
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
