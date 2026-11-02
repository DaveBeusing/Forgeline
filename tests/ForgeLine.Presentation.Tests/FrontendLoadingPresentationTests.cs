using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class FrontendLoadingPresentationTests
{
    [Fact]
    public void OnlyFiniteReportedRunningProgressCanShowAPercentage()
    {
        var running = FrontendSurfaceView.Loading("RESTORING SAVED SESSION", true, .625f);
        Assert.Equal(FrontendLoadingVisualState.Running, running.LoadingState);
        Assert.True(running.LoadingShowsProgress);
        Assert.False(FrontendSurfaceView.Loading("WAITING", false, 0).LoadingShowsProgress);
        Assert.False((running with { Progress = float.NaN }).LoadingShowsProgress);
        Assert.False((running with { Progress = float.PositiveInfinity }).LoadingShowsProgress);

        foreach (var state in new[]
        {
            FrontendLoadingVisualState.Pending,
            FrontendLoadingVisualState.Cancelling,
            FrontendLoadingVisualState.Completed,
            FrontendLoadingVisualState.RecoverableError,
            FrontendLoadingVisualState.FatalError
        })
        {
            Assert.False((running with { LoadingState = state }).LoadingShowsProgress);
        }
    }

    [Theory]
    [InlineData(FrontendLoadingVisualState.Pending, "WAITING FOR COMMAND SYSTEMS", false)]
    [InlineData(FrontendLoadingVisualState.Cancelling, "CANCELLATION REQUESTED", false)]
    [InlineData(FrontendLoadingVisualState.Completed, "COMMAND SYSTEMS READY", false)]
    [InlineData(FrontendLoadingVisualState.RecoverableError, "SESSION RECOVERY REQUIRED", true)]
    [InlineData(FrontendLoadingVisualState.FatalError, "COMMAND SYSTEMS FAILED", true)]
    public void LifecycleStatesHaveExplicitTextAndErrorClassification(
        FrontendLoadingVisualState state, string heading, bool isError)
    {
        var view = FrontendSurfaceView.Loading("A reported status", false, 0) with { LoadingState = state };
        Assert.Equal(heading, view.LoadingHeading);
        Assert.Equal(isError, view.LoadingIsError);
    }
}
