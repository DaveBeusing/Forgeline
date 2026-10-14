using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class FrontendLayoutCapture
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    internal static int Run(string output, string? runtimeRoot = null)
    {
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Layout capture directory must be empty.");
            Directory.CreateDirectory(output);
            using var platform = new WindowsPlatform();
            using IWindow window = platform.CreateWindow(new("FORGELINE Frontend Layout Qualification", 1280, 720));
            using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { EnableVSync = false });
            using var renderer = new FrontendOverlayRenderer(graphics);
            double? backgroundLoadMilliseconds = null;
            if (runtimeRoot is not null)
            {
                var catalog = ForgeLine.Assets.RuntimeAssetCatalog.Load(runtimeRoot);
                var wait = System.Diagnostics.Stopwatch.StartNew();
                do { renderer.PrepareBackground(catalog, RenderQuality.High); Thread.Sleep(1); }
                while (renderer.BackgroundDiagnostics.State == "Loading" && wait.Elapsed < TimeSpan.FromSeconds(15));
                if (renderer.BackgroundDiagnostics.State != "Ready") throw new InvalidDataException("Cinematic art did not become ready.");
                backgroundLoadMilliseconds = wait.Elapsed.TotalMilliseconds;
            }
            var results = new List<object>();
            var menu = new MainMenuModel(false);
            var saves = new LoadGameModel(Enumerable.Range(0, 50).Select(i => new LoadGameEntry($"s{i}",
                $"SAVE {i} / ÄÖÜ äöü ß / " + new string('W', 100), $"s{i}.json", (ulong)i, LoadGameEntryState.Available)));
            saves.Focus(49);
            foreach (var display in new[] { (1280, 720), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440), (3440, 1440), (3840, 2160) })
            {
                graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
                foreach (uint dpi in new uint[] { 96, 144, 192 })
                foreach (float scale in new[] { .75f, 1f, 2f })
                {
                    var layout = FrontendDesign.ResolveLayout(display.Item1, display.Item2, scale, dpi);
                    var interaction = new SettingsInteractionModel { VisibleCapacity = layout.Geometry.VisibleDetailRows };
                    interaction.Focus(FrontendSettingsField.BindingKey);
                    var settings = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
                    for (int key = 0; key < 128 && interaction.CanApply; key++) interaction.Adjust(settings, 1);
                    if (interaction.CanApply) throw new InvalidDataException("Expected a conflicting binding fixture.");
                    var views = new Dictionary<string, FrontendSurfaceView>
                    {
                        ["menu"] = FrontendPresentationAdapter.MainMenu(menu),
                        ["new-game"] = FrontendPresentationAdapter.NewGame(new NewGameModel()),
                        ["load"] = FrontendPresentationAdapter.LoadGame(saves, layout.Geometry.VisibleDetailRows),
                        ["settings"] = FrontendPresentationAdapter.Settings(settings, interaction),
                        ["controls"] = FrontendPresentationAdapter.Controls(new ForgeLine.Input.RtsCameraBindings()),
                        ["loading"] = FrontendSurfaceView.Loading("LOADING SAVED MATCH / 75%", true, .75f),
                        ["pause"] = FrontendPresentationAdapter.PauseMenu(new PauseMenuModel())
                    };
                    foreach (var (name, source) in views)
                    {
                        var geometry = source.Kind == FrontendSurfaceKind.Controls
                            ? FrontendGeometry.Resolve(display.Item1, display.Item2, scale, dpi, true) : layout.Geometry;
                        int scroll = source.Kind == FrontendSurfaceKind.Controls ? Math.Max(0, source.DetailLines.Count - geometry.VisibleDetailRows) : 0;
                        var view = source with { Layout = geometry, DetailScroll = scroll };
                        void Record(IGraphicsCommandContext c) { c.BeginPass(GraphicsFramePass.Overlay); renderer.Render(c, view); }
                        for (int warmup = 0; warmup < 3; warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Record);
                        byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                        byte[] repeated = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Record);
                        if (!pixels.AsSpan().SequenceEqual(repeated) || renderer.LastLayout != geometry)
                            throw new InvalidDataException("Layout capture or shared geometry is not repeatable.");
                        if (!renderer.TextDiagnostics.AtlasReady || renderer.TextDiagnostics.DroppedGlyphs != 0 || renderer.TextDiagnostics.MissingGlyphs != 0)
                            throw new InvalidDataException("Frontend text is incomplete.");
                        string? image = null;
                        if ((dpi == 96 && scale == 1 && name is "menu" or "controls") ||
                            (display == (1280, 720) && dpi == 192 && scale == 2 && name is "settings" or "controls"))
                        {
                            image = FormattableString.Invariant($"{name}-{display.Item1}x{display.Item2}-{dpi}dpi-{scale:0.00}.png");
                            ReferenceCaptureImage.WritePng(Path.Combine(output, image), display.Item1, display.Item2, pixels);
                        }
                        results.Add(new { Screen = name, Width = display.Item1, Height = display.Item2, Dpi = dpi, UiScale = scale,
                            VisibleRows = geometry.VisibleDetailRows, Scroll = scroll, GeometryScale = geometry.Scale,
                            Repeatable = true, SharedGeometry = true, Vertices = renderer.LastRenderedVertexCount,
                            Font = renderer.TextDiagnostics, PixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)), Image = image });
                    }
                    platform.PumpEvents();
                }
            }
            var modes = new List<object>();
            foreach (WindowMode mode in new[] { WindowMode.Windowed, WindowMode.BorderlessFullscreen })
            {
                window.SetMode(mode); platform.PumpEvents(); graphics.WaitForIdle();
                var size = window.ClientSize; graphics.Resize(size.Width, size.Height);
                var geometry = FrontendGeometry.Resolve(size.Width, size.Height, 1, window.Dpi);
                var view = FrontendPresentationAdapter.MainMenu(menu) with { Layout = geometry };
                for (int warmup = 0; warmup < 3; warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, view));
                graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, view));
                if (renderer.LastLayout != geometry) throw new InvalidDataException("Window geometry mismatch.");
                modes.Add(new { Mode = mode.ToString(), size.Width, size.Height, Dpi = window.Dpi, geometry.Scale });
            }
            graphics.WaitForIdle(); graphics.Resize(0, 0); graphics.RenderFrame(GraphicsColor.ForgeLineClear); graphics.Resize(1280, 720);
            var restored = FrontendPresentationAdapter.MainMenu(menu) with { Layout = FrontendGeometry.Resolve(1280, 720) };
            for (int warmup = 0; warmup < 3; warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, restored));
            graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c => renderer.Render(c, restored));
            if (graphics.Diagnostics.Health.SubmissionFaultCount != 0) throw new InvalidDataException("Native submission failed.");
            object? comparison = null;
            if (runtimeRoot is not null)
            {
                graphics.WaitForIdle(); graphics.Resize(1920, 1080);
                using var fallback = new FrontendOverlayRenderer(graphics);
                var sample = FrontendPresentationAdapter.MainMenu(menu) with { Layout = FrontendGeometry.Resolve(1920, 1080) };
                object Measure(FrontendOverlayRenderer target)
                {
                    void Draw(IGraphicsCommandContext c) { c.BeginPass(GraphicsFramePass.Overlay); target.Render(c, sample); }
                    for (int i = 0; i < 16; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
                    var cpu = new List<double>(); var gpu = new List<double>(); ulong fence = 0;
                    for (int i = 0; i < 64; i++)
                    {
                        long start = System.Diagnostics.Stopwatch.GetTimestamp(); graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
                        cpu.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                        if (graphics.Diagnostics.Frame is { } frame && frame.GpuSubmissionFence > fence && graphics.Diagnostics.GpuFrameMilliseconds is { } time)
                        { fence = frame.GpuSubmissionFence; gpu.Add(time); }
                    }
                    cpu.Sort(); gpu.Sort();
                    return new { Vertices = target.LastRenderedVertexCount, CpuSubmitP50Microseconds = cpu[cpu.Count/2], GpuSamples = gpu.Count,
                        GpuFrameP50Milliseconds = gpu.Count == 0 ? (double?)null : gpu[gpu.Count/2] };
                }
                comparison = new { Fallback = Measure(fallback), Cinematic = Measure(renderer),
                    Contract = "Same-device 1080p complete clear/menu/image/text GPU frame; no world. CPU submit includes waits/present. Not an isolated image shader benchmark." };
            }
            File.WriteAllText(Path.Combine(output, "qualification.json"), JsonSerializer.Serialize(new
            {
                Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Device = graphics.Diagnostics.Device, MatrixDpiContract = "Requested physical-pixel layout DPI; actual window DPI recorded separately. No physical multi-monitor certification.",
                Background = renderer.BackgroundDiagnostics, BackgroundLoadMilliseconds = backgroundLoadMilliseconds, NativeComparison = comparison,
                Cases = results, WindowModes = modes, ZeroSizeRestore = true, SubmissionFaults = graphics.Diagnostics.Health.SubmissionFaultCount
            }, s_json));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"[frontend-layout:failure] {error}"); return 1; }
    }
}
