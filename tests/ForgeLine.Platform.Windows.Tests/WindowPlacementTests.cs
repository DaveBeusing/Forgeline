using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void OffscreenSavedBoundsFallBackToCurrentWorkArea()
    {
        var requested =
            new WindowBounds(
                4_000,
                100,
                1_600,
                900);
        var workArea =
            new WindowBounds(
                0,
                0,
                1_920,
                1_040);

        WindowBounds result =
            WindowPlacement.ConstrainToWorkArea(
                requested,
                workArea,
                fallbackWidth: 1_280,
                fallbackHeight: 720);

        Assert.Equal(
            new WindowBounds(
                160,
                70,
                1_600,
                900),
            result);
    }

    [Fact]
    public void OversizedSavedBoundsAreConstrainedToAvailableWorkArea()
    {
        var requested =
            new WindowBounds(
                -100,
                -100,
                3_840,
                2_160);
        var workArea =
            new WindowBounds(
                0,
                0,
                1_920,
                1_040);

        WindowBounds result =
            WindowPlacement.ConstrainToWorkArea(
                requested,
                workArea,
                fallbackWidth: 1_280,
                fallbackHeight: 720);

        Assert.Equal(
            workArea,
            result);
    }

    [Fact]
    public void InvalidSavedDimensionsUseValidatedFallbackDimensions()
    {
        var requested =
            new WindowBounds(
                300,
                200,
                0,
                -1);
        var workArea =
            new WindowBounds(
                0,
                0,
                1_920,
                1_040);

        WindowBounds result =
            WindowPlacement.ConstrainToWorkArea(
                requested,
                workArea,
                fallbackWidth: 1_280,
                fallbackHeight: 720);

        Assert.Equal(
            new WindowBounds(
                320,
                160,
                1_280,
                720),
            result);
    }
}
