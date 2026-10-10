using System.Numerics;
using System.Text.Json.Serialization;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public readonly record struct SceneAtmosphereSettings
{
    public SceneAtmosphereSettings() { }
    public AtmosphereQuality Quality { get; init; }
    public float ColorRed { get; init; } = 0.58f;
    public float ColorGreen { get; init; } = 0.66f;
    public float ColorBlue { get; init; } = 0.76f;
    public float Density { get; init; } = 0.0012f;
    public float StartDistance { get; init; } = 100;
    public float BaseHeight { get; init; }
    public float HeightFalloff { get; init; } = 0.02f;
    public float MaximumOpacity { get; init; } = 0.5f;
    public float ZoomCompensation { get; init; } = 0.75f;
    [JsonIgnore]
    public GraphicsAtmosphereSettings Graphics => new(Quality, new(ColorRed, ColorGreen, ColorBlue),
        Density, StartDistance, BaseHeight, HeightFalloff, MaximumOpacity, ZoomCompensation);
    public static SceneAtmosphereSettings Default => new();
    public void Validate() => Graphics.Validate();

    public static GraphicsAtmosphereView CreateView(RtsCamera camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (!Matrix4x4.Invert(camera.GetMatrices(width, height).ViewProjection, out Matrix4x4 inverse))
            throw new ArgumentException("The atmospheric view requires an invertible camera projection.", nameof(camera));
        return new(inverse, camera.Position, camera.Distance);
    }
}
