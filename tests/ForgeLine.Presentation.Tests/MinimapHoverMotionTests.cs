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
        Assert.InRange(expanded.Width / initial.Width, 1.49f, 1.51f);
        Assert.Equal(MinimapHoverMotion.DesiredScale, motion.MaximumScale);
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

    [Fact]
    public void RingOccupancyCapsExpansionWithoutCoveringControls()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        HudRect ring = new(layout.Minimap.X - 70, layout.Minimap.Y - 70, 50, 50);
        HudRect bounded = motion.Update(layout, pointer, true, TimeSpan.Zero, true, false, ringBounds: ring);
        Assert.True(bounded.Width >= layout.Minimap.Width);
        Assert.True(bounded.Width < layout.Minimap.Width * MinimapHoverMotion.DesiredScale);
        Assert.False(bounded.Intersects(ring));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void ExpansionRemainsMonotonicAcrossRenderRates(int framesPerSecond)
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1920, 1080, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        HudRect previous = motion.Update(layout, pointer, true, TimeSpan.Zero, true, true);

        for (int frame = 1; frame * 1000.0 / framesPerSecond < 180; frame++)
        {
            TimeSpan now = TimeSpan.FromSeconds(frame / (double)framesPerSecond);
            HudRect current = motion.Update(layout, pointer, true, now, true, true);
            Assert.True(current.Width >= previous.Width);
            Assert.InRange(MathF.Abs(current.Right - layout.Minimap.Right), 0, .01f);
            Assert.InRange(MathF.Abs(current.Bottom - layout.Minimap.Bottom), 0, .01f);
            previous = current;
        }

        HudRect final = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(180), true, true);
        Assert.InRange(final.Width / layout.Minimap.Width, 1.49f, 1.51f);
        Assert.Equal(MinimapHoverPhase.Expanded, motion.Phase);
    }

    [Fact]
    public void MidFlightReversalPreservesCurrentGeometryAndCanResume()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        motion.Update(layout, pointer, true, TimeSpan.Zero, true, true);
        HudRect growing = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(75), true, true);
        HudRect reversed = motion.Update(layout, Vector2.Zero, true, TimeSpan.FromMilliseconds(85), true, true);
        Assert.Equal(growing, reversed);
        Assert.Equal(MinimapHoverPhase.Shrinking, motion.Phase);

        HudRect shrinking = motion.Update(layout, Vector2.Zero, true, TimeSpan.FromMilliseconds(125), true, true);
        Assert.True(shrinking.Width < growing.Width);
        HudRect resumed = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(135), true, true);
        Assert.Equal(shrinking, resumed);
        HudRect final = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(315), true, true);
        Assert.InRange(final.Width / layout.Minimap.Width, 1.49f, 1.51f);
    }

    [Fact]
    public void OccupiedAlertAreaReportsAndEnforcesActualExpansionLimit()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        HudRect baseMap = layout.Minimap;
        HudRect alert = new(baseMap.Right - 310, baseMap.Y - 95, 180, 70);
        GameplayHudLayout occupied = layout with { AlertStack = alert };
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));

        HudRect bounded = motion.Update(occupied, pointer, true, TimeSpan.Zero, true, false);
        Assert.InRange(motion.MaximumScale, 1, MinimapHoverMotion.DesiredScale - .01f);
        Assert.False(bounded.Intersects(alert));
        Assert.InRange(bounded.Width / baseMap.Width, motion.MaximumScale - .001f, motion.MaximumScale + .001f);

        HudRect unoccupied = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(20), true, false);
        Assert.InRange(unoccupied.Width / baseMap.Width, 1.49f, 1.51f);
        Assert.Equal(MinimapHoverMotion.DesiredScale, motion.MaximumScale);
    }

    [Fact]
    public void NewlyOccupiedRingClampsImmediatelyWithoutAnimatedOverlap()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        motion.Update(layout, pointer, true, TimeSpan.Zero, true, true);
        HudRect full = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(180), true, true);
        HudRect ring = new(layout.Minimap.X - 70, layout.Minimap.Y - 70, 50, 50);

        HudRect clamped = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(190),
            true, true, ringBounds: ring);
        Assert.True(clamped.Width < full.Width);
        Assert.False(clamped.Intersects(ring));
        Assert.True(motion.MaximumScale < MinimapHoverMotion.DesiredScale);
        HudRect recovered = motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(200),
            true, false);
        Assert.InRange(recovered.Width / layout.Minimap.Width, 1.49f, 1.51f);
    }

    [Fact]
    public void WarmSteadyHoverDoesNotAllocateManagedMemory()
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(1600, 900, 96);
        var motion = new MinimapHoverMotion();
        Vector2 pointer = Center(RtsMinimapInteractionLayout.GetMapRect(layout));
        for (int i = 0; i < 512; i++)
            motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(i), true, true);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 512; i < 1512; i++)
            motion.Update(layout, pointer, true, TimeSpan.FromMilliseconds(i), true, true);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static Vector2 Center(in HudRect bounds) =>
        new(bounds.X + bounds.Width * .5f, bounds.Y + bounds.Height * .5f);
}
