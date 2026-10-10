using System.Numerics;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public static class DirectionalShadowProjection
{
    public static GraphicsDirectionalShadowFrame Create(in RtsCameraState camera, in SceneLightingSettings lighting,
        GraphicsDirectionalShadowSettings settings)
    {
        settings.Validate();
        lighting.Validate();
        if (!settings.Enabled) throw new ArgumentException("A disabled profile has no shadow projection.", nameof(settings));
        Vector3 direction = Vector3.Normalize(lighting.DirectionToLight);
        Vector3 up = MathF.Abs(direction.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 orientation = Matrix4x4.CreateLookAt(direction, Vector3.Zero, up);
        Vector3 center = Vector3.Transform(camera.Target, orientation);
        float texel = 2 * settings.Radius / settings.Resolution;
        center.X = MathF.Round(center.X / texel) * texel;
        center.Y = MathF.Round(center.Y / texel) * texel;
        Matrix4x4.Invert(orientation, out Matrix4x4 inverse);
        Vector3 stabilized = Vector3.Transform(center, inverse);
        float depth = settings.Radius * 4;
        Matrix4x4 view = Matrix4x4.CreateLookAt(stabilized + direction * depth * 0.5f, stabilized, up);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(settings.Radius * 2, settings.Radius * 2, 0.1f, depth);
        return new(view * projection, stabilized, settings.Radius, depth);
    }
}
