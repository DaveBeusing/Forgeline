using System.Globalization;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ClientGraphicsOverrides
{
    internal static RenderQualitySettings Read(RenderQualitySettings saved, Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var settings = saved with
        {
            Quality = EnumValue<RenderQuality>("FORGELINE_RENDER_QUALITY") ?? saved.Quality,
            Antialiasing = EnumValue<SpatialAntialiasing>("FORGELINE_ANTIALIASING") ?? saved.Antialiasing,
            Shadows = EnumValue<DirectionalShadowQuality>("FORGELINE_SHADOW_QUALITY") ?? saved.Shadows,
            Textures = EnumValue<TextureDetailQuality>("FORGELINE_TEXTURE_QUALITY") ?? saved.Textures,
            ObjectDetail = EnumValue<RenderQuality>("FORGELINE_OBJECT_DETAIL") ?? saved.ObjectDetail,
            Atmosphere = EnumValue<AtmosphereQuality>("FORGELINE_ATMOSPHERE_QUALITY") ?? saved.Atmosphere,
            Particles = EnumValue<ParticleQuality>("FORGELINE_PARTICLE_QUALITY") ?? saved.Particles,
            SurfaceDetail = BooleanValue("FORGELINE_SURFACE_DETAIL") ?? saved.SurfaceDetail,
            VegetationMotion = BooleanValue("FORGELINE_VEGETATION_MOTION") ?? saved.VegetationMotion
        };
        string? scale = read("FORGELINE_RENDER_SCALE");
        if (!string.IsNullOrEmpty(scale))
        {
            if (!float.TryParse(scale, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new ArgumentException("FORGELINE_RENDER_SCALE requires a finite value from 0.5 through 1.");
            settings = settings with { ResolutionScale = value };
        }
        settings.Validate();
        return settings;

        T? EnumValue<T>(string name) where T : struct, Enum
        {
            string? text = read(name);
            if (string.IsNullOrEmpty(text)) return null;
            if (!Enum.TryParse(text, true, out T value) || !Enum.IsDefined(value))
                throw new ArgumentException($"{name} requires one of: {string.Join(", ", Enum.GetNames<T>())}.");
            return value;
        }
        bool? BooleanValue(string name)
        {
            string? text = read(name);
            if (string.IsNullOrEmpty(text)) return null;
            if (!bool.TryParse(text, out bool value)) throw new ArgumentException($"{name} requires true or false.");
            return value;
        }
    }
}
