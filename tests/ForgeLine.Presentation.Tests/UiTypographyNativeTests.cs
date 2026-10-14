using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class UiTypographyNativeTests
{
    [Fact]
    public void AtlasTextIsRepeatableBatchedAndSharedAcrossFrontendAndTextLayers()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Typography Test", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        using var frontend = new FrontendOverlayRenderer(graphics);
        using var text = new UiTextBatch(graphics);
        Assert.True(frontend.TextDiagnostics.AtlasReady); Assert.True(text.Ready);
        Assert.Equal(1, graphics.Diagnostics.Resources.LoadedTextureCount);
        var view = FrontendSurfaceView.Detail("Typography / ÄÖÜ äöü ß", [new("Regular / Bold", "AV 0123456789 % → ← ↑ ↓ …")], "F1 / ESC CLOSE");
        byte[] first = Capture(); Assert.Equal(first, Capture());
        Assert.InRange(frontend.LastRenderedVertexCount, 1, 12000);
        Assert.Equal(0, frontend.TextDiagnostics.MissingGlyphs); Assert.Equal(0, frontend.TextDiagnostics.DroppedGlyphs);
        Assert.Equal(6 * frontend.TextDiagnostics.Glyphs, frontend.TextDiagnostics.Vertices);
        Assert.Contains(first, x => x > 200);
        Assert.Equal(1, text.Diagnostics.MissingGlyphs);
        byte[] isolated = graphics.CaptureFrame(new GraphicsColor(0,0,0,1), c =>
        {
            text.Begin(); text.Add("AV ÄÖÜ äöü ß 0123456789",50,50,24,Vector4.One,c.Width,c.Height);
            text.Render(c);
        });
        int antialiased = 0, solid = 0;
        for (int i=0;i<isolated.Length;i+=4)
        {
            if (isolated[i] is >0 and <255) antialiased++;
            if (isolated[i]==255) solid++;
        }
        Assert.True(antialiased>100); Assert.True(solid>100);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        byte[] Capture() => graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c =>
        {
            frontend.Render(c, view);
            text.Begin(); text.Add("Diacritics: ÄÖÜ äöü ß  /  Greek: Ω  /  Missing: 😀", 50, 550, 24, Vector4.One, c.Width, c.Height);
            text.Render(c);
        });
    }
}
