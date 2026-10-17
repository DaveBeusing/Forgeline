using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class ProductionTextureBudgetTests
{
    private static readonly string[] Terrain = ["crushed_gravel", "concrete_base", "cracked_earth", "rocky_scrub", "dark_ash", "dry_dirt", "bedrock"];
    private static string Repository()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "ForgeLine.sln"))) return d.FullName;
        throw new DirectoryNotFoundException();
    }
    private static string[] Definitions(string repo) => [.. Terrain.Select(n => Path.Combine(repo, "assets/source/world/textures/terrain", n + ".asset.json")),
        .. Enumerable.Range(1, 4).Select(i => Path.Combine(repo, "assets/source/ui/directorate/atlas", $"portraits_{i}.asset.json"))];
    [Fact]
    public void CompressionScopePreservesLosslessUiAndFullResolutionTerrainCaps()
    {
        string repo = Repository();
        foreach (string file in Definitions(repo))
        {
            var d = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.Equal("bc7Unorm", d["textureFormat"]!.GetValue<string>()); Assert.Equal("balanced", d["textureCompressionQuality"]!.GetValue<string>());
            if (d["textureUsage"]!.GetValue<string>() == "baseColor") Assert.Equal(1024, d["textureMaxDimension"]!.GetValue<int>());
        }
        foreach (string file in Directory.EnumerateFiles(Path.Combine(repo, "assets/source/ui/directorate/atlas"), "*.asset.json"))
        {
            var d = JsonNode.Parse(File.ReadAllText(file))!;
            if (d["type"]!.GetValue<string>() == "texture" && !Path.GetFileName(file).StartsWith("portraits_", StringComparison.Ordinal))
                Assert.Equal("rgba8Unorm", d["textureFormat"]!.GetValue<string>());
        }
    }
    [Fact]
    public void PortraitCompressionCannotRelaxLosslessCoreOrIconPageContracts()
    {
        var texture = new RuntimeTextureData(32, 32, RuntimeTextureFormat.Bc7Unorm, RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage, [new(32, 32, 128, new byte[1024])]);
        var portrait = new UiSkinPage("ui.test.portrait", 32, 32, UiSkinGroup.Portraits);
        RuntimeUiSkinData.ValidatePage(portrait, texture);
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.ValidatePage(portrait with { Group = UiSkinGroup.Core }, texture));
        Assert.Throws<InvalidDataException>(() => RuntimeUiSkinData.ValidatePage(portrait with { Group = UiSkinGroup.Icons }, texture));
    }
    [Fact]
    public void AllElevenProductionTexturesKeepDimensionsMipsColorSpaceAndBoundedCompressionError()
    {
        string repo = Repository(), root = Path.Combine(Path.GetTempPath(), "forgeline-production-bc7-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), output = Path.Combine(root, "runtime"); Directory.CreateDirectory(source);
        var ids = new List<string>();
        try
        {
            foreach (string file in Definitions(repo))
            {
                var definition = JsonNode.Parse(File.ReadAllText(file))!; string id = definition["id"]!.GetValue<string>(); ids.Add(id);
                string input = Path.Combine(Path.GetDirectoryName(file)!, definition["source"]!.GetValue<string>());
                string copied = id + ".png"; File.Copy(input, Path.Combine(source, copied)); definition["source"] = copied;
                File.WriteAllText(Path.Combine(source, id + ".asset.json"), definition.ToJsonString());
                definition["id"] = id + ".reference"; definition["textureFormat"] = "rgba8Unorm";
                File.WriteAllText(Path.Combine(source, id + ".reference.asset.json"), definition.ToJsonString());
            }
            var result = AssetPipelineCompiler.Compile(source, output, clean: true); Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            var catalog = RuntimeAssetCatalog.Load(output); var decoder = new BCnEncoder.Decoder.BcDecoder(); var results = new List<Quality>();
            foreach (string id in ids)
            {
                var compressed = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(id)).Payload);
                var original = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(id + ".reference")).Payload);
                Assert.Equal(RuntimeTextureFormat.Bc7Unorm, compressed.Format); Assert.Equal(original.Width, compressed.Width); Assert.Equal(original.Height, compressed.Height);
                Assert.Equal(original.ColorSpace, compressed.ColorSpace); Assert.Equal(original.Usage, compressed.Usage); Assert.Equal(original.Mips.Count, compressed.Mips.Count);
                Assert.True(compressed.ResidentByteCount <= original.ResidentByteCount / 3);
                var mip = compressed.Mips[0]; var decoded = decoder.DecodeRaw(mip.Pixels, mip.Width, mip.Height, BCnEncoder.Shared.CompressionFormat.Bc7); var pixels = original.Mips[0].Pixels;
                double squared = 0, alphaError = 0; long samples = 0; int maxAlpha = 0;
                for (int i = 0; i < decoded.Length; i++)
                {
                    int offset = i * 4; var actual = decoded[i]; int ae = Math.Abs(actual.a - pixels[offset + 3]); alphaError += ae; maxAlpha = Math.Max(maxAlpha, ae);
                    if (pixels[offset + 3] < 16) continue;
                    squared += Math.Pow(actual.r - pixels[offset], 2) + Math.Pow(actual.g - pixels[offset + 1], 2) + Math.Pow(actual.b - pixels[offset + 2], 2); samples += 3;
                }
                double mse = squared / samples; double psnr = mse == 0 ? 99 : 10 * Math.Log10(255 * 255 / mse);
                results.Add(new(id, mip.Width, mip.Height, compressed.ResidentByteCount, original.ResidentByteCount, psnr, alphaError / decoded.Length, maxAlpha));
            }
            string? report = Environment.GetEnvironmentVariable("FORGELINE_TEXTURE_QUALITY_REPORT");
            if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(report, JsonSerializer.Serialize(results));
            Assert.All(results, q => { Assert.True(q.PsnrRgb >= 32, q.ToString()); Assert.True(q.MeanAlphaError <= 2, q.ToString()); Assert.True(q.MaximumAlphaError <= 16, q.ToString()); });
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed record Quality(string Id, int Width, int Height, long CompressedBytes, long ReferenceBytes, double PsnrRgb, double MeanAlphaError, int MaximumAlphaError);
}
