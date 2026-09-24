using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ViewFrustumTests
{
    [Fact]
    public void FrustumAcceptsBoundsAroundCameraTarget()
    {
        var camera = new RtsCamera();
        CameraMatrices matrices = camera.GetMatrices(1600, 900);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(matrices.ViewProjection);

        var bounds = new AxisAlignedBounds(
            camera.Target - new Vector3(5.0f),
            camera.Target + new Vector3(5.0f));

        Assert.True(frustum.Intersects(bounds));
    }

    [Fact]
    public void FrustumRejectsBoundsBehindCamera()
    {
        var camera = new RtsCamera();
        CameraMatrices matrices = camera.GetMatrices(1600, 900);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(matrices.ViewProjection);

        Vector3 awayFromTarget = Vector3.Normalize(
            camera.Position - camera.Target);
        Vector3 center =
            camera.Position + awayFromTarget * 250.0f;
        var bounds = new AxisAlignedBounds(
            center - new Vector3(5.0f),
            center + new Vector3(5.0f));

        Assert.False(frustum.Intersects(bounds));
    }
}
