using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Native injected-time round trips through the production input and presentation owner.</summary>
internal static class OnboardingCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Onboarding capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, runtimeAssets: assets);
            var snapshot = UiReferenceStates.Hud(scene.Snapshot, UiReferenceScenario.Onboarding, 1920, 1080, 96, 1).Snapshot;
            var guidance = new EarlyGameGuidanceController().Update(snapshot, true);
            using var platform = new WindowsPlatform();
            using var window = platform.CreateWindow(new("FORGELINE Onboarding Qualification", 1920, 1080));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false });
            using var hud = new GameplayHudRenderer(graphics, assets);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets);
            var publication = new PresentationSnapshotBuffer(); publication.Publish(snapshot);
            var world = new RenderWorld(); world.Update(publication);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target, RtsReferenceZoom.NormalGameplay));
            var records = new List<object>();
            foreach (var display in new[] { (1280, 720, 192u, 2f), (1920, 1080, 96u, 1f) })
            foreach (bool reduced in new[] { false, true })
            foreach (int hz in new[] { 30, 60, 144 })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                var layout = GameplayHudLayout.Create(display.Item1, display.Item2, display.Item3, display.Item4);
                var times = Enumerable.Range(0, (int)Math.Ceiling(.22 * hz) + 1).Select(i => Math.Min(220, i * 1000.0 / hz))
                    .Concat(new double[] { 0, 55, 110, 165, 220 }).Distinct().Order().ToArray();
                var pixels = new Dictionary<string, byte[]>();
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var owner = new GameplayGuidanceInteraction(); var input = new InputState();
                    owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.Zero, !reduced);
                    for (int direction = 0; direction < 2; direction++)
                    {
                        double start = 1 + direction * 230;
                        var hit = owner.View.Tab.IsEmpty ? owner.View.VisiblePanel : owner.View.Tab;
                        input.BeginFrame();
                        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
                            PlatformMouseButton.Left, (int)hit.X + 5, (int)hit.Y + 5));
                        owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.FromMilliseconds(start), !reduced);
                        if (!owner.BlocksGameplayThisFrame) throw new InvalidDataException("Native toggle did not retain input ownership.");
                        input.BeginFrame();
                        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, 1, 1));
                        foreach (double ms in times)
                        {
                            owner.Update(input, snapshot.SessionId, true, layout, TimeSpan.FromMilliseconds(start + ms), !reduced);
                            input.BeginFrame();
                            var ux = default(PreAlphaUxView) with { ShowOnboarding = true, Guidance = guidance, Onboarding = owner.View };
                            // Slowly traverse the frozen permitted fixture; never mutate simulation or dispatch a command.
                            camera.CenterOn(scene.Target + new Vector3((float)(direction * .22 + ms / 1000) * 4, 0, 0));
                            ground.Prepare(camera, display.Item1, display.Item2);
                            void Record(IGraphicsCommandContext c)
                            {
                                ground.Render(c, camera); meshes.Render(c, camera, world, 1);
                                c.BeginPass(GraphicsFramePass.Overlay);
                                hud.Render(c, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, ux, display.Item3, display.Item4);
                            }
                            graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            if (!hud.TextDiagnostics.AtlasReady || hud.TextDiagnostics.MissingGlyphs != 0 || hud.TextDiagnostics.DroppedGlyphs != 0 ||
                                hud.SkinDiagnostics.MissingAssets != 0 || hud.SkinDiagnostics.DroppedQuads != 0)
                                throw new InvalidDataException("Incomplete native onboarding content.");
                            if (ms is 0 or 55 or 110 or 165 or 220)
                            {
                                string key = $"{display.Item1}-{reduced}-{hz}-{direction}-{ms}";
                                byte[] image = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                                if (repeat == 0) pixels.Add(key, image);
                                else if (!image.AsSpan().SequenceEqual(pixels[key])) throw new InvalidDataException("Native onboarding sequence differs: " + key);
                                if (repeat == 1)
                                {
                                    if (hz == 60) ReferenceCaptureImage.WritePng(Path.Combine(output, key + ".png"), display.Item1, display.Item2, image);
                                    records.Add(new { Key = key, Milliseconds = ms, Direction = direction == 0 ? "Closing" : "Opening", InjectedHz = hz,
                                        ReducedMotion = reduced, State = owner.View, PixelSha256 = Convert.ToHexString(SHA256.HashData(image)),
                                        Text = hud.TextDiagnostics, Skin = hud.SkinDiagnostics });
                                }
                            }
                            platform.PumpEvents();
                        }
                        if (owner.View.Collapse != (direction == 0 ? 1 : 0)) throw new InvalidDataException("Native transition did not settle exactly.");
                    }
                }
                Console.WriteLine($"[ui:onboarding:capture] {display.Item1}x{display.Item2} reduced={reduced} samplingHz={hz}");
            }
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission fault.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Source = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Device = graphics.Diagnostics.Device, Fixture = scene.Fingerprint,
                Contract = "Injected presentation sampling through production controller; two identical native repeats over a slowly traversed frozen fixture. No gameplay commands. Rates are test inputs, not measured FPS. Human visual/motion acceptance is pending.",
                Cases = records, Health = graphics.Diagnostics.Health
            }, JsonOptions));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[ui:onboarding:failure] " + error); return 1; }
    }
}
