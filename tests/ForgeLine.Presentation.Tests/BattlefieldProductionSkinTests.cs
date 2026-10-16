using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class BattlefieldProductionSkinTests
{
    [Fact]
    public void EveryRosterPortraitAndHudIconMatchesTheAuthoritativeArtworkCatalog()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "assets/source/ui/directorate/artwork/artwork.catalog.json")));
        var sprites = document.RootElement.GetProperty("sprites").EnumerateArray().ToArray();
        var packed = RuntimeUiSkinData.FromPayload(RuntimeAssetCatalog.Load(RuntimeRoot()).Read(AssetId.Parse(UiSkinResources.AssetName)).Payload);
        foreach (string id in GameplayHudArtwork.RequiredIcons.Concat(GameplayHudArtwork.RequiredPortraits))
            Assert.Contains(packed.Sprites, sprite => sprite.Id == id);
        foreach (var definition in DirectorateContent.CreateUnitCatalog().Definitions)
            Verify(PlayerSelectionSummary.Empty with { Count = 1, Kind = PlayerSelectionKind.Unit, CommonUnitId = definition.Id }, "unit", definition.Id.Value);
        foreach (var definition in DirectorateContent.CreateBuildingCatalog().Definitions)
            Verify(PlayerSelectionSummary.Empty with { Count = 1, Kind = PlayerSelectionKind.Building, CommonBuildingId = definition.Id }, "building", definition.Id.Value);
        void Verify(PlayerSelectionSummary selection, string kind, uint id)
        {
            foreach (int size in new[] { 48, 64, 96, 128, 256 })
            {
                string artwork = Assert.IsType<string>(GameplayHudArtwork.Portrait(selection, size));
                var sprite = Assert.Single(sprites, s => s.GetProperty("id").GetString() == artwork);
                Assert.Equal(kind, sprite.GetProperty("gameplayKind").GetString());
                Assert.Equal(id, sprite.GetProperty("gameplayId").GetUInt32());
                Assert.Equal(size, sprite.GetProperty("width").GetInt32());
                Assert.True(sprite.GetProperty("requiresIdentifiedPermittedReadModel").GetBoolean());
            }
        }
    }
    [Fact]
    public void MissingIdentityEmptyMixedAndConstructionSelectionsNeverInventAPortrait()
    {
        var known = Selection;
        Assert.NotNull(GameplayHudArtwork.Portrait(known, 32));
        foreach (var selection in new[] { PlayerSelectionSummary.Empty, known with { Count = 0 }, known with { Count = 2 },
            known with { Kind = PlayerSelectionKind.Mixed }, known with { Kind = PlayerSelectionKind.Construction }, known with { CommonUnitId = default } })
            Assert.Null(GameplayHudArtwork.Portrait(selection, 32));
        // Identity and owned operational facts are independent copied permissions.
        Assert.NotNull(GameplayHudArtwork.Portrait(known with { HasHealth = false, HasSupply = false }, 32));
        Assert.Null(GameplayHudArtwork.Icon(RtsUiIcon.CursorMove, 16));
        Assert.Null(GameplayHudArtwork.Icon(RtsUiIcon.CommandPatrol, 16));
    }
    [Fact]
    public void NativeHudSharesSevenPagesAndReleasesEveryResourceAcrossSessionsAndSelectionChanges()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE battlefield skin", 1280, 720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = true });
        var catalog = RuntimeAssetCatalog.Load(RuntimeRoot());
        for (int session = 0; session < 2; session++)
        {
            using (var hud = new GameplayHudRenderer(device, catalog))
            {
                Assert.True(hud.SkinDiagnostics.Prepared);
                Assert.Equal(28 * 1024 * 1024, hud.SkinDiagnostics.ResidentPageBytes);
                Assert.Equal(8, device.Diagnostics.Resources.LoadedTextureCount); // seven shared atlas pages plus one shared font
                Assert.Equal(13, hud.SkinDiagnostics.Batches);
                Assert.Equal(52, hud.SkinDiagnostics.Buffers);
                long uploads = device.Diagnostics.Resources.TextureUploadCount;
                var camera = new RtsCamera();
                foreach (var selected in new[] { Selection, Selection with { DisplayName = new string('W', 300) },
                    Selection with { Count = 3, Kind = PlayerSelectionKind.Mixed }, PlayerSelectionSummary.Empty,
                    Selection with { HasHealth = false, HasSupply = false, HasReadiness = false }, Selection })
                {
                    var experience = default(PlayerExperienceSnapshot) with { Tick = new(1), Selection = selected, Resources = new(0,1,0,2,0,0,0) };
                    var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [], playerExperience: experience);
                    device.RenderFrame(GraphicsColor.ForgeLineClear, c => hud.Render(c, camera, snapshot,
                        new AxisAlignedBounds(new(-100), new(100)), default, default, default, FormationTemplate.Compact,
                        CombatGroupOverviewView.Empty, default, 192, 2));
                    Assert.Equal(0, hud.SkinDiagnostics.MissingAssets);
                    Assert.Equal(0, hud.SkinDiagnostics.DroppedQuads);
                    Assert.Equal(0, hud.TextDiagnostics.DroppedGlyphs);
                    Assert.True(hud.SkinDiagnostics.Vertices > 0);
                    Assert.Equal(uploads, device.Diagnostics.Resources.TextureUploadCount);
                }
                device.RenderFrame(GraphicsColor.ForgeLineClear, c => hud.Render(c, camera, null,
                    new AxisAlignedBounds(new(-100), new(100)), default, default, default, FormationTemplate.Compact,
                    CombatGroupOverviewView.Empty, default, 96, 1));
                Assert.Equal(GameplayHudState.WaitingForSnapshot, hud.State);
            }
            device.WaitForIdle();
            Assert.Equal(0, device.Diagnostics.Resources.LoadedTextureCount);
            Assert.Equal(0, device.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        }
        Assert.Equal(0, device.Diagnostics.Health.SubmissionFaultCount);
    }
    [Fact]
    public void MissingProductionAssetsFailExplicitlyAndLeaveBootstrapUsable()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-missing-hud-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(RuntimeAssetIntegrity.SealManifest(new() { CompilerVersion = "test" }), RuntimeAssetCatalog.CreateJsonOptions()));
            using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE HUD preparation", 1280, 720));
            using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
            Assert.Throws<KeyNotFoundException>(() => new GameplayHudRenderer(device, RuntimeAssetCatalog.Load(root)));
            device.WaitForIdle(); Assert.Equal(0, device.Diagnostics.Resources.LoadedTextureCount);
            using var bootstrap = new GameplayHudRenderer(device);
            Assert.False(bootstrap.SkinDiagnostics.Prepared);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void NativeHiddenOperationalFieldsDoNotChangePixelsAndEmptySelectionClearsSkinCommands()
    {
        using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE selection privacy", 1280, 720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        using var inspector = new SelectionInspectorHudRenderer(device, RuntimeAssetCatalog.Load(RuntimeRoot()));
        var hidden = Selection with { HasHealth = false, HasSupply = false, HasReadiness = false, HasPower = false };
        byte[] Capture(PlayerSelectionSummary selection) => device.CaptureFrame(GraphicsColor.ForgeLineClear, c =>
            inspector.Render(c, new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), 0, [],
                playerExperience: default(PlayerExperienceSnapshot) with { Tick = new(1), Selection = selection }),
                GameplayHudLayout.Create(c.Width, c.Height, 96)));
        var before = Capture(hidden);
        var after = Capture(hidden with { HealthFraction = .99, FuelFraction = .99, AmmunitionFraction = .99,
            Readiness = .99, InventoryQuantity = 100000, PowerState = PowerOperationalState.Brownout });
        Assert.True(before.AsSpan().SequenceEqual(after));
        var longName = Capture(hidden with { DisplayName = new string('W', 300) });
        var emptyName = Capture(hidden with { DisplayName = string.Empty });
        Assert.False(longName.AsSpan().SequenceEqual(emptyName));
        Capture(PlayerSelectionSummary.Empty);
        Assert.Equal(0, inspector.LastRenderedVertexCount);
        Assert.Equal(0, inspector.SkinDiagnostics.Vertices);
    }
    [Fact]
    public void PreparedSkinGeometryDoesNotAllocateOrUploadTexturesDuringWarmRecording()
    {
        using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE HUD geometry", 1280, 720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        using var skin = new GameplayHudSkin(device, RuntimeAssetCatalog.Load(RuntimeRoot()), icons: true, portraits: true);
        long uploads = device.Diagnostics.Resources.TextureUploadCount;
        device.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            void Record()
            {
                skin.Begin(context); skin.Panel(new(10,10,300,60), "panel.resource");
                skin.Control(new(10,80,120,32), selected: true, hover: true);
                skin.Icon(RtsUiIcon.ResourceSteel, 20,20,24); skin.Portrait(Selection, new(350,10,48,48));
                skin.Bar(new(20,50,200,6), .23, System.Numerics.Vector4.One);
            }
            for (int i = 0; i < 20; i++) Record();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Record();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            skin.Flush(context);
        });
        Assert.Equal(uploads, device.Diagnostics.Resources.TextureUploadCount);
    }
    private static PlayerSelectionSummary Selection => PlayerSelectionSummary.Empty with { Count = 1, Kind = PlayerSelectionKind.Unit,
        DisplayName = "Main Battle Tank", CommonUnitId = UnitIds.MainBattleTank, HasHealth = true, HealthFraction = .23,
        HasSupply = true, FuelFraction = .12, AmmunitionFraction = .08, HasReadiness = true, Readiness = .31 };
    private static string RuntimeRoot() => Path.Combine(Root(), "assets/runtime");
    private static string Root()
    { for (var p = new DirectoryInfo(AppContext.BaseDirectory); p is not null; p = p.Parent) if (File.Exists(Path.Combine(p.FullName, "ForgeLine.sln"))) return p.FullName; throw new DirectoryNotFoundException(); }
}
