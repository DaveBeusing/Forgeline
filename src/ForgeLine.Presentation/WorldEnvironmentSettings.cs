using System.Globalization;
using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Static visual inputs; never a simulation clock or gameplay weather model.</summary>
public readonly record struct WorldEnvironmentSettings
{
    public WorldEnvironmentSettings() { }
    public float Daylight { get; init; } = 1;
    public float CloudCover { get; init; }
    public float Wetness { get; init; }

    public void Validate()
    {
        Range(Daylight, nameof(Daylight)); Range(CloudCover, nameof(CloudCover)); Range(Wetness, nameof(Wetness));
    }

    public SceneLightingSettings ResolveLighting(SceneLightingSettings lighting)
    {
        Validate(); lighting.Validate();
        if (Daylight == 1 && CloudCover == 0) return lighting;
        Vector3 direction = Vector3.Normalize(lighting.DirectionToLight);
        Vector3 horizontal = new(direction.X, 0, direction.Z);
        horizontal = horizontal.LengthSquared() < .0001f ? Vector3.UnitX : Vector3.Normalize(horizontal);
        float elevation = float.Lerp(.34f, MathF.Asin(Math.Clamp(direction.Y, .34f, 1)), Daylight);
        return lighting with
        {
            DirectionToLight = Daylight == 1 ? lighting.DirectionToLight : horizontal * MathF.Cos(elevation) + Vector3.UnitY * MathF.Sin(elevation),
            DirectionalIntensity = lighting.DirectionalIntensity * float.Lerp(.75f, 1, Daylight) * (1 - .15f * CloudCover),
            DirectionalColor = Vector3.Lerp(new(.95f, .75f, .52f), lighting.DirectionalColor, Daylight),
            AmbientIntensity = lighting.AmbientIntensity * float.Lerp(.85f, 1, Daylight)
        };
    }

    internal string ShaderConstants()
    {
        Validate();
        return CloudCover == 0 && Wetness == 0 ? "" : string.Create(CultureInfo.InvariantCulture,
            $"#define WORLD_ENVIRONMENT 1\nstatic const float WorldCloudCover={CloudCover:R};\nstatic const float WorldWetness={Wetness:R};\n");
    }

    private static void Range(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }
}
