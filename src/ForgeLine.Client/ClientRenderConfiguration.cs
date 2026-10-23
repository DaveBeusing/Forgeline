using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// Shared by gameplay and native capture; apply only between renderer lifetimes.
internal readonly record struct ClientRenderConfiguration(
    RenderQualityProfile Profile, SceneLightingSettings Lighting, GraphicsAtmosphereSettings Atmosphere)
{
    internal static ClientRenderConfiguration Resolve(IGraphicsDevice graphics, RenderQualitySettings settings,
        int width, int height, SceneLightingSettings lighting, GraphicsAtmosphereSettings atmosphere)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        lighting.Validate(); atmosphere.Validate();
        var diagnostics = graphics.Diagnostics;
        var profile = settings.Resolve(graphics.SupportsPostProcessing, diagnostics.Device.IsSoftwareAdapter,
            Math.Max(1, width), Math.Max(1, height), diagnostics.Surface.BufferCount,
            graphics.AvailableMemoryBudgetBytes, graphics.SupportsParticleRendering,
            graphics.SupportsDirectionalShadows, graphics.SupportsAtmosphere);
        return new(profile, profile.Environment.ResolveLighting(lighting), atmosphere with { Quality = profile.Atmosphere });
    }

    internal void Apply(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphics.IsRecording) throw new InvalidOperationException("Render composition cannot change while recording a frame.");
        var output = new GraphicsSceneOutputSettings(Profile.LinearScene, Lighting.Exposure,
            Lighting.ToneMapping == SceneToneMappingMode.AcesFitted, Profile.ResolutionScale);
        output.Validate(); Atmosphere.Validate(); Profile.PostProcessing.Validate(); Profile.Shadows.Validate();
        var previous = new DeviceSettings(graphics.SceneOutput, graphics.PostProcessing,
            graphics.Atmosphere, graphics.DirectionalShadows, graphics.ParticleDepthEnabled);
        try
        {
            Reset(graphics);
            graphics.ConfigureSceneOutput(output);
            graphics.ConfigureAtmosphere(Atmosphere);
            graphics.ConfigureDirectionalShadows(Profile.Shadows);
            graphics.ConfigurePostProcessing(Profile.PostProcessing);
        }
        catch (Exception configurationError)
        {
            try
            {
                Reset(graphics);
                graphics.ConfigureSceneOutput(previous.Output);
                graphics.ConfigureAtmosphere(previous.Atmosphere);
                graphics.ConfigureDirectionalShadows(previous.Shadows);
                graphics.ConfigureParticleDepth(previous.ParticleDepth);
                graphics.ConfigurePostProcessing(previous.PostProcessing);
            }
            catch (Exception restorationError)
            {
                throw new GraphicsDeviceException("Render composition failed and the previous configuration could not be restored; restart the session.",
                    new AggregateException(configurationError, restorationError));
            }
            throw;
        }
    }

    internal static void Reset(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphics.IsRecording) throw new InvalidOperationException("Render composition cannot reset while recording a frame.");
        // Remove dependent effects before switching scene formats or scale. Each native
        // setter owns its existing fence wait, target retirement and measurement reset.
        graphics.ConfigurePostProcessing(GraphicsPostProcessingSettings.Neutral);
        graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default);
        graphics.ConfigureDirectionalShadows(default);
        graphics.ConfigureParticleDepth(false);
    }

    private readonly record struct DeviceSettings(GraphicsSceneOutputSettings Output,
        GraphicsPostProcessingSettings PostProcessing, GraphicsAtmosphereSettings Atmosphere,
        GraphicsDirectionalShadowSettings Shadows, bool ParticleDepth);
}
