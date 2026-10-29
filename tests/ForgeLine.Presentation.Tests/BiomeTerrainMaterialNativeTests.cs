using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class BiomeTerrainMaterialNativeTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    [Fact]
    public void ExplicitBiomeProfileKeepsThirteenBindingsAndNoPerFrameFieldWork()
    {
        var world = BiomeTerrainMaterialTests.Flat(new(-1, 0), new(0, 0));
        var field = new BiomeTerrainMaterialTests.Field((x, z) =>
        {
            int t = (int)Math.Clamp((x + 256000) * ushort.MaxValue / 512000, 0, ushort.MaxValue);
            return BiomeTerrainMaterialTests.Sample(new(0, (ushort)(ushort.MaxValue - t), 0, 0, (ushort)t));
        });
        var mask = new TerrainInfrastructureMask(
        [
            TerrainInfrastructureMask.Road(new(3, 24), new(3, 232), 1),
            new(TerrainMaterialSlot.Concrete, new(128, 128), new(16, 16), 4, Strength: 1)
        ]);
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Biome Terrain", 640, 360));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.Resize(640, 360);
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        using var renderer = new TerrainRenderer(TerrainVisualProfileResolver.CreateBiomeDefinition(), graphics, world, field, mask)
            { DebugChunksEnabled = false };
        double setupMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long setupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        long preparedQueries = field.Calls;
        Assert.Equal(2 * 33 * 33 * 2, preparedQueries);
        var camera = new RtsCamera(new() { InitialTarget = new(0, 10, 128), InitialDistance = 420, MaximumDistance = 2000 });
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        byte[]? regular = null;
        foreach (var mode in new[] { TerrainDebugVisualizationMode.None, TerrainDebugVisualizationMode.MaterialPalette })
        {
            renderer.DebugVisualizationMode = mode;
            renderer.Prepare(camera, 640, 360);
            byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, camera));
            Assert.Equal(pixels, graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, camera)));
            Assert.Equal(640 * 360 * 4, pixels.Length);
            Assert.Equal(renderer.LastDiagnostics.VisibleChunks, renderer.LastDiagnostics.DrawCalls);
            Assert.InRange(renderer.LastDiagnostics.DrawCalls, 1, 2);
            Assert.Equal(13, renderer.LastDiagnostics.TextureBindingsPerDraw);
            Assert.Equal(13, renderer.LastDiagnostics.MaximumTextureSamplesPerPixel);
            Assert.Equal(preparedQueries, field.Calls);
            if (regular is null) regular = pixels;
            else Assert.NotEqual(regular, pixels);
            hashes.Add(mode.ToString(), Convert.ToHexStringLower(SHA256.HashData(pixels)));
            if (Environment.GetEnvironmentVariable("FORGELINE_BIOME_TERRAIN_CAPTURE") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                TerrainCaptureImage.WritePng(Path.Combine(output, "procedural-" + mode + ".png"), 640, 360, pixels);
            }
        }
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        if (Environment.GetEnvironmentVariable("FORGELINE_BIOME_TERRAIN_CAPTURE") is { Length: > 0 } reportRoot)
            File.WriteAllText(Path.Combine(reportRoot, "procedural.json"), JsonSerializer.Serialize(new
            {
                graphics.Diagnostics.Device, setupMilliseconds, setupAllocatedBytes, preparedQueries,
                terrain = renderer.LastDiagnostics, hashes,
                qualification = "WARP, deterministic fallback material colors, fixed camera; no production texture or physical graphics acceptance."
            }, ReportOptions));
    }
}
