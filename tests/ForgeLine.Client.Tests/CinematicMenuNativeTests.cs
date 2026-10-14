using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class CinematicMenuNativeTests
{
    [Fact]
    public void MenuPresentsBeforeOptionalArtAndReusesOneImageAcrossSecondaryScreens()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "assets", "runtime", "manifest.json"))) root = root.Parent;
        Assert.NotNull(root);
        var catalog = RuntimeAssetCatalog.Load(Path.Combine(root.FullName, "assets", "runtime"));
        Assert.True(catalog.Contains(AssetId.Parse(FrontendMenuBackground.AssetName)));
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Cinematic Menu Test", 1280, 720));
        for (int restart = 0; restart < 2; restart++)
        {
            var target = new GraphicsWindowTarget(window.NativeHandle.Value, 1280, 720, false);
            using var host = new ClientFrontendRenderHost(target);
            host.SetMenuAssets(catalog);
            var view = FrontendSurfaceView.MainMenu([new("new-game", "NEW GAME", true, true)]) with
            { Layout = FrontendGeometry.Resolve(1280, 720, 1, window.Dpi), BackgroundQuality = RenderQuality.High };
            host.Publish(view);
            Assert.True(host.WaitForLatestFrame(TimeSpan.FromSeconds(10)));
            Assert.True(host.HasPresentedFrame);
            Assert.True(SpinWait.SpinUntil(() => { host.ThrowIfFaulted(); return host.MenuImageDiagnostics.State == "Ready"; }, TimeSpan.FromSeconds(15)));
            int uploads = host.MenuImageDiagnostics.Uploads;
            long initialBytes = host.MenuImageDiagnostics.ResidentBytes;
            Assert.InRange(uploads, 1, 1);
            Assert.InRange(host.MenuImageDiagnostics.ResidentBytes, 1, FrontendMenuBackground.MaximumPayloadBytes);
            host.Publish(FrontendSurfaceView.Detail("SETTINGS", [], "ESC BACK") with { Layout = view.Layout });
            Assert.True(host.WaitForLatestFrame(TimeSpan.FromSeconds(5)));
            host.Publish(view);
            Assert.True(host.WaitForLatestFrame(TimeSpan.FromSeconds(5)));
            Assert.Equal(uploads, host.MenuImageDiagnostics.Uploads);
            host.UpdateSurface(1600, 900, false);
            host.Publish(view with { Layout = FrontendGeometry.Resolve(1600, 900, 1, window.Dpi), BackgroundQuality = RenderQuality.Low });
            Assert.True(host.WaitForLatestFrame(TimeSpan.FromSeconds(5)));
            if (initialBytes > 1024 * 1024)
                Assert.True(SpinWait.SpinUntil(() => host.MenuImageDiagnostics.Uploads > uploads, TimeSpan.FromSeconds(5)));
            else Assert.Equal(uploads, host.MenuImageDiagnostics.Uploads); // Software already selected the reduced mip.
            host.ThrowIfFaulted();
        }
    }
}
