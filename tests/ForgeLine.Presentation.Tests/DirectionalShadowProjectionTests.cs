using System.Numerics;
using ForgeLine.Graphics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DirectionalShadowProjectionTests
{
    [Theory]
    [InlineData(DirectionalShadowQuality.Low, 1024, 180)]
    [InlineData(DirectionalShadowQuality.Medium, 2048, 450)]
    [InlineData(DirectionalShadowQuality.High, 4096, 750)]
    public void ProfilesHaveBoundedStableProjection(DirectionalShadowQuality quality, int resolution, float radius)
    {
        var settings = new GraphicsDirectionalShadowSettings(quality);
        var camera = new RtsCameraState(new Vector3(120, 4, 130), 0.3f, -1, 420, false, default);
        GraphicsDirectionalShadowFrame frame = DirectionalShadowProjection.Create(camera, SceneLightingSettings.Default, settings);
        frame.Validate();
        Assert.Equal(resolution, settings.Resolution);
        Assert.Equal(radius, frame.Radius);
        Assert.Equal(1, settings.CascadeCount);
        Assert.Equal((long)resolution * resolution * 24, settings.PayloadBytes(3));
        Vector3 center = Vector3.Transform(frame.Center, frame.ViewProjection);
        Assert.InRange(MathF.Abs(center.X), 0, 0.00001f);
        Assert.InRange(MathF.Abs(center.Y), 0, 0.00001f);
        var staticFrame = DirectionalShadowProjection.Create(camera, SceneLightingSettings.Default, settings);
        Assert.Equal(frame, staticFrame);
        // Zoom cannot resize the shadow footprint or change static texel alignment.
        var zoom = DirectionalShadowProjection.Create(camera with { Distance = 1000 }, SceneLightingSettings.Default, settings);
        Assert.Equal(frame, zoom);
    }

    [Fact]
    public void DisabledProfileAllocatesNoTargetsAndInvalidProfilesAreRejected()
    {
        Assert.Equal(0, default(GraphicsDirectionalShadowSettings).PayloadBytes(3));
        Assert.Equal(0, default(GraphicsDirectionalShadowSettings).MaximumCasters);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsDirectionalShadowSettings((DirectionalShadowQuality)99).Validate());
        Assert.Throws<ArgumentException>(() => new GraphicsDirectionalShadowFrame(default, default, 1, 1).Validate());
        Assert.Throws<ArgumentException>(() => new GraphicsDirectionalShadowFrame(Matrix4x4.Identity, default, 1, 1, 0.9f).Validate());
        var invalid = Matrix4x4.Identity;
        invalid.M33 = float.NaN;
        Assert.Throws<ArgumentException>(() => new GraphicsDirectionalShadowFrame(invalid, default, 1, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsDirectionalShadowSettings(DirectionalShadowQuality.Low).PayloadBytes(0));
    }

    [Fact]
    public void SubTexelCameraTranslationDoesNotMoveTheShadowGrid()
    {
        var settings = new GraphicsDirectionalShadowSettings(DirectionalShadowQuality.Low);
        var lighting = SceneLightingSettings.Default;
        var camera = new RtsCameraState(Vector3.Zero, 0, -1, 420, false, default);
        Matrix4x4 orientation = Matrix4x4.CreateLookAt(lighting.DirectionToLight, Vector3.Zero, Vector3.UnitY);
        Matrix4x4.Invert(orientation, out Matrix4x4 inverse);
        Vector3 delta = Vector3.TransformNormal(Vector3.UnitX * (2 * settings.Radius / settings.Resolution * 0.1f), inverse);
        var first = DirectionalShadowProjection.Create(camera, lighting, settings);
        var moved = DirectionalShadowProjection.Create(camera with { Target = delta }, lighting, settings);
        Assert.InRange(Vector3.Distance(first.Center, moved.Center), 0, 0.00001f);
    }
}
