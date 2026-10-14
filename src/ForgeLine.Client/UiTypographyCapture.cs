using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class UiTypographyCapture
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Capture output must be empty.");
            Directory.CreateDirectory(output);
            var catalog = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var fixture = VisualReferenceScene.Create(VisualReferenceSceneKind.IndustrialBase, terrain, runtimeAssets: catalog);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Typography Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false, EnableMemoryDiagnostics = true });
            using var frontend = new FrontendOverlayRenderer(graphics);
            using var hud = new GameplayHudRenderer(graphics, catalog);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: catalog,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(catalog))
            { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, catalog);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(fixture.Target, RtsReferenceZoom.NormalGameplay));
            var buffer = new PresentationSnapshotBuffer(); buffer.Publish(fixture.Snapshot);
            var world = new RenderWorld(); world.Update(buffer);
            var menu = FrontendSurfaceView.MainMenu([new("continue", "Continue", false, false),
                new("new-game", "New Game", true, true), new("settings", "Settings / ÄÖÜ äöü ß", true, false)], typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);
            var results = new List<object>();
            foreach (var display in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440) })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                ground.Prepare(camera, display.Item1, display.Item2);
                foreach (uint dpi in new uint[] { 96, 144, 192 })
                    foreach (float scale in new[] { .75f, 1f, 2f })
                    {
                        string key = FormattableString.Invariant($"{display.Item1}x{display.Item2}-{dpi}dpi-{scale:0.00}");
                        var layout = FrontendDesign.ResolveLayout(display.Item1, display.Item2, scale);
                        if (layout.ContentWidth > display.Item1 + .01 || layout.ContentHeight > display.Item2 + .01)
                            throw new InvalidDataException("Frontend exceeds native surface.");
                        foreach (bool gameplay in new[] { false, true })
                        {
                            void Record(IGraphicsCommandContext c)
                            {
                                if (gameplay)
                                {
                                    ground.Render(c, camera); meshes.Render(c, camera, world, 1);
                                    c.BeginPass(GraphicsFramePass.Overlay);
                                    hud.Render(c, camera, fixture.Snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                        default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, dpi, scale);
                                }
                                else { c.BeginPass(GraphicsFramePass.Overlay); frontend.Render(c, menu, scale); }
                            }
                            for (int i = 0; i < 4; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            var watch = Stopwatch.StartNew(); byte[] rgba = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record); watch.Stop();
                            byte[] repeated = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                            bool equal = rgba.AsSpan().SequenceEqual(repeated);
                            if (!equal) throw new InvalidDataException("Native typography capture is not repeatable.");
                            var textDiagnostics = gameplay ? hud.TextDiagnostics : frontend.TextDiagnostics;
                            if (!textDiagnostics.AtlasReady || textDiagnostics.MissingGlyphs != 0 || textDiagnostics.DroppedGlyphs != 0)
                                throw new InvalidDataException("Bundled font text is incomplete.");
                            string? image = null;
                            if (dpi == 96 && (scale == 1 || display == (1920, 1080)))
                            {
                                image = $"{(gameplay ? "hud" : "menu")}-{key}.png";
                                ReferenceCaptureImage.WritePng(Path.Combine(output, image), display.Item1, display.Item2, rgba);
                            }
                            results.Add(new
                            {
                                Width = display.Item1,
                                Height = display.Item2,
                                Dpi = dpi,
                                UiScale = scale,
                                Gameplay = gameplay,
                                Image = image,
                                Repeatable = equal,
                                PixelSha256 = Convert.ToHexString(SHA256.HashData(rgba)),
                                Vertices = gameplay ? hud.LastRenderedVertexCount : frontend.LastRenderedVertexCount,
                                CaptureReadbackMilliseconds = watch.Elapsed.TotalMilliseconds,
                                Font = textDiagnostics
                            });
                        }
                        if (display == (1920, 1080) && dpi == 96 && scale == 1)
                        {
                            var views = new Dictionary<string, FrontendSurfaceView>
                            {
                                ["loading"] = FrontendSurfaceView.Loading("Loading / 75% →", true, .75f),
                                ["settings"] = FrontendPresentationAdapter.Settings(new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings())), new SettingsInteractionModel()),
                                ["controls"] = FrontendSurfaceView.Detail("Controls / Typography", [new("Camera", "W A S D / ↑ ↓ ← →"), new("Warning", "ÄÖÜ äöü ß / long localized label", true, true)], "F1 / ESC CLOSE") with { Kind = FrontendSurfaceKind.Controls },
                                ["pause"] = FrontendSurfaceView.PauseMenu([new("resume", "Resume", true, true)])
                            };
                            foreach (var (name, view) in views)
                            {
                                byte[] rgba = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => { c.BeginPass(GraphicsFramePass.Overlay); frontend.Render(c, view); });
                                ReferenceCaptureImage.WritePng(Path.Combine(output, $"{name}.png"), display.Item1, display.Item2, rgba);
                            }
                        }
                        platform.PumpEvents();
                    }
            }
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native graphics submission failed.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Device = graphics.Diagnostics.Device,
                WindowDpi = window.Dpi,
                AtlasResidentBytes = 2048 * 4096,
                DpiContract = "Requested HUD layout DPI sampling; this is not a physical monitor DPI certification.",
                TimingContract = "Capture readback timing includes GPU waits; it is not an isolated text GPU benchmark.",
                Cases = results
            }, s_json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"[typography:failure] {error.GetType().Name}: {error.Message}"); return 1; }
    }
}
