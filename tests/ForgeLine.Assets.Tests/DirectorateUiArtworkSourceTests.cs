using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateUiArtworkSourceTests
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64];
    private static readonly int[] PortraitSizes = [48, 64, 96, 128, 256];
    private static string Source
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Missing repository."), "assets/source/ui/directorate/artwork");
        }
    }

    [Fact]
    public void SourceIdentitiesDefinitionsAndAllSizeFamiliesAreComplete()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "artwork.catalog.json")));
        JsonElement[] sprites = document.RootElement.GetProperty("sprites").EnumerateArray().ToArray();
        Assert.Equal(815, sprites.Length);
        Assert.Equal(799, sprites.Count(static s => s.GetProperty("availability").GetString() == "runtime_required"));
        Assert.Equal(sprites.Length, sprites.Select(static s => s.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(sprites.Length, Directory.EnumerateFiles(Source, "*.png", SearchOption.AllDirectories).Count());
        Assert.Equal(799, Directory.EnumerateFiles(Source, "*.asset.json", SearchOption.AllDirectories).Count());
        foreach (var group in sprites.GroupBy(static s => s.GetProperty("logicalRole").GetString()))
        {
            int[] sizes = group.Select(static s => s.GetProperty("width").GetInt32()).Order().ToArray();
            Assert.Equal(group.Key!.StartsWith("portrait.", StringComparison.Ordinal) ? PortraitSizes : IconSizes, sizes);
        }
        foreach (JsonElement sprite in sprites)
        {
            string path = Path.Combine(Source, sprite.GetProperty("file").GetString()!);
            Assert.Equal(sprite.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            string definition = Path.ChangeExtension(path, ".asset.json");
            bool required = sprite.GetProperty("availability").GetString() == "runtime_required";
            Assert.Equal(required, File.Exists(definition));
            if (required)
            {
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(definition));
                Assert.Equal(sprite.GetProperty("id").GetString(), metadata.RootElement.GetProperty("id").GetString());
                Assert.Equal(Path.GetFileName(path), metadata.RootElement.GetProperty("source").GetString());
                Assert.False(metadata.RootElement.GetProperty("textureGenerateMipmaps").GetBoolean());
                Assert.Equal("linear", metadata.RootElement.GetProperty("textureColorSpace").GetString());
            }
            if (sprite.TryGetProperty("editableSource", out JsonElement editable))
            {
                string master = Path.Combine(Source, editable.GetString()!);
                Assert.DoesNotContain("\r", File.ReadAllText(master), StringComparison.Ordinal);
                Assert.Equal(sprite.GetProperty("editableSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(master))));
                Assert.DoesNotContain(XDocument.Load(master).Descendants(), static e => e.Name.LocalName is "text" or "image" or "script");
            }
        }
    }

    [Fact]
    public void AllPixelsDecodeAndCursorHotspotsBoundsAndStatusShapesRemainValid()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "forgeline-ui-art-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(temporary, "source"), runtime = Path.Combine(temporary, "runtime");
            Directory.CreateDirectory(source);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "artwork.catalog.json")));
            JsonElement[] sprites = document.RootElement.GetProperty("sprites").EnumerateArray().ToArray();
            foreach (JsonElement sprite in sprites)
            {
                string relative = sprite.GetProperty("file").GetString()!;
                string target = Path.Combine(source, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(Source, relative), target);
                // Isolated decode includes inspection-only sprites; never promote their definitions.
                File.WriteAllText(Path.ChangeExtension(target, ".asset.json"), JsonSerializer.Serialize(new
                { id = sprite.GetProperty("id").GetString(), type = "texture", source = Path.GetFileName(target), textureGenerateMipmaps = false, textureColorSpace = "linear", textureUsage = "displayImage" }));
            }
            AssetCompilationResult result = AssetPipelineCompiler.Compile(source, runtime, clean: true);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(815, result.CompiledCount);
            RuntimeAssetCatalog catalog = RuntimeAssetCatalog.Load(runtime);
            var statusMasks = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement sprite in sprites)
            {
                string role = sprite.GetProperty("logicalRole").GetString()!;
                RuntimeTextureData texture = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(sprite.GetProperty("id").GetString()!)).Payload);
                int width = texture.Width, height = texture.Height;
                Assert.Equal(sprite.GetProperty("width").GetInt32(), width);
                Assert.Equal(sprite.GetProperty("height").GetInt32(), height);
                Assert.Single(texture.Mips);
                byte[] pixels = texture.Mips[0].Pixels;
                if (sprite.GetProperty("alphaMode").GetString() == "opaque_native_capture")
                {
                    Assert.All(Enumerable.Range(0, width * height), i => Assert.Equal(255, pixels[i * 4 + 3]));
                    Assert.Contains(Enumerable.Range(0, width * height), i => !pixels.AsSpan(i * 4, 3).SequenceEqual(pixels.AsSpan(0, 3)));
                    continue;
                }
                int left = width, top = height, right = -1, bottom = -1;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    byte alpha = pixels[(y * width + x) * 4 + 3];
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) Assert.Equal(0, alpha);
                    if (alpha == 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
                Assert.True(right >= left && bottom >= top, role);
                Assert.Equal(new[] { left, top, right + 1, bottom + 1 }, sprite.GetProperty("visibleBounds").EnumerateArray().Select(static n => n.GetInt32()).ToArray());
                if (sprite.TryGetProperty("hotspot", out JsonElement hotspot))
                {
                    int x = hotspot[0].GetInt32(), y = hotspot[1].GetInt32();
                    Assert.InRange(x, left, right); Assert.InRange(y, top, bottom);
                    Assert.True(pixels[(y * width + x) * 4 + 3] > 0, role);
                    Assert.Equal("source_only_no_cursor_enabled", sprite.GetProperty("activation").GetString());
                }
                if (width == 16 && role is "status.pending" or "status.rejected" or "status.invalid" or "status.unavailable" or "marker.friendly_unit" or "marker.visible_enemy" or "marker.detected_contact")
                    Assert.True(statusMasks.Add(Convert.ToHexStringLower(SHA256.HashData(pixels.Where(static (_, i) => i % 4 == 3).ToArray()))), role);
            }
            Assert.Equal(7, statusMasks.Count);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }
}
