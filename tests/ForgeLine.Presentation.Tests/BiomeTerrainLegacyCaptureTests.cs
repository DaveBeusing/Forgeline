using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class BiomeTerrainLegacyCaptureTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BIOME_TERRAIN_CAPTURE"));

    [Fact(Skip = "Set FORGELINE_BIOME_TERRAIN_CAPTURE to capture paired authored terrain references.", SkipUnless = nameof(Enabled))]
    public void AuthoredControlBytesAndWarpPixelsMatchPinnedReferences()
    {
        string root = Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BIOME_TERRAIN_CAPTURE")!);
        string phase = Environment.GetEnvironmentVariable("FORGELINE_BIOME_TERRAIN_PHASE") ?? "changed";
        if (phase is not ("baseline" or "changed")) throw new InvalidOperationException("Capture phase must be baseline or changed.");
        Directory.CreateDirectory(root);
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Terrain Material References", 640, 360));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.Resize(640, 360);
        var results = new List<CaptureRow>();
        foreach (string mapKey in new[] { "central-divide", "split-basin" })
        {
            var definition = mapKey == "central-divide" ? CentralDivideBattlefield.Create() : SplitBasinBattlefield.Create();
            var recipe = mapKey == "central-divide" ? BattlefieldTerrainRecipe.CentralDivide() : SplitBasinBattlefield.CreateTerrainRecipe();
            var terrain = BattlefieldTerrainFactory.Create(definition, recipe);
            var profile = TerrainPresentationProfile.CreateCentralDivide();
            var writer = new WorldCanonicalWriter();
            foreach (var chunk in terrain.Chunks)
            {
                writer.WriteInt32(chunk.Coordinate.X); writer.WriteInt32(chunk.Coordinate.Z);
                var splat = TerrainSplatMapBuilder.Build(chunk, profile);
                foreach (var slot in splat.Palette) writer.WriteUInt32((uint)slot);
                foreach (var mip in splat.Mips)
                {
                    writer.WriteInt32(mip.Width); writer.WriteInt32(mip.Height); writer.WriteBytes(mip.Pixels);
                }
            }
            string controls = Convert.ToHexStringLower(SHA256.HashData(writer.ToArray()));
            using var renderer = new TerrainRenderer(graphics, terrain, presentationProfile: profile) { DebugChunksEnabled = false };
            float x = definition.Metadata.WidthMeters * .5f, z = definition.Metadata.HeightMeters * .5f;
            Assert.True(terrain.TrySampleHeight(x, z, out float height));
            var camera = new RtsCamera(new() { InitialTarget = new(x, height, z), InitialDistance = 800, MaximumDistance = 2000 });
            foreach (var mode in new[] { TerrainDebugVisualizationMode.None, TerrainDebugVisualizationMode.MaterialPalette })
            {
                renderer.DebugVisualizationMode = mode;
                renderer.Prepare(camera, 640, 360);
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, camera));
                Assert.Equal(640 * 360 * 4, pixels.Length);
                Assert.Equal(pixels, graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, camera)));
                Assert.Equal(13, renderer.LastDiagnostics.TextureBindingsPerDraw);
                Assert.Equal(13, renderer.LastDiagnostics.MaximumTextureSamplesPerPixel);
                Assert.True(renderer.LastDiagnostics.DrawCalls > 0);
                string hash = Convert.ToHexStringLower(SHA256.HashData(pixels));
                string name = mapKey + "-" + mode;
                TerrainCaptureImage.WritePng(Path.Combine(root, phase + "-" + name + ".png"), 640, 360, pixels);
                results.Add(new(name, controls, hash));
            }
        }
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        string baselinePath = Path.Combine(root, "baseline.json");
        if (phase == "changed")
        {
            var baseline = JsonSerializer.Deserialize<CaptureRow[]>(File.ReadAllText(baselinePath))!;
            Assert.Equal(baseline, results);
        }
        File.WriteAllText(Path.Combine(root, phase + ".json"), JsonSerializer.Serialize(results, ReportOptions));
    }

    public sealed record CaptureRow(string Name, string ControlSha256, string PixelSha256);
}
