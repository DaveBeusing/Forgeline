using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsVisualReferenceTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(3440, 1440)]
    public void ReferenceCameraKeepsTargetCenteredAndVerticalFramingStable(int width, int height)
    {
        var target = new Vector3(1500, 20, 1700);
        var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(target));
        CameraMatrices matrices = camera.GetMatrices(width, height);
        ScreenProjection screen = camera.WorldToScreen(target, width, height);
        Assert.True(screen.IsVisible);
        Assert.InRange(Vector2.Distance(screen.Position, new Vector2(width / 2f, height / 2f)), 0, 0.01f);
        Assert.Equal(1f / MathF.Tan(MathF.PI / 8f), matrices.Projection.M22, 5);
        Assert.Equal(MathF.PI / 4f, camera.Settings.VerticalFieldOfViewRadians);
        Assert.Equal(420f, camera.Distance);
        Assert.Equal(45f, camera.GetDiagnostics().YawDegrees, 4);
        Assert.Equal(-55f, camera.GetDiagnostics().PitchDegrees, 4);
    }

    [Fact]
    public void ZoomBookmarksExposeMoreDetailAtCloseDistance()
    {
        float[] sizes = Enum.GetValues<RtsReferenceZoom>().Select(zoom =>
        {
            var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(Vector3.Zero, zoom));
            return ScreenSpaceLod.ProjectedDiameter(Vector3.Zero, 5, camera.GetMatrices(2560, 1440), 1440);
        }).ToArray();
        Assert.True(sizes[0] > sizes[1] && sizes[1] > sizes[2]);
    }

    [Fact]
    public void ProjectionRespondsToSizeAndFovButPreservesLodAcrossDisplayResolution()
    {
        var camera = new RtsCamera(RtsVisualReference.CreateCameraSettings(Vector3.Zero));
        float normal = ScreenSpaceLod.ProjectedDiameter(Vector3.Zero, 5, camera.GetMatrices(2560, 1440), 1440);
        Assert.Equal(normal, ScreenSpaceLod.ProjectedDiameter(Vector3.Zero, 5, camera.GetMatrices(3840, 2160), 2160), 5);
        Assert.Equal(normal * 2, ScreenSpaceLod.ProjectedDiameter(Vector3.Zero, 10, camera.GetMatrices(2560, 1440), 1440), 5);
        var narrow = new RtsCamera(camera.Settings with { VerticalFieldOfViewRadians = MathF.PI / 6 });
        Assert.True(ScreenSpaceLod.ProjectedDiameter(Vector3.Zero, 5, narrow.GetMatrices(2560, 1440), 1440) > normal);
    }

    [Fact]
    public void HysteresisPreventsOscillationAndAllowsLargeZoomJumps()
    {
        Assert.Equal(0, ScreenSpaceLod.Select(85, 0));
        Assert.Equal(1, ScreenSpaceLod.Select(85, 1));
        Assert.Equal(1, ScreenSpaceLod.Select(29, 1));
        Assert.Equal(2, ScreenSpaceLod.Select(29, 2));
        Assert.Equal(2, ScreenSpaceLod.Select(10, 0));
        Assert.Equal(0, ScreenSpaceLod.Select(300, 2));
    }
}
