using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResponsiveHudLayoutTests
{
    [Fact]
    public void RightColumnAndReservationsSurviveEveryDisplayAndDockState()
    {
        foreach (var size in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (uint dpi in new uint[] { 96, 144, 192 })
        foreach (float scale in new[] { .75f, 1f, 2f })
        {
            var layout = GameplayHudLayout.Create(size.Item1, size.Item2, dpi, scale);
            Assert.Equal(layout.SafeArea.Right, layout.TopStatusBar.Right, 3);
            Assert.True(layout.TopStatusBar.Width <= 2 * layout.ActionDock.Width);
            Assert.Equal(layout.SafeArea.Y, layout.TopStatusBar.Y);
            Assert.Equal(layout.TopStatusBar.Bottom + layout.Gap, layout.ActionDock.Y, 3);
            Assert.Equal(layout.SafeArea.X, layout.OnboardingBounds.X);
            Assert.Equal(layout.SafeArea.Y, layout.OnboardingBounds.Y);
            Assert.False(layout.OnboardingBounds.Intersects(layout.TopStatusBar));
            foreach (bool expanded in new[] { false, true })
            {
                var dock = layout.VisibleActionDock(expanded);
                var notifications = layout.NotificationBounds(expanded);
                Assert.Equal(layout.SafeArea.Right, dock.Right, 3);
                Assert.Equal(layout.SafeArea.Right, notifications.Right, 3);
                Assert.Equal(dock.Bottom + layout.Gap, notifications.Y, 3);
                Assert.InRange(notifications.Width, 0, dock.Width);
                Assert.False(notifications.IsEmpty);
                Assert.False(notifications.Intersects(dock));
                Assert.False(notifications.Intersects(layout.Minimap));
                Assert.True(notifications.Bottom <= layout.SafeArea.Bottom);
                var pointer = new Vector2(dock.X + dock.Width / 2, dock.Y + dock.Height / 2);
                Assert.True(HudInteractionContext.BlocksWorldPointer(pointer, layout, true, expanded));
                if (!expanded)
                {
                    Assert.True(dock.Height < layout.ActionDock.Height);
                    Assert.False(HudInteractionContext.BlocksWorldPointer(new(dock.X + 1, dock.Bottom + 1),
                        layout, true, false));
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(1, 1)]
    [InlineData(200, 160)]
    public void UnusableClientsExposeNoInvisibleInputControls(int width, int height)
    {
        var layout = GameplayHudLayout.Create(width, height, 192, 2);
        Assert.True(layout.SafeArea.IsEmpty);
        Assert.True(layout.TopStatusBar.IsEmpty);
        Assert.True(layout.VisibleActionDock(false).IsEmpty);
        Assert.True(layout.NotificationBounds(true).IsEmpty);
        Assert.True(layout.OnboardingBounds.IsEmpty);
        Assert.False(PlayerActionDockInteractionLayout.TryHit(Vector2.Zero, layout, false, 8, out _));
        Assert.False(HudInteractionContext.BlocksWorldPointer(Vector2.Zero, layout, true));
    }
}
