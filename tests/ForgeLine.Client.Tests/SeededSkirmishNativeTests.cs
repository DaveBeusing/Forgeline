using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

[Collection("Native scene output")]
public sealed class SeededSkirmishNativeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [Fact]
    public void WarpRendersSeedEditingProgressErrorsAndFullFingerprintsRepeatably()
    {
        var package = GeneratedMapCompiler.Compile(new(), new string('a',64), TestContext.Current.CancellationToken);
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Seeded Skirmish Qualification",1280,720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new()
        { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        using var renderer = new FrontendOverlayRenderer(graphics);
        string? output = Environment.GetEnvironmentVariable("FORGELINE_SEEDED_CAPTURE_ROOT");
        if (output is not null) Directory.CreateDirectory(output);
        var reports = new List<object>();
        foreach (var display in new[] { (1280,720,1f,96f), (1280,720,2f,192f), (3440,1440,1f,96f) })
        {
            graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
            var layout = FrontendDesign.ResolveLayout(display.Item1, display.Item2, display.Item3, display.Item4);
            foreach (string state in new[] { "authored", "seed", "progress", "error", "ready", "hash" })
            {
                var model = new NewGameModel { VisibleCapacity = layout.Geometry.VisibleDetailRows };
                if (state != "authored") model.SelectMapKind(SkirmishMapKind.Generated);
                if (state == "seed") model.Generated.SetSeedText("0xffffffffffffffff");
                if (state == "progress") model.Generated.Begin();
                if (state == "error") model.Generated.SetSeedText("18446744073709551616");
                if (state is "ready" or "hash") model.Generated.Complete(new(1,package.Manifest.GameplaySha256,package.Manifest.VisualSha256));
                if (state == "hash") model.FocusRow(13);
                var view = FrontendPresentationAdapter.NewGame(model) with { Layout = layout.Geometry };
                void Draw(IGraphicsCommandContext c) { c.BeginPass(GraphicsFramePass.Overlay); renderer.Render(c,view); }
                for (int warmup=0;warmup<3;warmup++) graphics.RenderFrame(GraphicsColor.ForgeLineClear,Draw);
                byte[] pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw);
                Assert.Equal(pixels, graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw));
                Assert.Equal(0,renderer.TextDiagnostics.MissingGlyphs); Assert.Equal(0,renderer.TextDiagnostics.DroppedGlyphs);
                Assert.Equal(layout.Geometry,renderer.LastLayout);
                string name = FormattableString.Invariant($"{state}-{display.Item1}x{display.Item2}-{display.Item4}dpi-{display.Item3}.png");
                if (output is not null) ReferenceCaptureImage.WritePng(System.IO.Path.Combine(output,name),display.Item1,display.Item2,pixels);
                reports.Add(new { Image=name, State=state, Width=display.Item1, Height=display.Item2, Scale=display.Item3,
                    LayoutDpi=display.Item4, ActualWindowDpi=window.Dpi, Hash=Convert.ToHexStringLower(SHA256.HashData(pixels)), Missing=renderer.TextDiagnostics.MissingGlyphs,
                    Dropped=renderer.TextDiagnostics.DroppedGlyphs, Focused=model.FocusedRow, Offset=model.VisibleStart });
            }
        }
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        if(output is not null) File.WriteAllText(System.IO.Path.Combine(output,"qualification.json"),JsonSerializer.Serialize(new
        {
            Source=Environment.GetEnvironmentVariable("FORGELINE_QUALIFICATION_SOURCE"),
            Build=ClientBuildIdentity.InformationalVersion,
            package.ManifestSha256, package.Manifest.GameplaySha256,
            Evidence="WARP structural/readback qualification; synthetic host asset identity; physical playthrough remains separate", Cases=reports
        },JsonOptions));
    }
}
