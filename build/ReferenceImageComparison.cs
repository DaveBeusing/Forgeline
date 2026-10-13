using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

// Deliberately accepts only the native capture encoder's unfiltered RGBA8 PNGs.
public static class ReferenceImageComparison
{
    public sealed class Result
    {
        public int MaximumChannelDelta { get; set; }
        public double MeanAbsoluteChannelDelta { get; set; }
        public int ChangedPixels { get; set; }
        public double ChangedPixelFraction { get; set; }
        public bool WithinTolerance { get; set; }
    }

    public static Result Compare(string current, string baseline, int width, int height, int channelTolerance, double maximumChangedFraction)
    {
        if (channelTolerance < 0 || channelTolerance > 8 || !double.IsFinite(maximumChangedFraction) || maximumChangedFraction < 0 || maximumChangedFraction > .05)
            throw new ArgumentOutOfRangeException(nameof(channelTolerance));
        byte[] left = Read(current, width, height), right = Read(baseline, width, height);
        var result = new Result();
        long absolute = 0;
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            bool changed = false;
            for (int channel = 0; channel < 4; channel++)
            {
                int index = pixel * 4 + channel;
                int delta = Math.Abs(left[index] - right[index]);
                absolute += delta;
                result.MaximumChannelDelta = Math.Max(result.MaximumChannelDelta, delta);
                changed |= delta > channelTolerance;
            }
            if (changed) result.ChangedPixels++;
        }
        result.MeanAbsoluteChannelDelta = absolute / (double)left.Length;
        result.ChangedPixelFraction = result.ChangedPixels / (double)(width * height);
        result.WithinTolerance = result.ChangedPixelFraction <= maximumChangedFraction;
        return result;
    }

    private static byte[] Read(string path, int width, int height)
    {
        int size = checked(width * height * 4);
        if (width <= 0 || height <= 0 || size > 134217728) throw new InvalidDataException("Image exceeds native comparison bounds.");
        using var input = File.OpenRead(path);
        using var compressed = new MemoryStream();
        byte[] signature = new byte[8]; input.ReadExactly(signature);
        if (Convert.ToHexString(signature) != "89504E470D0A1A0A") throw new InvalidDataException("Invalid native PNG.");
        bool header = false, ended = false;
        byte[] word = new byte[4];
        while (input.Position < input.Length)
        {
            input.ReadExactly(word);
            int length = BinaryPrimitives.ReadInt32BigEndian(word);
            if (length < 0 || length > 134217728 || length > input.Length - input.Position - 8) throw new InvalidDataException("Invalid PNG chunk length.");
            input.ReadExactly(word); string type = Encoding.ASCII.GetString(word);
            byte[] data = new byte[length]; input.ReadExactly(data); input.ReadExactly(word);
            if (type == "IHDR")
            {
                if (header || length != 13 || BinaryPrimitives.ReadInt32BigEndian(data) != width ||
                    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)) != height || data[8] != 8 || data[9] != 6 ||
                    data[10] != 0 || data[11] != 0 || data[12] != 0) throw new InvalidDataException("Expected native RGBA8 dimensions/encoding.");
                header = true;
            }
            else if (type == "IDAT")
            {
                if (!header || compressed.Length + length > 134217728) throw new InvalidDataException("Invalid native IDAT.");
                compressed.Write(data);
            }
            else if (type == "IEND") { ended = length == 0 && input.Position == input.Length; break; }
            else throw new InvalidDataException("Unsupported native capture chunk.");
        }
        if (!header || !ended || compressed.Length == 0) throw new InvalidDataException("Incomplete native PNG.");
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] pixels = new byte[size];
        for (int row = 0; row < height; row++)
        {
            if (zlib.ReadByte() != 0) throw new InvalidDataException("Expected native unfiltered rows.");
            zlib.ReadExactly(pixels.AsSpan(row * width * 4, width * 4));
        }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("Unexpected decompressed PNG payload.");
        return pixels;
    }
}
