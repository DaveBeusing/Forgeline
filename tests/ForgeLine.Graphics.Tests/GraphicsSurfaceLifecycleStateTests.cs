using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class GraphicsSurfaceLifecycleStateTests
{
    [Fact]
    public void ValidResizeRequestBecomesPendingAndCompletes()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_280,
                720,
                suspended: false);

        Assert.True(
            state.RequestResize(
                1_920,
                1_080));
        Assert.True(
            state.HasPendingResize);
        Assert.True(
            state.TryGetPendingResize(
                out GraphicsResizeRequest request));
        Assert.Equal(
            1_920,
            request.Width);
        Assert.Equal(
            1_080,
            request.Height);

        state.CompleteResize(
            request);

        Assert.Equal(
            1_920,
            state.Width);
        Assert.Equal(
            1_080,
            state.Height);
        Assert.False(
            state.HasPendingResize);
        Assert.False(
            state.IsSuspended);
        Assert.Equal(
            state.ResizeGeneration,
            state.AppliedResizeGeneration);
    }

    [Fact]
    public void ZeroSizeSuspendsWithoutCreatingInvalidResize()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_920,
                1_080,
                suspended: false);

        Assert.True(
            state.RequestResize(
                0,
                0));

        Assert.True(
            state.IsSuspended);
        Assert.False(
            state.HasPendingResize);
        Assert.Equal(
            1_920,
            state.Width);
        Assert.Equal(
            1_080,
            state.Height);
    }

    [Fact]
    public void RestoreAtSameSizeQueuesResumeResize()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_920,
                1_080,
                suspended: true);

        Assert.True(
            state.RequestResize(
                1_920,
                1_080));
        Assert.True(
            state.IsSuspended);
        Assert.True(
            state.HasPendingResize);

        Assert.True(
            state.TryGetPendingResize(
                out GraphicsResizeRequest request));
        state.CompleteResize(
            request);

        Assert.False(
            state.IsSuspended);
        Assert.Equal(
            1_920,
            state.Width);
        Assert.Equal(
            1_080,
            state.Height);
    }

    [Fact]
    public void MultipleResizeRequestsCollapseToLatestValidSize()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_280,
                720,
                suspended: false);

        Assert.True(
            state.RequestResize(
                1_600,
                900));
        Assert.True(
            state.RequestResize(
                2_560,
                1_440));

        Assert.True(
            state.TryGetPendingResize(
                out GraphicsResizeRequest request));
        Assert.Equal(
            2_560,
            request.Width);
        Assert.Equal(
            1_440,
            request.Height);

        state.CompleteResize(
            request);

        Assert.Equal(
            2_560,
            state.Width);
        Assert.Equal(
            1_440,
            state.Height);
    }

    [Fact]
    public void SuspensionCancelsPendingResizeUntilAValidRestoreArrives()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_280,
                720,
                suspended: false);

        _ = state.RequestResize(
            1_600,
            900);
        Assert.True(
            state.RequestResize(
                0,
                0));

        Assert.True(
            state.IsSuspended);
        Assert.False(
            state.HasPendingResize);
        Assert.False(
            state.TryGetPendingResize(
                out _));

        Assert.True(
            state.RequestResize(
                1_920,
                1_080));
        Assert.True(
            state.HasPendingResize);
    }

    [Fact]
    public void OcclusionStateCanRecoverAndIsClearedByResizeCompletion()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_280,
                720,
                suspended: false);

        Assert.True(
            state.MarkOccluded());
        Assert.True(
            state.IsOccluded);
        Assert.False(
            state.MarkOccluded());

        Assert.True(
            state.MarkPresentable());
        Assert.False(
            state.IsOccluded);

        _ = state.MarkOccluded();
        _ = state.RequestResize(
            1_600,
            900);
        Assert.True(
            state.TryGetPendingResize(
                out GraphicsResizeRequest request));

        state.CompleteResize(
            request);

        Assert.False(
            state.IsOccluded);
    }

    [Fact]
    public void StaleResizeCompletionIsRejected()
    {
        var state =
            new GraphicsSurfaceLifecycleState(
                1_280,
                720,
                suspended: false);

        _ = state.RequestResize(
            1_600,
            900);
        Assert.True(
            state.TryGetPendingResize(
                out GraphicsResizeRequest stale));

        _ = state.RequestResize(
            1_920,
            1_080);

        Assert.Throws<InvalidOperationException>(
            () => state.CompleteResize(
                stale));
    }
}
