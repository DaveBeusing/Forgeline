using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Audio;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

/// <summary>Injected-time native sequence qualification, never a gameplay command source.</summary>
internal static class UiFeedbackCapture
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    internal static int Run(string output, string runtimeRoot)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("UI feedback capture output must be empty.");
            Directory.CreateDirectory(output);
            var assets = RuntimeAssetCatalog.Load(runtimeRoot);
            var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
            var scene = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain, runtimeAssets: assets);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE UI Feedback Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false });
            using var frontend = new FrontendOverlayRenderer(graphics);
            using var hud = new GameplayHudRenderer(graphics, assets);
            using var ground = new TerrainRenderer(graphics, terrain, runtimeAssets: assets,
                presentationProfile: TerrainPresentationProfile.CreateCentralDivide(assets)) { DebugChunksEnabled = false };
            using var meshes = new SimpleInstanceRenderer(graphics, assets);
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(scene.Target, RtsReferenceZoom.NormalGameplay));
            var results = new List<object>();
            foreach (var display in new[] { (1280, 720, 192u, 2f), (1920, 1080, 144u, 1f) })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                foreach (string surface in new[] { "Menu", "Panel", "Dock", "Accepted", "Rejected" })
                foreach (bool reduced in new[] { false, true })
                foreach (int rate in new[] { 30, 60, 144 })
                {
                    bool isDock = surface is "Dock" or "Accepted" or "Rejected";
                    var settings = new UiFeedbackSettings(ReducedMotion: reduced);
                    var geometry = FrontendDesign.ResolveLayout(display.Item1, display.Item2, display.Item4, display.Item3).Geometry;
                    var initial = FrontendSurfaceView.MainMenu([new("new", "NEW GAME", true, true), new("settings", "SETTINGS", true, false)])
                        with { Layout = geometry, FeedbackSettings = settings };
                    var changed = surface == "Panel" ? FrontendSurfaceView.Detail("SETTINGS", [new("INTERFACE SOUND", "OFF", IsFocused: true)], "ESC BACK", "APPLY")
                        with { Layout = geometry, FeedbackSettings = settings } : initial with
                        { MenuEntries = [new("new", "NEW GAME", true, false), new("settings", "SETTINGS", true, true)] };
                    var (snapshot, panel) = CommandDockCapture.Fixture(scene.Snapshot, PlayerActionPanelMode.Production,
                        surface is "Accepted" or "Rejected" ? surface : "Queue", display.Item1, display.Item2, display.Item3, display.Item4);
                    var baseline = CommandDockCapture.Fixture(scene.Snapshot, PlayerActionPanelMode.Production, "Queue",
                        display.Item1, display.Item2, display.Item3, display.Item4).Snapshot;
                    var published = new PresentationSnapshotBuffer(); published.Publish(snapshot);
                    var world = new RenderWorld(); world.Update(published);
                    if (isDock) ground.Prepare(camera, display.Item1, display.Item2);
                    string key = $"{surface}-{(reduced ? "Reduced" : "Motion")}-{rate}hz-{display.Item1}x{display.Item2}";
                    var cpu = new List<double>(); var gpu = new List<double>(); ulong fence = 0;
                    var images = new Dictionary<int, byte[]>();
                    int frames = (int)Math.Ceiling(rate / 3.0);
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        var feedback = new UiInteractionFeedback();
                        if (isDock) feedback.Gameplay(panel with { Mode = PlayerActionPanelMode.Construction, SelectedIndex = 0 },
                            baseline, TimeSpan.Zero, true, settings, RenderQuality.High);
                        else feedback.Frontend(initial, TimeSpan.Zero, true);
                        for (int i = 0; i <= frames; i++)
                        {
                            TimeSpan time = TimeSpan.FromSeconds((i + 1.0) / rate);
                            var animated = changed;
                            var animatedPanel = panel;
                            if (isDock) animatedPanel = feedback.Gameplay(panel, snapshot, time, true, settings, RenderQuality.High);
                            else animated = feedback.Frontend(changed, time, true);
                            void Record(IGraphicsCommandContext c)
                            {
                                if (isDock) { ground.Render(c, camera); meshes.Render(c, camera, world, 1); }
                                c.BeginPass(GraphicsFramePass.Overlay);
                                if (isDock) hud.Render(c, camera, snapshot, terrain.WorldBounds, RtsInformationLayerView.Empty,
                                    animatedPanel, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, display.Item3, display.Item4);
                                else frontend.Render(c, animated);
                            }
                            long start = Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                            if (repeat == 1 && i > 2)
                            {
                                cpu.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                                if (graphics.Diagnostics.Frame is { } frame && frame.GpuSubmissionFence > fence && graphics.Diagnostics.GpuFrameMilliseconds is { } ms)
                                { fence = frame.GpuSubmissionFence; gpu.Add(ms); }
                            }
                            if (i == 0 || i == Math.Max(1, rate / 15) || i == frames)
                            {
                                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                                if (repeat == 0) images.Add(i, pixels);
                                else if (!pixels.AsSpan().SequenceEqual(images[i])) throw new InvalidDataException("Motion sequence did not repeat: " + key);
                                if (repeat == 1 && rate == 60)
                                    ReferenceCaptureImage.WritePng(Path.Combine(output, $"{key}-{i}.png"), display.Item1, display.Item2, pixels);
                            }
                            var text = isDock ? hud.TextDiagnostics : frontend.TextDiagnostics;
                            if (!text.AtlasReady || text.MissingGlyphs != 0 || text.DroppedGlyphs != 0)
                                throw new InvalidDataException("Motion text is incomplete: " + key);
                            if (i == frames)
                            {
                                animatedPanel = panel with { Motion = null };
                                animated = changed with { Motion = null };
                                byte[] settled = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                                if (!settled.AsSpan().SequenceEqual(images[frames])) throw new InvalidDataException("Motion end differs from static state: " + key);
                            }
                            platform.PumpEvents();
                        }
                    }
                    if (!reduced && images[0].AsSpan().SequenceEqual(images[Math.Max(1, rate / 15)]))
                        throw new InvalidDataException("Expected visible decoration change: " + key);
                    if (reduced && !images[0].AsSpan().SequenceEqual(images[frames]))
                        throw new InvalidDataException("Reduced motion changed pixels: " + key);
                    cpu.Sort(); gpu.Sort();
                    results.Add(new { Surface = surface, ReducedMotion = reduced, InjectedRate = rate, Width = display.Item1, Height = display.Item2,
                        LayoutDpi = display.Item3, UiScale = display.Item4, Frames = frames + 1, Repeats = 2,
                        Samples = cpu.Count, SubmitP50Milliseconds = cpu[cpu.Count / 2], SubmitP95Milliseconds = cpu[(int)((cpu.Count - 1) * .95)],
                        GpuSamples = gpu.Count, GpuP50Milliseconds = gpu.Count == 0 ? (double?)null : gpu[gpu.Count / 2],
                        Images = images.Select(x => new { Frame = x.Key, PixelSha256 = Convert.ToHexString(SHA256.HashData(x.Value)) }) });
                    Console.WriteLine("[ui:feedback:capture] " + key);
                }
            }
            using var audio = new WaveOutUiVoice();
            var sound = new List<object>();
            foreach (var cue in new[] { UiAudioCue.Navigation, UiAudioCue.Intent, UiAudioCue.Accepted, UiAudioCue.Rejected })
            {
                bool played = audio.TryPlay(cue, .06f);
                bool busyDropped = played && !audio.TryPlay(cue, .06f);
                audio.StopPlayback();
                bool restarted = played && audio.TryPlay(cue, .06f);
                if (played && !restarted) throw new InvalidOperationException("UI voice did not restart after reset.");
                Thread.Sleep(140); audio.StopPlayback();
                sound.Add(new { Cue = cue.ToString(), Played = played, BusyDropped = busyDropped, RestartedAfterStop = restarted });
            }
            bool audioAvailable = audio.Available;
            uint? beforeIntro = audio.DeviceVolume;
            uint? duringIntro = null;
            uint? afterIntro = null;
            if (beforeIntro.HasValue)
            {
                using (var intro = new StudioSplashAudio())
                {
                    intro.Update(0, 0);
                    duringIntro = audio.DeviceVolume;
                }
                afterIntro = audio.DeviceVolume;
                if (afterIntro != beforeIntro) throw new InvalidOperationException("Studio fade left the shared audio gain changed.");
            }
            audio.Dispose();
            if (audio.OwnsNativeResources) throw new InvalidOperationException("Audio driver did not release its buffers.");
            graphics.WaitForIdle();
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission fault.");
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Source = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Device = graphics.Diagnostics.Device, WindowDpi = window.Dpi, Fixture = scene.Fingerprint,
                Contract = "Injected presentation clock, authored copied dock state and frozen battlefield; no requests dispatched. Native rates are sampling inputs, not sustained client FPS. Submit includes waits/Present. Short sequential cohorts are observational. Physical-monitor and human motion/sound acceptance remain separate.",
                Cases = results, Sound = sound, AudioAvailable = audioAvailable, AudioReleased = !audio.OwnsNativeResources,
                StudioVolumeBefore = beforeIntro, StudioVolumeDuringMute = duringIntro,
                StudioVolumeAfter = afterIntro, StudioMuteObserved = duringIntro == 0,
                StudioVolumeRestored = beforeIntro.HasValue && afterIntro == beforeIntro
            }, s_json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[ui:feedback:failure] " + error); return 1; }
    }
}
