using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateUiSkinSourceTests
{
    private static readonly string Repository = FindRepository();
    private static readonly string Source = Path.Combine(Repository, "assets/source/ui/directorate");

    [Fact]
    public void SourceCatalogHasUniqueCompleteCompilerDefinitionsAndValidSlices()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "skin.catalog.json")));
        JsonElement sprites = document.RootElement.GetProperty("sprites");
        Assert.Equal(37, sprites.GetArrayLength());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement sprite in sprites.EnumerateArray())
        {
            string id = sprite.GetProperty("id").GetString()!;
            string file = sprite.GetProperty("file").GetString()!;
            string editable = sprite.GetProperty("editableSource").GetString()!;
            Assert.True(ids.Add(id), id);
            Assert.True(files.Add(file), file);
            Assert.StartsWith("ui.skin.directorate.", id, StringComparison.Ordinal);
            Assert.Equal(Path.GetFileName(file), file);
            Assert.Equal(Path.GetFileName(editable), editable);
            Assert.Equal(Hash(Path.Combine(Source, file)), sprite.GetProperty("sha256").GetString());
            Assert.Equal(Hash(Path.Combine(Source, editable)), sprite.GetProperty("editableSha256").GetString());
            XDocument svg = XDocument.Load(Path.Combine(Source, editable));
            Assert.DoesNotContain(svg.Descendants(), e => e.Name.LocalName is "text" or "image" or "script");
            Assert.Equal(sprite.GetProperty("width").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), svg.Root!.Attribute("width")!.Value);
            int[] insets = sprite.GetProperty("insets").EnumerateArray().Select(static x => x.GetInt32()).ToArray();
            int[] minimum = sprite.GetProperty("minimumSize").EnumerateArray().Select(static x => x.GetInt32()).ToArray();
            Assert.Equal(4, insets.Length);
            Assert.Equal(2, minimum.Length);
            Assert.All(insets, static n => Assert.True(n >= 0));
            Assert.True(insets[0] + insets[2] < minimum[0], id);
            Assert.True(insets[1] + insets[3] < minimum[1], id);
            Assert.InRange(minimum[0], 1, sprite.GetProperty("width").GetInt32());
            Assert.InRange(minimum[1], 1, sprite.GetProperty("height").GetInt32());
            using JsonDocument definition = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, Path.ChangeExtension(file, ".asset.json"))));
            Assert.Equal(id, definition.RootElement.GetProperty("id").GetString());
            Assert.Equal(file, definition.RootElement.GetProperty("source").GetString());
            Assert.Equal("texture", definition.RootElement.GetProperty("type").GetString());
            Assert.False(definition.RootElement.GetProperty("textureGenerateMipmaps").GetBoolean());
            Assert.Equal("rgba8Unorm", definition.RootElement.GetProperty("textureFormat").GetString());
        }
        Assert.Equal(files.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Source, "*.png").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(37, Directory.EnumerateFiles(Source, "*.asset.json").Count());
        Assert.Equal(37, Directory.EnumerateFiles(Source, "*.svg").Count());
    }

    [Fact]
    public void EverySourceDecodesThroughCompilerWithTransparentPerimeterAndHollowFrames()
    {
        string runtime = Path.Combine(Path.GetTempPath(), "forgeline-ui-skin-" + Guid.NewGuid().ToString("N"));
        try
        {
            AssetCompilationResult result = AssetPipelineCompiler.Compile(Source, runtime, clean: true);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(37, result.CompiledCount);
            RuntimeAssetCatalog catalog = RuntimeAssetCatalog.Load(runtime);
            using JsonDocument sourceCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "skin.catalog.json")));
            foreach (JsonElement source in sourceCatalog.RootElement.GetProperty("sprites").EnumerateArray())
            {
                string id = source.GetProperty("id").GetString()!;
                RuntimeTextureData texture = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(id)).Payload);
                Assert.Equal(source.GetProperty("width").GetInt32(), texture.Width);
                Assert.Equal(source.GetProperty("height").GetInt32(), texture.Height);
                Assert.Equal(RuntimeTextureColorSpace.Linear, texture.ColorSpace);
                Assert.Equal(RuntimeTextureFormat.Rgba8Unorm, texture.Format);
                Assert.Single(texture.Mips);
                byte[] pixels = texture.Mips[0].Pixels;
                Assert.Contains(Enumerable.Range(0, texture.Width * texture.Height), i => pixels[i * 4 + 3] > 0);
                for (int x = 0; x < texture.Width; x++)
                {
                    Assert.Equal(0, pixels[x * 4 + 3]);
                    Assert.Equal(0, pixels[((texture.Height - 1) * texture.Width + x) * 4 + 3]);
                }
                for (int y = 0; y < texture.Height; y++)
                {
                    Assert.Equal(0, pixels[y * texture.Width * 4 + 3]);
                    Assert.Equal(0, pixels[(y * texture.Width + texture.Width - 1) * 4 + 3]);
                }
                if (id.EndsWith(".portrait", StringComparison.Ordinal) || id.EndsWith(".minimap", StringComparison.Ordinal) ||
                    id.Contains(".overlay.", StringComparison.Ordinal) && !id.EndsWith(".disabled", StringComparison.Ordinal))
                    Assert.Equal(0, pixels[((texture.Height / 2) * texture.Width + texture.Width / 2) * 4 + 3]);
            }
        }
        finally { if (Directory.Exists(runtime)) Directory.Delete(runtime, recursive: true); }
    }

    [Fact]
    public void StatesReferenceRealLayersAndPreserveIndependentFocusAndSelection()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "skin.catalog.json")));
        JsonElement root = document.RootElement;
        HashSet<string> ids = root.GetProperty("sprites").EnumerateArray().Select(static s => s.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        JsonElement states = root.GetProperty("states");
        string[] required = ["normal", "hover", "pressed", "focus", "selected", "disabled", "warning", "error", "pending", "accepted", "rejected"];
        Assert.Equal(required.Order(StringComparer.Ordinal), states.EnumerateObject().Select(static s => s.Name).Order(StringComparer.Ordinal));
        Assert.Empty(states.GetProperty("normal").EnumerateArray());
        foreach (JsonProperty state in states.EnumerateObject())
            foreach (JsonElement layer in state.Value.EnumerateArray()) Assert.Contains(layer.GetString()!, ids);
        Assert.NotEqual(states.GetProperty("focus")[0].GetString(), states.GetProperty("selected")[0].GetString());
        Assert.Equal(states.GetProperty("error")[0].GetString(), states.GetProperty("rejected")[0].GetString());
        Assert.Equal("pending_human_review", root.GetProperty("reviewStatus").GetString());
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Existing Git blob identity requires SHA-1 compatibility; original content integrity is checked independently with SHA-256.")]
    public void OriginalBoardsRetainAuthenticatedIdentitiesAndDecodeWithoutRuntimeRegistration()
    {
        string reference = Path.Combine(Repository, "assets/reference/ui/directorate");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(reference, "catalog.json")));
        JsonElement root = document.RootElement;
        Assert.Equal("complete", root.GetProperty("importStatus").GetString());
        Assert.False(root.GetProperty("runtimeEligible").GetBoolean());
        string[] expected =
        [
            "57d51455045aca0c222fa836ae10d10829556f2acc5c2480db3215a1ec0f2a04",
            "187f22dda7fe420a85cbc51c8e0b823160ee2249b157ebe0c8b049a252edce21",
            "2a02bc2897c84a29d6dbd9918ea0771e1ca665368af3f18075edba5ebcdc3458",
            "f1c87d16ad06fc91eba53c8742f0b28353d906a802ebaa270942d86d2a4398d1",
            "1e5845f4a5b0df70fab834efca2cf5a6d8067ab11ea88f66f3c6639c0dbeba5e"
        ];
        string temporary = Path.Combine(Path.GetTempPath(), "forgeline-ui-reference-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(temporary, "source");
            string runtime = Path.Combine(temporary, "runtime");
            Directory.CreateDirectory(source);
            JsonElement[] originals = root.GetProperty("references").EnumerateArray().ToArray();
            Assert.Equal(5, originals.Length);
            foreach (JsonElement item in originals)
            {
                string name = item.GetProperty("file").GetString()!;
                byte[] bytes = File.ReadAllBytes(Path.Combine(reference, name));
                string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                Assert.Contains(sha, expected);
                Assert.Equal(item.GetProperty("sha256").GetString(), sha);
                Assert.Equal(item.GetProperty("bytes").GetInt32(), bytes.Length);
                byte[] header = System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
                Assert.Equal(item.GetProperty("gitBlobSha").GetString(), Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes])));
                File.WriteAllBytes(Path.Combine(source, name), bytes);
                string id = item.GetProperty("id").GetString()!;
                File.WriteAllText(Path.Combine(source, Path.ChangeExtension(name, ".asset.json")), JsonSerializer.Serialize(new
                { id, type = "texture", source = name, textureGenerateMipmaps = false }));
            }
            // Isolated decoder exercise only. These definitions never enter assets/source.
            AssetCompilationResult result = AssetPipelineCompiler.Compile(source, runtime, clean: true);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(5, result.CompiledCount);
            RuntimeAssetCatalog catalog = RuntimeAssetCatalog.Load(runtime);
            foreach (JsonElement item in originals)
            {
                RuntimeTextureData image = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(item.GetProperty("id").GetString()!)).Payload);
                Assert.Equal(item.GetProperty("width").GetInt32(), image.Width);
                Assert.Equal(item.GetProperty("height").GetInt32(), image.Height);
                byte[] alpha = image.Mips[0].Pixels.Where(static (_, i) => i % 4 == 3).ToArray();
                Assert.Equal(255, alpha.Max());
                Assert.Equal(item.GetProperty("hasAlphaChannel").GetBoolean() ? 0 : 255, alpha.Min());
            }
            Assert.Empty(Directory.EnumerateFiles(reference, "*.asset.json", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot find source repository.");
    }
}
