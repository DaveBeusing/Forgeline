using System.Numerics;
using ForgeLine.Input;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsCameraTests
{
    [Fact]
    public void KeyboardPanIsFrameRateIndependent()
    {
        var settings = StableMovementSettings();
        var singleStep = new RtsCamera(settings);
        var splitSteps = new RtsCamera(settings);
        RtsCameraInputFrame input = Frame(pan: new Vector2(0.0f, 1.0f));

        singleStep.Update(input, 1.0f, 1600, 900);

        for (int index = 0; index < 10; index++)
        {
            splitSteps.Update(input, 0.1f, 1600, 900);
        }

        Assert.InRange(
            Vector3.Distance(singleStep.Target, splitSteps.Target),
            0.0f,
            0.0001f);
    }

    [Fact]
    public void ZoomNeverCrossesConfiguredLimits()
    {
        var settings = StableMovementSettings() with
        {
            InitialDistance = 50.0f,
            MinimumDistance = 20.0f,
            MaximumDistance = 100.0f
        };
        var camera = new RtsCamera(settings);

        camera.Update(Frame(zoomSteps: 100.0f), 0.0f, 1600, 900);
        Assert.Equal(20.0f, camera.Distance);

        camera.Update(Frame(zoomSteps: -100.0f), 0.0f, 1600, 900);
        Assert.Equal(100.0f, camera.Distance);
    }

    [Fact]
    public void PanningRemainsCameraRelativeAfterRotation()
    {
        var settings = StableMovementSettings() with
        {
            BasePanSpeedUnitsPerSecond = 10.0f,
            RotationSpeedRadiansPerSecond = MathF.PI / 2.0f
        };
        var camera = new RtsCamera(settings);

        camera.Update(
            Frame(
                pan: new Vector2(0.0f, 1.0f),
                rotation: 1.0f),
            1.0f,
            1600,
            900);

        Assert.InRange(camera.Target.X, 9.999f, 10.001f);
        Assert.InRange(MathF.Abs(camera.Target.Z), 0.0f, 0.001f);
    }

    [Fact]
    public void ScreenCenterRayAlignsWithCameraForwardDirection()
    {
        var camera = new RtsCamera(StableMovementSettings());

        CameraRay ray = camera.ScreenPointToWorldRay(
            new Vector2(800.0f, 450.0f),
            1600,
            900);

        Vector3 expected = Vector3.Normalize(camera.Target - camera.Position);
        float alignment = Vector3.Dot(expected, ray.Direction);

        Assert.InRange(alignment, 0.9999f, 1.0001f);
    }

    [Fact]
    public void TargetProjectsToViewportCenter()
    {
        var camera = new RtsCamera(StableMovementSettings());

        ScreenProjection projection = camera.WorldToScreen(
            camera.Target,
            1600,
            900);

        Assert.True(projection.IsVisible);
        Assert.InRange(projection.Position.X, 799.99f, 800.01f);
        Assert.InRange(projection.Position.Y, 449.99f, 450.01f);
    }

    [Fact]
    public void ProjectionChangesWhenViewportAspectChanges()
    {
        var camera = new RtsCamera(StableMovementSettings());

        CameraMatrices wide = camera.GetMatrices(1600, 900);
        CameraMatrices square = camera.GetMatrices(900, 900);

        Assert.NotEqual(wide.Projection.M11, square.Projection.M11);
        Assert.Equal(wide.Projection.M22, square.Projection.M22);
    }

    [Fact]
    public void EdgeScrollMovesCameraWhenPointerEntersConfiguredZone()
    {
        var settings = StableMovementSettings() with
        {
            EdgeScrollEnabled = true,
            EdgeScrollZonePixels = 20.0f
        };
        var camera = new RtsCamera(settings);

        camera.Update(
            Frame(
                hasPointerPosition: true,
                pointerPosition: new Vector2(0.0f, 450.0f)),
            1.0f,
            1600,
            900);

        Assert.True(camera.Target.X < 0.0f);
    }

    [Fact]
    public void DragPanUsesPointerDeltaWithoutFrameTimeScaling()
    {
        var settings = StableMovementSettings() with
        {
            DragPanUnitsPerPixelAtReferenceDistance = 1.0f
        };
        var camera = new RtsCamera(settings);

        camera.Update(
            Frame(
                dragPan: true,
                hasPointerPosition: true,
                pointerPosition: new Vector2(100.0f, 100.0f),
                pointerDelta: new Vector2(5.0f, 0.0f)),
            0.001f,
            1600,
            900);

        Assert.InRange(camera.Target.X, -5.001f, -4.999f);
    }

    private static RtsCameraSettings StableMovementSettings() =>
        new()
        {
            EdgeScrollEnabled = false,
            InitialDistance = 90.0f,
            PanReferenceDistance = 90.0f,
            MinimumPanSpeedScale = 1.0f,
            MaximumPanSpeedScale = 1.0f
        };

    private static RtsCameraInputFrame Frame(
        Vector2? pan = null,
        float rotation = 0.0f,
        float pitch = 0.0f,
        float zoomSteps = 0.0f,
        bool dragPan = false,
        bool hasPointerPosition = false,
        Vector2? pointerPosition = null,
        Vector2? pointerDelta = null) =>
        new(
            pan ?? Vector2.Zero,
            rotation,
            pitch,
            zoomSteps,
            dragPan,
            hasPointerPosition,
            pointerPosition ?? Vector2.Zero,
            pointerDelta ?? Vector2.Zero);
}
