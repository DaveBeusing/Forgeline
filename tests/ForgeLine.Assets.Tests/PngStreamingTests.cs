using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class PngStreamingTests
{
    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(4, -1)]
    [InlineData(4, 1)]
    public void StreamsAllFiltersAndRejectsIncorrectInflatedLength(int channels, int lengthChange)
    {
        const int width = 8;
        const int height = 5;
        byte[] pixels = new byte[width * height * channels];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i * 53 + i / 7) % 256);
        int rowBytes = width * channels;
        byte[] filtered = new byte[(rowBytes + 1) * height];
        for (int y = 0; y < height; y++)
        {
            filtered[y * (rowBytes + 1)] = (byte)y;
            for (int x = 0; x < rowBytes; x++)
            {
                int left = x >= channels ? pixels[y * rowBytes + x - channels] : 0;
                int up = y > 0 ? pixels[(y - 1) * rowBytes + x] : 0;
                int upperLeft = y > 0 && x >= channels ? pixels[(y - 1) * rowBytes + x - channels] : 0;
                int prediction = y switch
                {
                    1 => left, 2 => up, 3 => (left + up) / 2,
                    4 => Paeth(left, up, upperLeft), _ => 0
                };
                filtered[y * (rowBytes + 1) + x + 1] = unchecked((byte)(pixels[y * rowBytes + x] - prediction));
            }
        }
        Array.Resize(ref filtered, filtered.Length + lengthChange);
        string root = Path.Combine(Path.GetTempPath(), "forgeline-png-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source");
            string runtime = Path.Combine(root, "runtime");
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "image.png"), Png(width, height, channels, filtered));
            File.WriteAllText(Path.Combine(source, "image.asset.json"), JsonSerializer.Serialize(new
            {
                id = "texture.test.scanlines", type = "texture", source = "image.png",
                textureGenerateMipmaps = false
            }));
            var progress = new List<AssetCompilationProgress>();
            AssetCompilationResult result = AssetPipelineCompiler.Compile(source, runtime, progress: progress.Add);
            Assert.Equal(lengthChange == 0, result.Success);
            if (lengthChange != 0)
            {
                Assert.Contains(result.Diagnostics, d => d.Code == "ASSET100" && d.AssetId == "texture.test.scanlines");
                Assert.Contains(progress, p => p.Stage == "texture-decode" && p.State == "failed" && p.SourcePath!.EndsWith("image.png", StringComparison.Ordinal));
                Assert.False(File.Exists(Path.Combine(runtime, RuntimeAssetCatalog.ManifestFileName)));
                return;
            }
            RuntimeTextureData texture = RuntimeTextureData.FromPayload(
                RuntimeAssetCatalog.Load(runtime).Read(AssetId.Parse("texture.test.scanlines")).Payload);
            byte[] expected = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                pixels.AsSpan(i * channels, 3).CopyTo(expected.AsSpan(i * 4, 3));
                expected[i * 4 + 3] = channels == 4 ? pixels[i * 4 + 3] : (byte)255;
            }
            Assert.Equal(expected, texture.Mips[0].Pixels);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] Png(int width, int height, int channels, byte[] filtered)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = channels == 4 ? (byte)6 : (byte)2;
        Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(filtered);
        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number); output.Write(type); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type) crc = UpdateCrc(crc, value);
        foreach (byte value in data) crc = UpdateCrc(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }
}
