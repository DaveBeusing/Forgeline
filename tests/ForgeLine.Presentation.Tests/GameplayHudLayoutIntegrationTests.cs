using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayHudLayoutIntegrationTests
{
    [Fact]
    public void ActionPanelOriginUsesSharedHudLayout()
    {
        const int width = 1920;
        const int height = 1080;
        const uint dpi = 144;
        const float uiScale = 1.25f;

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                width,
                height,
                dpi,
                uiScale);
        var controller =
            new PlayerActionPanelController();

        PlayerActionPanelView view =
            controller.CreateView(
                width,
                height,
                actions: null,
                dpi,
                uiScale);

        Assert.Equal(
            layout.ActionDock.X,
            view.OriginX);
        Assert.Equal(
            layout.ActionDock.Y,
            view.OriginY);
    }
}
