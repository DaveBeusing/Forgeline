using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class UiArtworkCoverageTests
{
    private static readonly int[] PortraitSizes = [48, 64, 96, 128, 256];
    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Missing repository.");
        }
    }
    private static string Source => Path.Combine(Root, "assets/source/ui/directorate/artwork");

    [Fact]
    public void EveryCurrentSemanticEnumHasAnExplicitVisualOrNoneException()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "artwork.catalog.json")));
        JsonElement[] bindings = document.RootElement.GetProperty("bindings").EnumerateArray().ToArray();
        HashSet<string> roles = document.RootElement.GetProperty("sprites").EnumerateArray().Select(static s => s.GetProperty("logicalRole").GetString()!).ToHashSet(StringComparer.Ordinal);
        Type[] domains = [typeof(RtsUiIcon), typeof(GameplayAction), typeof(PlayerActionRequestKind), typeof(MainMenuCommand), typeof(PauseMenuCommand),
            typeof(StrategicOverlayMode), typeof(FrontendSettingsSection), typeof(FrontendSettingsField), typeof(RtsCursorKind), typeof(RtsMinimapSymbolKind),
            typeof(TacticalTargetingMode), typeof(BattlefieldSupplyStatus), typeof(PlayerCommandFeedbackState), typeof(UnitPresentationDamageState), typeof(BuildingPresentationState)];
        foreach (Type domain in domains)
        {
            JsonElement[] actual = bindings.Where(b => b.GetProperty("domain").GetString() == domain.Name).ToArray();
            string[] expected = Enum.GetNames(domain).Where(static n => n != "None").Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual.Select(static b => b.GetProperty("member").GetString()).Order(StringComparer.Ordinal).ToArray());
            foreach (JsonElement binding in actual) Assert.Contains(binding.GetProperty("role").GetString()!, roles);
        }
        foreach (RtsUiIconDefinition definition in RtsUiIconCatalog.All)
        {
            JsonElement binding = Assert.Single(bindings, b => b.GetProperty("domain").GetString() == nameof(RtsUiIcon) && b.GetProperty("member").GetString() == definition.Icon.ToString());
            Assert.Equal(definition.AssetId, binding.GetProperty("legacyAssetId").GetString());
        }
    }

    [Fact]
    public void EveryRealResourceTechnologyAndRosterIdentityHasTheCorrectSource()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "artwork.catalog.json")));
        JsonElement[] sprites = document.RootElement.GetProperty("sprites").EnumerateArray().ToArray();
        foreach (ResourceDefinition resource in InitialResourceDefinitions.CreateCatalog().Definitions)
            Assert.Equal(5, sprites.Count(s => s.GetProperty("logicalRole").GetString() == resource.Key));
        JsonElement[] portraits = sprites.Where(static s => s.GetProperty("logicalRole").GetString()!.StartsWith("portrait.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(100, portraits.Length);
        foreach (UnitDefinition unit in DirectorateContent.CreateUnitCatalog().Definitions)
            Verify("unit", unit.Id.Value, unit.Key, UnitPresentationCatalog.Get(unit.Id).MeshAssetId);
        foreach (BuildingDefinition building in DirectorateContent.CreateBuildingCatalog().Definitions)
            Verify("building", building.Id.Value, building.Key, BuildingPresentationCatalog.Get(building.Id).MeshAssetId);
        void Verify(string kind, uint id, string key, string mesh)
        {
            JsonElement[] family = portraits.Where(p => p.GetProperty("gameplayKind").GetString() == kind && p.GetProperty("gameplayId").GetUInt32() == id).ToArray();
            Assert.Equal(PortraitSizes, family.Select(static p => p.GetProperty("width").GetInt32()).Order().ToArray());
            foreach (JsonElement portrait in family)
            {
                Assert.Equal(key, portrait.GetProperty("gameplayKey").GetString());
                Assert.Equal(mesh, portrait.GetProperty("meshAssetId").GetString());
                Assert.True(portrait.GetProperty("requiresIdentifiedPermittedReadModel").GetBoolean());
            }
        }
        string[] technologies = DirectorateTechnologyDefinitions.CreateCatalog().Definitions.Select(static t => t.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(technologies, document.RootElement.GetProperty("bindings").EnumerateArray().Where(static b => b.GetProperty("domain").GetString() == "TechnologyId").Select(static b => b.GetProperty("member").GetString()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void NativePortraitsMatchCapturedBytesAndUnknownContactsStayGeneric()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "artwork.catalog.json")));
        JsonElement root = document.RootElement;
        JsonElement[] sprites = root.GetProperty("sprites").EnumerateArray().ToArray();
        JsonElement[] captures = root.GetProperty("nativePortraitCapture").GetProperty("captures").EnumerateArray().ToArray();
        Assert.Equal(100, captures.Length);
        foreach (JsonElement capture in captures)
        {
            JsonElement portrait = Assert.Single(sprites, s => s.TryGetProperty("nativeCaptureFile", out JsonElement file) && file.GetString() == capture.GetProperty("file").GetString());
            Assert.Equal(capture.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Source, portrait.GetProperty("file").GetString()!)))));
            Assert.True(capture.GetProperty("repeatPixelsEqual").GetBoolean());
            Assert.Equal(capture.GetProperty("meshRuntimeHash").GetString(), portrait.GetProperty("meshRuntimeHash").GetString());
        }
        JsonElement contact = Assert.Single(root.GetProperty("bindings").EnumerateArray(), b => b.GetProperty("domain").GetString() == nameof(RtsMinimapSymbolKind) && b.GetProperty("member").GetString() == nameof(RtsMinimapSymbolKind.DetectedContact));
        Assert.Equal("marker.detected_contact", contact.GetProperty("role").GetString());
        Assert.DoesNotContain(sprites, static s => s.GetProperty("id").GetString()!.Contains("aviation", StringComparison.Ordinal) || s.GetProperty("id").GetString()!.Contains("railway", StringComparison.Ordinal));
        Assert.All(sprites.Where(static s => s.GetProperty("logicalRole").GetString() == "command.patrol"), static s => Assert.Equal("intentionally_unavailable", s.GetProperty("availability").GetString()));
    }
}
