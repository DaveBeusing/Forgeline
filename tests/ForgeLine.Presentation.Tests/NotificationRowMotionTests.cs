using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class NotificationRowMotionTests
{
    private static NotificationStackLayout One(HudRect area, ulong revision = 1)
    {
        var text = new HudRect(area.X + 42, area.Y + 6, area.Width - 140, 24);
        var action = new HudRect(area.Right - 90, area.Y + 6, 82, 24);
        return new(area, 14, 1, new(new(PlayerAlertState.LowPower), area, text, action, default, false, revision));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void NewlyVisibleMessageEasesWithin180MillisecondsAtInjectedRates(int rate)
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var area = layout.NotificationBounds() with { Height = 36 };
        var motion = new NotificationSurfaceMotion();
        var start = TimeSpan.FromSeconds(1);
        motion.Update(default, new(1), layout, start, true);
        var shown = One(area);
        motion.Update(shown, new(1), layout, start + TimeSpan.FromMilliseconds(16), true);
        Assert.Equal(0, motion.Opacity(0));
        float previous = 0;
        for (int n = 1; n * 1000d / rate < 180; n++)
        {
            motion.Update(shown, new(1), layout,
                start + TimeSpan.FromMilliseconds(16) + TimeSpan.FromSeconds(n / (double)rate), true);
            Assert.InRange(motion.Opacity(0), previous, 1);
            previous = motion.Opacity(0);
        }
        motion.Update(shown, new(1), layout, start + TimeSpan.FromMilliseconds(196), true);
        Assert.Equal(1, motion.Opacity(0));
    }

    [Fact]
    public void ResolvedMessageHasNoHitTargetDuringDecorativeFade()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var area = layout.NotificationBounds() with { Height = 36 };
        var first = One(area);
        var motion = new NotificationSurfaceMotion();
        var start = TimeSpan.FromSeconds(1);
        motion.Update(first, new(1), layout, start, true);
        var empty = new NotificationStackLayout(area, 14, 0);
        motion.Update(empty, new(1), layout, start + TimeSpan.FromMilliseconds(16), true);
        Assert.False(empty.Contains(new Vector2(area.X + 5, area.Y + 5)));
        Assert.Equal(1, motion.RetiredCount);
        motion.Update(empty, new(1), layout, start + TimeSpan.FromMilliseconds(106), true);
        Assert.InRange(motion.Retired(0).Opacity, .49f, .51f);
        motion.Update(empty, new(1), layout, start + TimeSpan.FromMilliseconds(196), true);
        Assert.Equal(0, motion.RetiredCount);
    }

    [Fact]
    public void IdentityRevisionSessionClockAndReducedMotionDoNotRetainStaleVisuals()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var area = layout.NotificationBounds() with { Height = 36 };
        var shown = One(area);
        var motion = new NotificationSurfaceMotion();
        var t = TimeSpan.FromSeconds(1);
        motion.Update(shown, new(1), layout, t, true);
        motion.Update(One(area, 2), new(1), layout, t + TimeSpan.FromMilliseconds(16), true);
        Assert.Equal(0, motion.Opacity(0));
        motion.Update(shown, new(2), layout, t + TimeSpan.FromMilliseconds(32), true);
        Assert.Equal(1, motion.Opacity(0));
        Assert.Equal(0, motion.RetiredCount);
        motion.Update(shown, new(2), layout with { Scale = layout.Scale + .25f },
            t + TimeSpan.FromMilliseconds(48), true);
        Assert.Equal(1, motion.Opacity(0));
        motion.Update(default, new(2), layout, t + TimeSpan.FromMilliseconds(64), false);
        Assert.Equal(0, motion.RetiredCount);
        motion.Update(shown, new(2), layout, t + TimeSpan.FromMilliseconds(300), true);
        Assert.Equal(1, motion.Opacity(0));
        motion.Update(shown, new(2), layout, t + TimeSpan.FromMilliseconds(250), true);
        Assert.Equal(1, motion.Opacity(0));
    }

    [Fact]
    public void NarrowAlertFallsBackToOneMoreControlWithoutHiddenOverlappingButtons()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        layout = layout with { AlertStack = layout.AlertStack with { Width = 64 } };
        var experience = default(PlayerExperienceSnapshot) with
        {
            Tick = new(1), Player = new(1), Alerts = PlayerAlertState.SupplyCritical
        };
        var stack = ActionableAlertLayout.Resolve(experience, layout);
        Assert.True(stack.Count > 0);
        var row = stack.First;
        Assert.True(row.Truncated);
        Assert.True(row.Text.Right <= row.Action.X);
        Assert.Equal(row.Action, row.Details);
        Assert.True(row.Action.Right <= row.Bounds.Right);
    }

    [Theory]
    [InlineData(1280, 720, 96u, .75f)]
    [InlineData(1920, 1080, 144u, 1f)]
    [InlineData(3440, 1440, 192u, 2f)]
    public void NotificationContentAndButtonsShareVerticalMidline(int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var experience = default(PlayerExperienceSnapshot) with
        {
            Tick = new(1),
            Player = new(1),
            Alerts = PlayerAlertState.LowPower | PlayerAlertState.SupplyCritical
        };
        var stack = ActionableAlertLayout.Resolve(experience, layout,
            localFeedback: "Placement preview pending because the destination is outside the established industrial footprint");
        for (int i = 0; i < stack.Count; i++)
        {
            var card = stack.Card(i);
            float center = card.Bounds.Y + card.Bounds.Height * .5f;
            Assert.InRange(MathF.Abs(card.Text.Y + card.Text.Height * .5f - center), 0, .001f);
            if (!card.Action.IsEmpty)
            {
                Assert.InRange(MathF.Abs(card.Action.Y + card.Action.Height * .5f - center), 0, .001f);
                Assert.True(card.Text.Right <= card.Action.X);
            }
            if (!card.Details.IsEmpty && card.Item.Hidden == 0)
            {
                Assert.InRange(MathF.Abs(card.Details.Y + card.Details.Height * .5f - center), 0, .001f);
                Assert.True(card.Text.Right <= card.Details.X);
                Assert.True(card.Details.Right <= card.Action.X);
            }
        }
        Assert.Equal(.30f, NotificationSurfaceVisuals.BackgroundOpacity);
    }
}
