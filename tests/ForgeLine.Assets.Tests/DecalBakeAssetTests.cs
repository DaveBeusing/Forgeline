using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DecalBakeAssetTests
{
    private static readonly string[] Families = ["tire_tracks", "tracked_vehicle_marks", "road_wear", "oil_stain", "blast_mark", "shell_impact", "scorch_mark", "concrete_crack"];
    private static string Repository()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ForgeLine.sln"))) return folder.FullName;
        throw new DirectoryNotFoundException();
    }

    [Fact]
    public void EightStableFamiliesShareThreeBoundedTexturesAndTheOriginalCarrier()
    {
        string source = Path.Combine(Repository(), "assets/source");
        for (int index = 0; index < Families.Length; index++)
        {
            string family = Families[index];
            using var material = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "world/materials/decals", family + ".material.json")));
            var data = material.RootElement;
            Assert.Equal("texture.world.decal.atlas_base", data.GetProperty("baseColorTexture").GetString());
            Assert.Equal("texture.world.decal.atlas_normal", data.GetProperty("normalTexture").GetString());
            Assert.Equal("texture.world.decal.atlas_orm", data.GetProperty("ormTexture").GetString());
            Assert.Equal(0, data.GetProperty("metallicFactor").GetSingle());
            Assert.Equal(1, data.GetProperty("roughnessFactor").GetSingle());
            Assert.Equal(.214f, data.GetProperty("uvScale")[0].GetSingle());
            Assert.Equal(.464f, data.GetProperty("uvScale")[1].GetSingle());
            Assert.Equal(.018f + index % 4 * .25f, data.GetProperty("uvOffset")[0].GetSingle());
            Assert.Equal(.018f + index / 4 * .5f, data.GetProperty("uvOffset")[1].GetSingle());
            Assert.True(new FileInfo(Path.Combine(source, "world/authoring/decals", family + ".blend")).Length > 100_000);
        }
    }

    [Fact]
    public void RealCompiledCatalogRetainsStraightCoverageTangentBasisAndSourceLessLoadingWithinCaps()
    {
        string production = Path.Combine(Repository(), "assets/source");
        string temporary = Path.Combine(Path.GetTempPath(), "forgeline-decal-bakes-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(temporary, "source"), runtime = Path.Combine(temporary, "runtime");
        Directory.CreateDirectory(source);
        try
        {
            foreach (string family in Families) CopyDefinition($"world/materials/decals/{family}.asset.json");
            foreach (string channel in new[] { "decal_atlas", "decal_atlas_normal", "decal_atlas_orm" })
                CopyDefinition($"world/textures/decals/{channel}.asset.json");
            foreach (string file in Directory.EnumerateFiles(Path.Combine(source, "world/textures/decals"), "*.asset.json").ToArray())
            {
                var reference = JsonNode.Parse(File.ReadAllText(file))!;
                reference["id"] = reference["id"]!.GetValue<string>() + ".reference";
                reference["textureFormat"] = "rgba8Unorm";
                File.WriteAllText(file.Replace(".asset.json", ".reference.asset.json"), reference.ToJsonString());
            }
            CopyDefinition("world/meshes/decals/quad.asset.json");
            var result = AssetPipelineCompiler.Compile(source, runtime, clean: true);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Directory.Move(source, source + "-unavailable");
            var catalog = RuntimeAssetCatalog.Load(runtime);
            var mesh = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse("mesh.world.decal.quad")).Payload);
            Assert.Equal(4, mesh.Vertices.Count);
            Assert.Equal(6, mesh.Indices.Count);
            Assert.True((mesh.Attributes & RuntimeMeshAttributes.Tangent) != 0);
            Assert.Empty(mesh.MaterialIds);
            foreach (var vertex in mesh.Vertices)
            {
                Assert.Equal(0, vertex.Y);
                Assert.Equal(1, vertex.NormalY);
                Assert.InRange(MathF.Abs(vertex.X), .49999f, .50001f);
                Assert.InRange(MathF.Abs(vertex.Z), .49999f, .50001f);
            }
            long resident = 0;
            var quality = new List<object>();
            var qualityFailures = new List<string>();
            var decoder = new BCnEncoder.Decoder.BcDecoder();
            foreach (var channel in new[] { ("base", 512, RuntimeTextureUsage.BaseColor), ("normal", 256, RuntimeTextureUsage.Normal), ("orm", 256, RuntimeTextureUsage.Orm) })
            {
                var texture = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse("texture.world.decal.atlas_" + channel.Item1)).Payload);
                Assert.Equal(channel.Item2, texture.Width); Assert.Equal(channel.Item2, texture.Height);
                Assert.Equal(4, texture.Mips.Count); Assert.Equal(channel.Item3, texture.Usage);
                Assert.Equal(channel.Item1 == "normal" ? RuntimeTextureFormat.Rgba8Unorm : RuntimeTextureFormat.Bc7Unorm, texture.Format);
                Assert.Equal(channel.Item1 == "base" ? RuntimeTextureColorSpace.Srgb : RuntimeTextureColorSpace.Linear, texture.ColorSpace);
                resident += texture.ResidentByteCount;
                var original = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse("texture.world.decal.atlas_" + channel.Item1 + ".reference")).Payload);
                for (int level = 0; level < texture.Mips.Count; level++)
                {
                    var compressedMip = texture.Mips[level];
                    var decodedMip = texture.Format == RuntimeTextureFormat.Bc7Unorm
                        ? decoder.DecodeRaw(compressedMip.Pixels, compressedMip.Width, compressedMip.Height, BCnEncoder.Shared.CompressionFormat.Bc7)
                        : Enumerable.Range(0, compressedMip.Width * compressedMip.Height).Select(i => new BCnEncoder.Shared.ColorRgba32(compressedMip.Pixels[i * 4], compressedMip.Pixels[i * 4 + 1], compressedMip.Pixels[i * 4 + 2], compressedMip.Pixels[i * 4 + 3])).ToArray();
                    var pixels = original.Mips[level].Pixels;
                    double squared = 0, alphaError = 0; int maxAlpha = 0;
                    var angles = new List<double>();
                    for (int i = 0; i < decodedMip.Length; i++)
                    {
                        var actual = decodedMip[i]; int offset = i * 4;
                        int ae = Math.Abs(actual.a - pixels[offset + 3]); alphaError += ae; maxAlpha = Math.Max(maxAlpha, ae);
                        squared += Math.Pow(actual.r - pixels[offset], 2) + Math.Pow(actual.g - pixels[offset + 1], 2) + Math.Pow(actual.b - pixels[offset + 2], 2);
                        if (channel.Item1 == "normal")
                        {
                            var a = System.Numerics.Vector3.Normalize(new(actual.r / 127.5f - 1, actual.g / 127.5f - 1, actual.b / 127.5f - 1));
                            var b = System.Numerics.Vector3.Normalize(new(pixels[offset] / 127.5f - 1, pixels[offset + 1] / 127.5f - 1, pixels[offset + 2] / 127.5f - 1));
                            angles.Add(Math.Acos(Math.Clamp(System.Numerics.Vector3.Dot(a, b), -1, 1)) * 180 / Math.PI);
                        }
                    }
                    double mse = squared / (decodedMip.Length * 3); double psnr = mse == 0 ? 99 : 10 * Math.Log10(255 * 255 / mse);
                    double meanAlpha = alphaError / decodedMip.Length;
                    angles.Sort(); double meanAngle = angles.Count == 0 ? 0 : angles.Average();
                    double p99Angle = angles.Count == 0 ? 0 : angles[(int)((angles.Count - 1) * .99)];
                    double maxAngle = angles.Count == 0 ? 0 : angles[^1];
                    quality.Add(new { channel = channel.Item1, level, psnr, meanAlpha, maxAlpha, meanAngle, p99Angle, maxAngle });
                    if (psnr < 32) qualityFailures.Add($"{channel.Item1} mip {level} PSNR {psnr}");
                    if (meanAlpha > 2 || maxAlpha > 16) qualityFailures.Add($"{channel.Item1} mip {level} alpha {meanAlpha}/{maxAlpha}");
                    if (meanAngle > 2 || p99Angle > 6 || maxAngle > 12) qualityFailures.Add($"normal mip {level} angle {meanAngle}/{p99Angle}/{maxAngle}");
                }
                if (channel.Item1 != "base") continue;
                var decoded = decoder.DecodeRaw(texture.Mips[0].Pixels, texture.Width, texture.Height, BCnEncoder.Shared.CompressionFormat.Bc7);
                for (int family = 0; family < 8; family++)
                {
                    var alphas = new List<byte>();
                    var mip = texture.Mips[0];
                    for (int y = family / 4 * 256; y < (family / 4 + 1) * 256; y++)
                    for (int x = family % 4 * 128; x < (family % 4 + 1) * 128; x++)
                        alphas.Add(decoded[y * mip.Width + x].a);
                    Assert.Equal(0, alphas.Min()); Assert.InRange(alphas.Max(), 64, 255);
                    Assert.Contains(alphas, alpha => alpha is > 0 and < 128);
                }
            }
            Assert.Equal(783_360, resident);
            string? report = Environment.GetEnvironmentVariable("FORGELINE_DECAL_QUALITY_REPORT");
            if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(report, JsonSerializer.Serialize(quality));
            Assert.True(qualityFailures.Count == 0, string.Join(Environment.NewLine, qualityFailures));
            foreach (string family in Families)
            {
                using var material = JsonDocument.Parse(catalog.Read(AssetId.Parse("material.world.decal." + family)).Payload);
                Assert.Equal("texture.world.decal.atlas_normal", material.RootElement.GetProperty("normalTexture").GetString());
                Assert.Equal("texture.world.decal.atlas_orm", material.RootElement.GetProperty("ormTexture").GetString());
            }
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }

        void CopyDefinition(string relative)
        {
            string input = Path.Combine(production, relative);
            Copy(input);
            using var definition = JsonDocument.Parse(File.ReadAllText(input));
            Copy(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input)!, definition.RootElement.GetProperty("source").GetString()!)));
        }
        void Copy(string input)
        {
            string relative = Path.GetRelativePath(production, input);
            Assert.False(relative.StartsWith("..", StringComparison.Ordinal));
            string output = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(input, output, true);
        }
    }
}
