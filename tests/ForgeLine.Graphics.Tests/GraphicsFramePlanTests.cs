using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class GraphicsFramePlanTests
{
    [Theory]
    [InlineData(320, 240, 2)]
    [InlineData(1920, 1080, 3)]
    [InlineData(5120, 2160, 4)]
    public void DirectOutputDescribesExistingResourcesWithoutTransientCopies(int width, int height, int buffers)
    {
        var plan = new GraphicsFramePlan(width, height, buffers);
        Assert.Equal((long)width * height * 4 * buffers, plan.Output.PayloadBytes);
        Assert.Equal((long)width * height * 4, plan.Depth.PayloadBytes);
        Assert.Equal(GraphicsFrameTargetLifetime.BackBuffer, plan.Output.Lifetime);
        Assert.Equal(GraphicsFrameTargetLifetime.Surface, plan.Depth.Lifetime);
        Assert.Equal(0, plan.TransientPayloadBytes);
    }

    [Theory]
    [InlineData(0, 1, 3)]
    [InlineData(1, -1, 3)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 1, 5)]
    public void SuspendedSizesAndUnsupportedBufferCountsHaveNoActivePlan(int width, int height, int buffers) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsFramePlan(width, height, buffers));

    [Fact]
    public void OutputSpaceOverlayCannotReturnToWorld()
    {
        var state = new GraphicsFramePassState();
        Assert.False(state.Enter(GraphicsFramePass.World));
        Assert.True(state.Enter(GraphicsFramePass.Overlay));
        Assert.False(state.Enter(GraphicsFramePass.Overlay));
        Assert.Throws<InvalidOperationException>(() => state.Enter(GraphicsFramePass.World));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Enter((GraphicsFramePass)42));
    }

    [Fact]
    public void LargeTargetAccountingDoesNotOverflow32BitArithmetic()
    {
        var plan = new GraphicsFramePlan(16384, 16384, 4);
        Assert.Equal(4_294_967_296L, plan.Output.PayloadBytes);
    }
}
