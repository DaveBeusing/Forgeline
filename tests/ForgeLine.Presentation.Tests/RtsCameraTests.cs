using System.Numerics;
using ForgeLine.Input;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsCameraTests
{
    [Theory]
    [InlineData(0f, -1f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1.2f, -1f, 0f)]
    [InlineData(1.2f, 1f, 0f)]
    [InlineData(-2.4f, 0f, 1f)]
    [InlineData(-2.4f, 0f, -1f)]
    public void PanAxesAgreeWithProjectedViewAtEveryYaw(float yaw, float x, float y)
    {
        var camera = new RtsCamera(StableMovementSettings() with { InitialYawRadians = yaw });
        Vector3 initial = camera.Target;
        camera.Update(Frame(pan: new(x, y)), 0.1f, 1600, 900);
        Vector2 projectedMovement = camera.WorldToScreen(initial, 1600, 900).Position - new Vector2(800, 450);
        if (x != 0) Assert.True(projectedMovement.X * x < 0);
        if (y != 0) Assert.True(projectedMovement.Y * y > 0);
    }

    [Theory]
    [InlineData(ForgeLine.Platform.PlatformKey.A, 0, 450)]
    [InlineData(ForgeLine.Platform.PlatformKey.Left, 0, 450)]
    [InlineData(ForgeLine.Platform.PlatformKey.D, 1599, 450)]
    [InlineData(ForgeLine.Platform.PlatformKey.Right, 1599, 450)]
    [InlineData(ForgeLine.Platform.PlatformKey.W, 800, 0)]
    [InlineData(ForgeLine.Platform.PlatformKey.S, 800, 899)]
    public void KeyboardAndEdgePanHaveEquivalentDirections(ForgeLine.Platform.PlatformKey key, int x, int y)
    {
        var keyboard = new RtsCamera(StableMovementSettings());
        var edge = new RtsCamera(StableMovementSettings() with { EdgeScrollEnabled = true });
        var input = new InputState();
        input.Apply(ForgeLine.Platform.PlatformInputEvent.KeyChanged(ForgeLine.Platform.PlatformInputEventKind.KeyDown, key));
        keyboard.Update(new RtsCameraActionMapper().Map(input), 0.1f, 1600, 900);
        edge.Update(Frame(hasPointerPosition: true, pointerPosition: new(x, y)), 0.1f, 1600, 900);
        Assert.InRange(Vector3.Dot(Vector3.Normalize(keyboard.Target), Vector3.Normalize(edge.Target)), 0.9999f, 1.0001f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void HorizontalPanDistanceMatchesElapsedTimeAcrossFrameRates(int frames)
    {
        var camera = new RtsCamera(StableMovementSettings());
        for (int i = 0; i < frames; i++) camera.Update(Frame(pan: Vector2.UnitX), 1f / frames, 960, 540);
        Assert.InRange(Vector3.Distance(camera.Target, camera.GroundRight * camera.Settings.BasePanSpeedUnitsPerSecond), 0, 0.001f);
    }

    [Theory]
    [InlineData(-1, 450)]
    [InlineData(1600, 450)]
    [InlineData(800, -1)]
    [InlineData(800, 900)]
    public void PointerOutsideResizedViewportDoesNotEdgePan(int x, int y)
    {
        var camera = new RtsCamera(StableMovementSettings() with { EdgeScrollEnabled = true });
        camera.Update(Frame(hasPointerPosition: true, pointerPosition: new(x, y)), 1, 1600, 900);
        Assert.Equal(Vector3.Zero, camera.Target);
    }

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
    public void ScreenWorldGroundRoundTripReturnsOriginalTarget()
    {
        var camera = new RtsCamera(StableMovementSettings());
        ScreenProjection projected = camera.WorldToScreen(camera.Target, 1600, 900);

        bool hit = camera.TryScreenPointToWorldOnHorizontalPlane(
            projected.Position,
            camera.Target.Y,
            1600,
            900,
            out Vector3 worldPoint);

        Assert.True(hit);
        Assert.InRange(Vector3.Distance(camera.Target, worldPoint), 0.0f, 0.005f);
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
    public void CameraStateTransferPreservesViewWithoutSharingMutableCamera()
    {
        var settings =
            StableMovementSettings();
        var source =
            new RtsCamera(settings);
        var copy =
            new RtsCamera(settings);

        source.Update(
            Frame(
                pan: new Vector2(0.4f, 0.8f),
                rotation: 0.5f,
                pitch: -0.25f,
                zoomSteps: -2.0f,
                hasPointerPosition: true,
                pointerPosition: new Vector2(320.0f, 240.0f)),
            0.5f,
            1600,
            900);

        RtsCameraState state =
            source.CaptureState();
        copy.ApplyState(
            state);

        Assert.Equal(
            source.Target,
            copy.Target);
        Assert.Equal(
            source.YawRadians,
            copy.YawRadians);
        Assert.Equal(
            source.PitchRadians,
            copy.PitchRadians);
        Assert.Equal(
            source.Distance,
            copy.Distance);
        Assert.Equal(
            source.GetMatrices(1600, 900),
            copy.GetMatrices(1600, 900));

        source.Update(
            Frame(
                pan: new Vector2(1.0f, 0.0f)),
            0.25f,
            1600,
            900);

        Assert.NotEqual(
            source.Target,
            copy.Target);
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

        Assert.True(camera.Target.X > 0.0f);
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

        Assert.InRange(camera.Target.X, 4.999f, 5.001f);
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
