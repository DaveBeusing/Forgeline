using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class BattlefieldStatusNativeTests
{
    [Theory]
    [InlineData(120, 96u, 1f)]
    [InlineData(1000, 192u, 2f)]
    public void NativeStatusIsRepeatableAndHiddenOrStaleFactsDoNotPaint(float distance, uint dpi, float scale)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE battlefield status", 1600, 900));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        using var renderer = new RtsInformationOverlayRenderer(graphics);
        var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero, InitialDistance = distance });
        var view = RtsInformationLayerView.Empty with { MinimapEnabled = false, OverlayMode = StrategicOverlayMode.Supply };
        var clear = Capture(BattlefieldStatusOverlayTests.Snapshot(1, enemy: true));
        Assert.Equal(0, renderer.LastBattlefieldStatusMarkers);
        var after = Capture(BattlefieldStatusOverlayTests.Snapshot(1));
        Assert.Equal(1, renderer.LastBattlefieldStatusMarkers);
        Assert.False(clear.AsSpan().SequenceEqual(after));
        Assert.True(after.AsSpan().SequenceEqual(Capture(BattlefieldStatusOverlayTests.Snapshot(1))));
        Assert.True(clear.AsSpan().SequenceEqual(Capture(BattlefieldStatusOverlayTests.Snapshot(1, stale: true))));
        Assert.True(renderer.TextDiagnostics.AtlasReady);
        Assert.Equal(0, renderer.TextDiagnostics.MissingGlyphs);
        Assert.Equal(0, renderer.TextDiagnostics.DroppedGlyphs);

        if (Environment.GetEnvironmentVariable("FORGELINE_BATTLEFIELD_STATUS_CAPTURE") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, $"before-{distance:0}.rgba"), clear);
            File.WriteAllBytes(Path.Combine(output, $"after-{distance:0}.rgba"), after);
        }

        byte[] Capture(PresentationSnapshot source) => graphics.CaptureFrame(new(.025f, .03f, .04f, 1), context =>
        {
            context.BeginPass(GraphicsFramePass.Overlay);
            renderer.Render(context, camera, source, new AxisAlignedBounds(new(-1000), new(1000)),
                view, CombatGroupOverviewView.Empty, dpi, scale);
        });
    }
}
