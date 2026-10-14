using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public enum RenderQuality { Low, Medium, High, Ultra }
public enum TextureDetailQuality { Full, Half, Quarter }

public readonly record struct RenderQualitySettings
{
    public RenderQualitySettings() { }
    public RenderQuality Quality { get; init; } = RenderQuality.Medium;
    public GraphicsPostProcessingSettings ColorGrade { get; init; } = GraphicsPostProcessingSettings.Neutral;
    public float? ResolutionScale { get; init; }
    public SpatialAntialiasing? Antialiasing { get; init; }
    public DirectionalShadowQuality? Shadows { get; init; }
    public TextureDetailQuality? Textures { get; init; }
    public RenderQuality? ObjectDetail { get; init; }
    public bool? SurfaceDetail { get; init; }
    public bool? VegetationMotion { get; init; }
    public AtmosphereQuality? Atmosphere { get; init; }
    public ParticleQuality? Particles { get; init; }
    public WorldEnvironmentSettings Environment { get; init; } = new();
    public VegetationRenderSettings Vegetation { get; init; } = new();
    public TerrainResidencySettings TerrainResidency { get; init; } = TerrainResidencySettings.Default;

    public void Validate()
    {
        Require(Quality, nameof(Quality));
        ColorGrade.Validate();
        Environment.Validate(); Vegetation.Validate(); TerrainResidency.Validate();
        if (ResolutionScale is { } scale && (!float.IsFinite(scale) || scale is < .5f or > 1))
            throw new ArgumentOutOfRangeException(nameof(ResolutionScale));
        if (Antialiasing is { } aa) Require(aa, nameof(Antialiasing));
        if (Shadows is { } shadow) Require(shadow, nameof(Shadows));
        if (Textures is { } texture) Require(texture, nameof(Textures));
        if (ObjectDetail is { } detail) Require(detail, nameof(ObjectDetail));
        if (Atmosphere is { } atmosphere) Require(atmosphere, nameof(Atmosphere));
        if (Particles is { } particles) Require(particles, nameof(Particles));
    }

    public RenderQualityProfile Resolve(bool supported, bool software, int width, int height, int buffers,
        ulong? availableBytes = null, bool particlesSupported = true)
    {
        Validate();
        if (width <= 0 || height <= 0 || buffers is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(width));
        RenderQualitySettings input = this;
        RenderQuality active = software ? RenderQuality.Low : Quality;
        string? reason = software ? "Software adapter uses Low quality." : null;
        if (!supported) return Profile(RenderQuality.Low, false, "Linear scene formats or postprocessing are unsupported.");
        while (true)
        {
            RenderQualityProfile profile = Profile(active, true, reason);
            if (!availableBytes.HasValue || Payload(profile) <= availableBytes.Value) return profile;
            if (active == RenderQuality.Low) return Profile(active, false, "Insufficient budget for linear scene targets.");
            active--;
            reason = "Quality reduced to fit the available graphics budget.";
        }

        ulong Payload(RenderQualityProfile profile)
        {
            int w = Math.Max(1, (int)Math.Ceiling(width * (double)profile.ResolutionScale));
            int h = Math.Max(1, (int)Math.Ceiling(height * (double)profile.ResolutionScale));
            ulong textureAllowance = (64UL * 1024 * 1024) >> (2 * (int)profile.Textures);
            return checked((ulong)w * (ulong)h * (8UL * (ulong)buffers + 4) +
                (profile.ResolutionScale < 1 ? (ulong)width * (ulong)height * 4 : 0) + textureAllowance +
                (ulong)profile.Shadows.PayloadBytes(buffers) + (ulong)profile.Shadows.ConstantUploadPayloadBytes(buffers) +
                (ulong)profile.Particles.Capacity * 64 * (ulong)buffers + 12);
        }

        RenderQualityProfile Profile(RenderQuality selected, bool linear, string? fallback)
        {
            bool limited = selected < input.Quality || software;
            int tier = Math.Min((int)selected, (int)RenderQuality.High);
            var shadow = input.Shadows ?? (DirectionalShadowQuality)(tier + 1);
            var particles = input.Particles ?? (ParticleQuality)(tier + 1);
            var textures = input.Textures ?? (selected == RenderQuality.Low ? TextureDetailQuality.Quarter : TextureDetailQuality.Full);
            var detail = input.ObjectDetail ?? selected;
            if (limited)
            {
                shadow = (DirectionalShadowQuality)Math.Min((int)shadow, tier + 1);
                particles = (ParticleQuality)Math.Min((int)particles, tier + 1);
                textures = (TextureDetailQuality)Math.Max((int)textures, selected == RenderQuality.Low ? 2 : 0);
                detail = (RenderQuality)Math.Min((int)detail, (int)selected);
            }
            var atmosphere = input.Atmosphere ?? (selected == RenderQuality.Low ? AtmosphereQuality.Disabled : AtmosphereQuality.Height);
            var aa = input.Antialiasing ?? (selected == RenderQuality.Low ? SpatialAntialiasing.Disabled :
                selected >= RenderQuality.High ? SpatialAntialiasing.High : SpatialAntialiasing.Low);
            if (limited)
            {
                aa = (SpatialAntialiasing)Math.Min((int)aa, selected == RenderQuality.Low ? 0 : selected == RenderQuality.Medium ? 1 : 2);
                if (software) atmosphere = AtmosphereQuality.Disabled;
            }
            if (!particlesSupported && particles != ParticleQuality.Disabled)
                fallback = string.IsNullOrEmpty(fallback) ? "Particle rendering is unsupported." : fallback + " Particle rendering is unsupported.";
            return new(input.Quality, selected, linear,
                new(linear ? shadow : DirectionalShadowQuality.Disabled),
                linear ? atmosphere : AtmosphereQuality.Disabled,
                new(linear && particlesSupported ? particles : ParticleQuality.Disabled),
                (input.SurfaceDetail ?? selected != RenderQuality.Low) && (!limited || selected != RenderQuality.Low)
                    ? TerrainSurfaceDetailSettings.Default : TerrainSurfaceDetailSettings.Disabled,
                detail switch { RenderQuality.Low => .8f, RenderQuality.High => 1.1f, RenderQuality.Ultra => 1.25f, _ => 1 },
                linear ? input.ColorGrade with { Antialiasing = aa } : GraphicsPostProcessingSettings.Neutral, fallback)
            {
                ResolutionScale = linear ? input.ResolutionScale ?? 1 : 1,
                Textures = linear ? textures : TextureDetailQuality.Quarter,
                VegetationMotion = !limited || selected != RenderQuality.Low ? input.VegetationMotion ?? selected != RenderQuality.Low : false,
                RequestedSettings = input,
                Environment = limited && selected == RenderQuality.Low ? input.Environment with { CloudCover = 0, Wetness = 0 } : input.Environment,
                Vegetation = input.Vegetation, TerrainResidency = input.TerrainResidency
            };
        }
    }

    private static void Require<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct RenderQualityProfile(RenderQuality Requested, RenderQuality Active, bool LinearScene,
    GraphicsDirectionalShadowSettings Shadows, AtmosphereQuality Atmosphere, ParticleSettings Particles,
    TerrainSurfaceDetailSettings TerrainDetail, float LodDiameterScale, GraphicsPostProcessingSettings PostProcessing, string? FallbackReason)
{
    public float ResolutionScale { get; init; } = 1;
    public TextureDetailQuality Textures { get; init; } = TextureDetailQuality.Full;
    public bool VegetationMotion { get; init; } = true;
    public RenderQualitySettings RequestedSettings { get; init; }
    public WorldEnvironmentSettings Environment { get; init; } = new();
    public VegetationRenderSettings Vegetation { get; init; } = new();
    public TerrainResidencySettings TerrainResidency { get; init; } = TerrainResidencySettings.Default;
}
