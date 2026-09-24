using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DebugDrawTests
{
    [Fact]
    public void DisabledDebugDrawDoesNotCollectGeometry()
    {
        var debugDraw = new DebugDraw();

        debugDraw.Line(
            Vector3.Zero,
            Vector3.One,
            Vector4.One);
        debugDraw.Box(
            new AxisAlignedBounds(
                Vector3.Zero,
                Vector3.One),
            Vector4.One);
        debugDraw.Circle(
            Vector3.Zero,
            2.0f,
            Vector4.One);
        debugDraw.Point(
            Vector3.Zero,
            1.0f,
            Vector4.One);

        Assert.True(debugDraw.Lines.IsEmpty);
        Assert.Empty(debugDraw.Labels);
    }

    [Fact]
    public void EnabledDebugDrawBuildsExpectedPrimitiveLines()
    {
        var debugDraw = new DebugDraw
        {
            Enabled = true
        };

        debugDraw.Box(
            new AxisAlignedBounds(
                Vector3.Zero,
                Vector3.One),
            Vector4.One);
        debugDraw.Point(
            Vector3.Zero,
            1.0f,
            Vector4.One);
        debugDraw.Circle(
            Vector3.Zero,
            2.0f,
            Vector4.One,
            segments: 8);
        debugDraw.Label(
            Vector3.Zero,
            "origin",
            Vector4.One);

        Assert.Equal(23, debugDraw.Lines.Length);
        Assert.Single(debugDraw.Labels);

        debugDraw.Clear();

        Assert.True(debugDraw.Lines.IsEmpty);
        Assert.Empty(debugDraw.Labels);
    }

    [Fact]
    public void FrameTimingTrackerSmoothsWithoutProducingInvalidRates()
    {
        var tracker = new FrameTimingTracker();

        FrameTimingMetrics first = tracker.Record(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(4));
        FrameTimingMetrics second = tracker.Record(
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(6));

        Assert.InRange(first.FramesPerSecond, 99.99, 100.01);
        Assert.True(second.FramesPerSecond > 0.0);
        Assert.True(second.FrameMilliseconds >= 10.0);
        Assert.True(second.CpuRenderMilliseconds >= 4.0);
    }
}
