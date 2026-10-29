using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ForgeLine.Presentation.Tests;

internal static class TerrainCaptureImage
{
    internal static void WritePng(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Capture dimensions do not match RGBA output.");
        using var output = new FileStream(path, FileMode.Create);
        output.Write(new byte[] {137,80,78,71,13,10,26,10});
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (int row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba.Slice(row * width * 4, width * 4));
            }
        Chunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length); output.Write(word);
        byte[] name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in name) Update(value);
        foreach (byte value in data) Update(value);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
        void Update(byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
    }
}
