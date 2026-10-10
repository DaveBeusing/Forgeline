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
        Assert.Equal((long)resolution * resolution * 12, settings.PayloadBytes(3));
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
    }
}
