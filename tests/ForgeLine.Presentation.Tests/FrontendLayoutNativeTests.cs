using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class FrontendLayoutNativeTests
{
    [Fact]
    public void WarpRendersPublishedDpiGeometryRepeatablyAcrossResizeAndRestore()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Layout Test", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        using var renderer = new FrontendOverlayRenderer(graphics);
        foreach (var display in new[] { (1280, 720), (1600, 900), (1280, 720) })
        {
            graphics.WaitForIdle(); graphics.Resize(display.Item1, display.Item2);
            var geometry = FrontendGeometry.Resolve(display.Item1, display.Item2, 2, 192);
            var view = FrontendSurfaceView.Detail("SETTINGS", [new("UI SCALE", "2.00", IsFocused: true, CanDecrease: true)],
                "BINDING CONFLICT - CHOOSE ANOTHER KEY", secondaryAction: "BACK") with { Layout = geometry };
            void Draw(IGraphicsCommandContext c) { c.BeginPass(GraphicsFramePass.Overlay); renderer.Render(c, view); }
            for (int i = 0; i < 3; i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear, Draw);
            var first = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw);
            Assert.Equal(first, graphics.CaptureFrame(GraphicsColor.ForgeLineClear, Draw));
            Assert.Equal(geometry, renderer.LastLayout);
            Assert.True(renderer.TextDiagnostics.AtlasReady);
            Assert.Equal(0, renderer.TextDiagnostics.DroppedGlyphs);
            Assert.Equal(0, renderer.TextDiagnostics.MissingGlyphs);
        }
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
    }
}
