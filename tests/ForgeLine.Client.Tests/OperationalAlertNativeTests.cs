using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

[Collection("Native scene output")]
public sealed class OperationalAlertNativeTests
{
    [Fact]
    public void QuietModePreservesCriticalPixelsAndBoundedOverflowUnderConcurrentOutages()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "ForgeLine.sln"))) root = Directory.GetParent(root)!.FullName;
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE operational alert qualification", 1280, 720));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        using var renderer = new ResourcePowerHudRenderer(graphics, RuntimeAssetCatalog.Load(Path.Combine(root, "assets/runtime")));
        var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(4),
            MatchStatus = PlayerMatchStatus.Active, Alerts = (PlayerAlertState)239,
            Power = new(new(1), 0, 100, 0, 100, 0, 1), CriticalSupplyUnits = 12, BlockedProductionFacilities = 40 };
        var layout = GameplayHudLayout.Create(1280, 720, 96);
        var tracker = new AlertLifecycleTracker();
        var allAlerts = tracker.Capture(new(1), experience, []);
        var all = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1), playerExperience: experience, alerts: allAlerts);
        var quietAlerts = tracker.Capture(new(1), experience, [], preferences: new() { Mode = NotificationMode.Quiet });
        var quiet = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1), playerExperience: experience, alerts: quietAlerts);
        var stack = ActionableAlertLayout.Resolve(all, layout);
        Assert.Equal(PlayerAlertState.CommandCoreUnderAttack, stack.Card(0).Item.Kind);
        Assert.True(stack.Card(3).Item.Hidden > 0);
        Assert.Equal(3, ActionableAlertLayout.Resolve(quiet, layout).Count);
        byte[] first = Draw(all), repeat = Draw(all), reduced = Draw(quiet);
        Assert.Equal(first, repeat);
        var critical = stack.Card(0).Bounds;
        for (int y = (int)critical.Y; y < (int)critical.Bottom; y++)
            Assert.Equal(first.AsSpan((y * 1280 + (int)critical.X) * 4, (int)critical.Width * 4).ToArray(),
                reduced.AsSpan((y * 1280 + (int)critical.X) * 4, (int)critical.Width * 4).ToArray());
        Assert.NotEqual(first, reduced);
        string? output = Environment.GetEnvironmentVariable("FORGELINE_OPERATIONAL_ALERT_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            ReferenceCaptureImage.WritePng(Path.Combine(output, "All.png"), 1280, 720, first);
            ReferenceCaptureImage.WritePng(Path.Combine(output, "Quiet.png"), 1280, 720, reduced);
        }
        byte[] Draw(PresentationSnapshot snapshot)
        {
            var pixels = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context => renderer.Render(context, snapshot, layout));
            Assert.Equal(0, renderer.TextDiagnostics.MissingGlyphs); Assert.Equal(0, renderer.TextDiagnostics.DroppedGlyphs);
            Assert.Equal(0, renderer.SkinDiagnostics.MissingAssets); Assert.Equal(0, renderer.SkinDiagnostics.DroppedQuads);
            return pixels;
        }
    }
}
