using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MinimapHoverMotionTests
{
    [Fact]
    public void HoverExpandsAndReversesWithContinuousSmoothStep()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        HudRect initial = motion.Update(layout, pointer, true, TimeSpan.Zero, true, true);
        HudRect midway = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(90), true, true);
        HudRect expanded = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(180), true, true);
        Assert.Equal(layout.Minimap, initial);
        Assert.True(midway.Width > initial.Width);
        Assert.True(midway.Width < expanded.Width);
        Assert.Equal(MinimapHoverPhase.Expanded, motion.Phase);
        Assert.InRange(expanded.Width / initial.Width, 1.34f, 1.36f);
        Assert.Equal(initial.Right, expanded.Right);
        Assert.Equal(initial.Bottom, expanded.Bottom);

        HudRect reversal = motion.Update(layout, Vector2.Zero, true, TimeSpan.FromMilliseconds(200), true, true);
        HudRect halfwayBack = motion.Update(layout, Vector2.Zero, true, TimeSpan.FromMilliseconds(290), true, true);
        HudRect collapsed = motion.Update(layout, Vector2.Zero, true, TimeSpan.FromMilliseconds(380), true, true);
        Assert.Equal(expanded, reversal);
        Assert.True(halfwayBack.Width < expanded.Width);
        Assert.True(halfwayBack.Width > initial.Width);
        Assert.Equal(initial, collapsed);
        Assert.Equal(MinimapHoverPhase.Idle, motion.Phase);
    }

    [Fact]
    public void ExpandedAreaRetainsHoverWhenPointerLeavesOriginalRectangle()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 center = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        motion.Update(layout, center, true, TimeSpan.Zero, true, true);
        HudRect expanded = motion.Update(layout, center, true, TimeSpan.FromMilliseconds(180), true, true);
        Vector2 expandedOnly = new(expanded.X + 10, expanded.Y + 10);
        Assert.False(layout.Minimap.Contains(expandedOnly));
        HudRect retained = motion.Update(layout, expandedOnly, true, TimeSpan.FromMilliseconds(200), true, true);
        Assert.Equal(expanded, retained);
        Assert.Equal(MinimapHoverPhase.Expanded, motion.Phase);
    }

    [Theory]
    [InlineData(1280, 720, 96u, .75f)]
    [InlineData(1920, 1080, 144u, 1f)]
    [InlineData(3840, 2160, 192u, 2f)]
    [InlineData(3440, 1440, 96u, 1f)]
    public void HoverBoundsStayInsideSafeAreaAndAvoidPersistentPanels(int width, int height, uint dpi, float uiScale)
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(width, height, dpi, uiScale);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        HudRect expanded = motion.Update(layout, pointer, true, TimeSpan.Zero, true, false);
        Assert.True(expanded.X >= layout.SafeArea.X - .01f);
        Assert.True(expanded.Y >= layout.SafeArea.Y - .01f);
        Assert.True(expanded.Right <= layout.SafeArea.Right + .01f);
        Assert.True(expanded.Bottom <= layout.SafeArea.Bottom + .01f);
        Assert.False(expanded.Intersects(layout.TopStatusBar));
        Assert.False(expanded.Intersects(layout.AlertStack));
        Assert.False(expanded.Intersects(layout.SecondaryView));
    }

    [Fact]
    public void ReducedMotionSettlesAndDisplayOrClockChangeDiscardsStaleTransition()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        HudRect immediate = motion.Update(layout, pointer, true, TimeSpan.Zero, true, false);
        Assert.True(immediate.Width > layout.Minimap.Width);
        Assert.Equal(MinimapHoverPhase.Expanded, motion.Phase);
        GameplayHudLayout changed = GameplayHudLayout.Create(1600, 900, 144);
        HudRect reset = motion.Update(changed, Vector2.Zero, false, TimeSpan.FromMilliseconds(20), true, true);
        Assert.Equal(changed.Minimap, reset);
        Assert.Equal(MinimapHoverPhase.Idle, motion.Phase);
        motion.Update(changed, Center(RtsMinimapInteractionLayout.GetMapRect(changed)), true,
            TimeSpan.FromMilliseconds(200), true, true);
        HudRect rollback = motion.Update(changed, Vector2.Zero, false, TimeSpan.FromMilliseconds(100), true, true);
        Assert.Equal(changed.Minimap, rollback);
    }

    [Fact]
    public void AnimatedMapProjectionUsesTheSameGeometryInBothDirections()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        motion.Update(layout, pointer, true, TimeSpan.Zero, true, true);
        HudRect midway = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(90), true, true);
        var animatedLayout = layout with { Minimap = midway };
        var worldBounds = new AxisAlignedBounds(new Vector3(-256, -20, 128), new Vector3(768, 80, 1152));
        var worldPosition = new Vector3(384, 0, 896);
        Vector2 projected = RtsMinimapInteractionLayout.MapWorldToPointer(worldPosition, animatedLayout, worldBounds);
        Assert.True(RtsMinimapInteractionLayout.TryMapPointerToWorld(projected, animatedLayout, worldBounds, out Vector3 restored));
        Assert.InRange(Vector3.Distance(worldPosition, restored), 0, .02f);
        Assert.True(RtsMinimapInteractionLayout.IsInteractiveSurface(projected, animatedLayout));
        Vector2 gap = new(animatedLayout.Minimap.X + 1, RtsMinimapInteractionLayout.GetSelectorRect(animatedLayout).Bottom + 1);
        Assert.False(RtsMinimapInteractionLayout.IsInteractiveSurface(gap, animatedLayout));
    }

    private static Vector2 Center(in HudRect bounds) =>
        new(bounds.X + bounds.Width * .5f, bounds.Y + bounds.Height * .5f);
}
