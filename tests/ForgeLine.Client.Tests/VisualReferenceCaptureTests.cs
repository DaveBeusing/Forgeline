using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class VisualReferenceCaptureTests
{
    [Fact]
    public void MatrixRejectsInvalidAndDuplicateStatesBeforeNativeStartup()
    {
        var request = new VisualReferenceCaptureRequest { OutputRoot = "unused", RuntimeRoot = "unused", Commit = new('a', 40) };
        request.Validate();
        Assert.Throws<ArgumentException>(() => (request with { Commit = "stale" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Displays = [new(3840, 2130)] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Scenes = [VisualReferenceSceneKind.BattlefieldCombat, VisualReferenceSceneKind.BattlefieldCombat] }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (request with { Samples = 0 }).Validate());
        Assert.False(Program.TryParseArguments(["--reference-capture-request"], out _, out _, out _, out _));
    }

    [Fact]
    public void PngContainsUnscaledRgbaRowsAndRefusesOverwrite()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "frame.png");
        try
        {
            byte[] rgba = [255,0,0,255,0,255,0,255,0,0,255,255,64,128,192,255];
            ReferenceCaptureImage.WritePng(path, 2, 2, rgba);
            byte[] png = File.ReadAllBytes(path);
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
            int offset = 8; byte[]? compressed = null;
            while (offset < png.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
                if (Encoding.ASCII.GetString(png.AsSpan(offset + 4, 4)) == "IDAT") compressed = png.AsSpan(offset + 8, length).ToArray();
                offset += 12 + length;
            }
            using var input = new ZLibStream(new MemoryStream(compressed!), CompressionMode.Decompress);
            using var rows = new MemoryStream(); input.CopyTo(rows);
            Assert.Equal(new byte[] {0,255,0,0,255,0,255,0,255,0,0,0,255,255,64,128,192,255}, rows.ToArray());
            Assert.Throws<IOException>(() => ReferenceCaptureImage.WritePng(path, 2, 2, rgba));
            Assert.Equal(png, File.ReadAllBytes(path));
            Assert.Throws<ArgumentException>(() => ReferenceCaptureImage.WritePng(Path.Combine(root, "invalid.png"), 3, 2, rgba));
            Assert.False(File.Exists(Path.Combine(root, "invalid.png")));
        }
        finally { Directory.Delete(root, true); }
    }
}
