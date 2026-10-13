using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Reflection;
using System.Text.Json;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class VisualReferenceCaptureTests
{
    [Fact]
    public void RejectedAndFailedRequestsCannotOverwriteExistingEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string output = Path.Combine(root, "existing"); Directory.CreateDirectory(output);
            string marker = Path.Combine(output, "baseline.txt"); File.WriteAllText(marker, "retain");
            string identity = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            var request = new VisualReferenceCaptureRequest { OutputRoot = output, Commit = identity[^40..], RuntimeRoot = Path.Combine(root, "missing-runtime") };
            string path = Path.Combine(root, "request.json");
            File.WriteAllText(path, JsonSerializer.Serialize(request, VisualReferenceCapture.JsonOptions));
            Assert.Equal(1, VisualReferenceCapture.Run(path));
            Assert.Equal("retain", File.ReadAllText(marker));
            Assert.Single(Directory.GetFiles(output));
            output = Path.Combine(root, "failed");
            File.WriteAllText(path, JsonSerializer.Serialize(request with { OutputRoot = output }, VisualReferenceCapture.JsonOptions));
            Assert.Equal(1, VisualReferenceCapture.Run(path));
            using var failure = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "failure.json")));
            Assert.Equal("structural-failure", failure.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, failure.RootElement.GetProperty("completedCaptures").GetInt32());
            Assert.Empty(Directory.GetFiles(output, "*.png"));
            output = Path.Combine(root, "wrong-build");
            File.WriteAllText(path, JsonSerializer.Serialize(request with { OutputRoot = output, Commit = new('0',40) }, VisualReferenceCapture.JsonOptions));
            Assert.Equal(1, VisualReferenceCapture.Run(path));
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void MatrixRejectsInvalidAndDuplicateStatesBeforeNativeStartup()
    {
        var request = new VisualReferenceCaptureRequest { OutputRoot = "unused", RuntimeRoot = "unused", Commit = new('a', 40) };
        request.Validate();
        Assert.Equal(45, request.YawDegrees);
        Assert.Equal(0, request.InspectionDistanceMeters);
        foreach (float distance in new[] { 0f, 20f, 120f }) (request with { InspectionDistanceMeters = distance }).Validate();
        foreach (float distance in new[] { -1f, 19f, 121f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => (request with { InspectionDistanceMeters = distance }).Validate());
        foreach (float yaw in new[] { 0f, 45f, 225f, 360f }) (request with { YawDegrees = yaw }).Validate();
        foreach (float yaw in new[] { -1f, 361f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => (request with { YawDegrees = yaw }).Validate());
        foreach (uint dpi in new[] { 0u, 96u, 120u, 144u, 192u }) (request with { RasterDpi = dpi }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (request with { RasterDpi = 193 }).Validate());
        Assert.Equal(6, request.Scenes.Length);
        (request with { Scenes = [VisualReferenceSceneKind.MechanicalMoving] }).Validate();
        Assert.Throws<ArgumentException>(() => (request with { Displays = [new(640, 360)] }).Validate());
        (request with { Software = true, Displays = [new(640, 360)] }).Validate();
        Assert.Throws<ArgumentException>(() => (request with { Commit = "stale" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Displays = [new(3840, 2130)] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Scenes = [VisualReferenceSceneKind.BattlefieldCombat, VisualReferenceSceneKind.BattlefieldCombat] }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (request with { Samples = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (request with { RepeatCaptures = 1 }).Validate());
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
