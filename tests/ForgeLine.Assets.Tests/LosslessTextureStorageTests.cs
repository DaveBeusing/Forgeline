using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class LosslessTextureStorageTests
{
    [Fact]
    public void StorageRoundTripPreservesEverySemanticMipAndGpuByte()
    {
        var original = new RuntimeTextureData(64, 64, RuntimeTextureFormat.Rgba8Unorm,
            RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage,
            [new(64,64,256,Enumerable.Repeat((byte)17,16384).ToArray()),
             new(32,32,128,Enumerable.Repeat((byte)31,4096).ToArray())]);
        byte[] packed = original.ToPayload(compressLosslessly: true);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(packed));
        Assert.True(packed.Length < original.ToPayload().Length / 4);
        Assert.Equal(packed, original.ToPayload(compressLosslessly: true));
        var decoded = RuntimeTextureData.FromPayload(packed);
        Assert.Equal(original.ToPayload(), decoded.ToPayload());
        Assert.Equal(original.ResidentByteCount, decoded.ResidentByteCount);
    }

    [Theory]
    [InlineData(4, -1)]
    [InlineData(4, int.MaxValue)]
    [InlineData(8, -1)]
    [InlineData(8, int.MaxValue)]
    public void InvalidStorageLengthsRejectBeforeAllocating(int offset, int value)
    {
        var texture = RuntimeTextureData.FromRgba8(64, 64, new byte[16384],
            RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage);
        byte[] packed = texture.ToPayload(compressLosslessly: true);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(offset), value);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(packed));
    }

    [Fact]
    public void CorruptionTruncationNestedStorageAndWrongDecodedExtentReject()
    {
        var texture = RuntimeTextureData.FromRgba8(64, 64, new byte[16384],
            RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage);
        byte[] packed = texture.ToPayload(compressLosslessly: true);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(packed.AsSpan(0, packed.Length - 1)));
        byte[] corrupt = (byte[])packed.Clone(); corrupt[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(corrupt));
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(Wrap(packed)));
        byte[] shortExtent = (byte[])packed.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(shortExtent.AsSpan(4), texture.ToPayload().Length - 1);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(shortExtent));
        byte[] longExtent = (byte[])packed.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(longExtent.AsSpan(4), texture.ToPayload().Length + 1);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(longExtent));
        byte[] invalidSemantic = texture.ToPayload();
        BinaryPrimitives.WriteInt32LittleEndian(invalidSemantic.AsSpan(40), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(Wrap(invalidSemantic)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(1000)]
    public void ValidStreamFollowedByPaddingDoesNotBypassSectionValidation(int padding)
    {
        var texture = RuntimeTextureData.FromRgba8(64, 64, new byte[16384],
            RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage);
        byte[] packed = texture.ToPayload(compressLosslessly: true);
        byte[] padded = new byte[packed.Length + padding]; packed.CopyTo(padded, 0);
        BinaryPrimitives.WriteInt32LittleEndian(padded.AsSpan(8), padded.Length - 12);
        Assert.Throws<InvalidDataException>(() => RuntimeTextureData.FromPayload(padded));
    }

    [Fact]
    public void ProductionUiPagesKeepAllPixelsAndResidencyWhileStoredBelowTwoMebibytes()
    {
        string repo = Repository();
        string root = Path.Combine(Path.GetTempPath(), "forgeline-lossless-ui-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), output = Path.Combine(root, "runtime");
        Directory.CreateDirectory(source);
        string[] pages = ["core_1", "icons_1", "icons_2"];
        var ids = new List<string>();
        try
        {
            foreach (string page in pages)
            {
                string folder = Path.Combine(repo, "assets/source/ui/directorate/atlas");
                var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, page + ".asset.json")))!;
                string id = definition["id"]!.GetValue<string>(); ids.Add(id);
                File.Copy(Path.Combine(folder, definition["source"]!.GetValue<string>()), Path.Combine(source, page + ".png"));
                definition["source"] = page + ".png";
                File.WriteAllText(Path.Combine(source, page + ".asset.json"), definition.ToJsonString());
                definition["id"] = id + ".reference";
                definition["textureUsage"] = "genericData";
                File.WriteAllText(Path.Combine(source, page + ".reference.asset.json"), definition.ToJsonString());
            }
            var built = AssetPipelineCompiler.Compile(source, output, clean: true);
            Assert.True(built.Success, string.Join(Environment.NewLine, built.Diagnostics));
            var catalog = RuntimeAssetCatalog.Load(output);
            long storedBytes = 0, residentBytes = 0;
            foreach (string id in ids)
            {
                var record = catalog.Get(AssetId.Parse(id)); storedBytes += record.RuntimeBytes;
                var actual = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(id)).Payload);
                var reference = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(id + ".reference")).Payload);
                Assert.Equal(RuntimeTextureFormat.Rgba8Unorm, actual.Format);
                Assert.Equal(RuntimeTextureColorSpace.Linear, actual.ColorSpace);
                Assert.Equal(RuntimeTextureUsage.DisplayImage, actual.Usage);
                Assert.Equal(reference.Width, actual.Width); Assert.Equal(reference.Height, actual.Height);
                Assert.Single(actual.Mips); Assert.Equal(reference.Mips[0].Pixels, actual.Mips[0].Pixels);
                Assert.Equal(reference.ResidentByteCount, actual.ResidentByteCount);
                residentBytes += actual.ResidentByteCount;
            }
            Assert.Equal(12 * 1024 * 1024, residentBytes);
            Assert.True(storedBytes < 2 * 1024 * 1024, storedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] Wrap(byte[] raw)
    {
        using var encoded = new MemoryStream();
        using (var zlib = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);
        using var result = new MemoryStream(); using var writer = new BinaryWriter(result);
        writer.Write(4); writer.Write(raw.Length); writer.Write((int)encoded.Length); writer.Write(encoded.ToArray());
        return result.ToArray();
    }
    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ForgeLine.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException();
    }
}
