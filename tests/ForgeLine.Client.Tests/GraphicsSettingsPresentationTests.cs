using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

[CollectionDefinition("Native graphics settings", DisableParallelization = true)]
public sealed class NativeGraphicsSettingsGroup;

[Collection("Native graphics settings")]
public sealed class GraphicsSettingsPresentationTests
{
    [Fact]
    public void GraphicsRowsRenderAtNativeOutputWithStablePixels()
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Graphics Settings", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        using var renderer = new FrontendOverlayRenderer(graphics);
        var model = new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()));
        model.SetRenderQuality(FrontendRenderQuality.Ultra);
        var interaction = new SettingsInteractionModel();
        interaction.Focus(FrontendSettingsField.ResolutionScale);
        interaction.Adjust(model, 1);
        Assert.Equal(.5f, model.GraphicsSettings.ResolutionScale);
        var surface = FrontendPresentationAdapter.Settings(model, interaction);
        byte[] image = Capture();
        Assert.Equal(image, Capture());
        Assert.True(renderer.TextDiagnostics.AtlasReady);
        Assert.Equal(0, renderer.TextDiagnostics.MissingGlyphs);
        Assert.Equal(6 * renderer.TextDiagnostics.Glyphs, renderer.TextDiagnostics.Vertices);
        Assert.InRange(renderer.LastRenderedVertexCount, 1_000, 8_000);
        string? root = Environment.GetEnvironmentVariable("FORGELINE_SETTINGS_CAPTURE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            Directory.CreateDirectory(root);
            ReferenceCaptureImage.WritePng(Path.Combine(root, "graphics-settings.png"), 1280, 720, image);
        }
        byte[] Capture() => graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.BeginPass(GraphicsFramePass.Overlay);
            renderer.Render(context, surface);
        });
    }
}
