using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public enum RenderQuality { Low, Medium, High }

public readonly record struct RenderQualitySettings
{
    public RenderQualitySettings() { }
    public RenderQuality Quality { get; init; } = RenderQuality.Medium;
    public GraphicsPostProcessingSettings ColorGrade { get; init; } = GraphicsPostProcessingSettings.Neutral;
    public void Validate()
    {
        if (!Enum.IsDefined(Quality)) throw new ArgumentOutOfRangeException(nameof(Quality));
        ColorGrade.Validate();
    }
    public RenderQualityProfile Resolve(bool supported, bool software, int width, int height, int buffers, ulong? availableBytes = null)
    {
        Validate();
        RenderQuality requested = Quality;
        GraphicsPostProcessingSettings grade = ColorGrade;
        if (width <= 0 || height <= 0 || buffers is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(width));
        RenderQuality active = software ? RenderQuality.Low : Quality;
        string? fallback = software && Quality != active ? "Software adapter uses Low quality." : null;
        if (!supported) return Profile(RenderQuality.Low, false, "Linear postprocessing is unsupported.");
        while (availableBytes.HasValue && Payload(active) > availableBytes.Value)
        {
            if (active == RenderQuality.Low) return Profile(active, false, "Insufficient budget for linear scene targets.");
            active--;
            fallback = "Quality reduced to fit the available graphics budget.";
        }
        return Profile(active, true, fallback);

        ulong Payload(RenderQuality quality) => checked((ulong)width * (ulong)height * 8 * (ulong)buffers +
            (ulong)new GraphicsDirectionalShadowSettings((DirectionalShadowQuality)((int)quality + 1)).PayloadBytes(buffers) +
            (ulong)new GraphicsDirectionalShadowSettings((DirectionalShadowQuality)((int)quality + 1)).ConstantUploadPayloadBytes(buffers) +
            (ulong)new ParticleSettings((ParticleQuality)((int)quality + 1)).Capacity * 64 * (ulong)buffers + 12);
        RenderQualityProfile Profile(RenderQuality activeQuality, bool linear, string? reason) => new(requested, activeQuality, linear,
            new(linear ? (DirectionalShadowQuality)((int)activeQuality + 1) : DirectionalShadowQuality.Disabled),
            linear && activeQuality != RenderQuality.Low ? AtmosphereQuality.Height : AtmosphereQuality.Disabled,
            new(linear ? (ParticleQuality)((int)activeQuality + 1) : ParticleQuality.Disabled),
            activeQuality == RenderQuality.Low ? TerrainSurfaceDetailSettings.Disabled : TerrainSurfaceDetailSettings.Default,
            activeQuality switch { RenderQuality.Low => .8f, RenderQuality.High => 1.1f, _ => 1 },
            grade with { Antialiasing = !linear || activeQuality == RenderQuality.Low ? SpatialAntialiasing.Disabled :
                activeQuality == RenderQuality.High ? SpatialAntialiasing.High : SpatialAntialiasing.Low }, reason);
    }
}

public readonly record struct RenderQualityProfile(RenderQuality Requested, RenderQuality Active, bool LinearScene,
    GraphicsDirectionalShadowSettings Shadows, AtmosphereQuality Atmosphere, ParticleSettings Particles,
    TerrainSurfaceDetailSettings TerrainDetail, float LodDiameterScale, GraphicsPostProcessingSettings PostProcessing, string? FallbackReason);
