namespace ForgeLine.Graphics;

public interface IGraphicsDevice : IDisposable
{
    ulong? AvailableMemoryBudgetBytes => null;
    bool SupportsPostProcessing => false;
    GraphicsPostProcessingSettings PostProcessing => GraphicsPostProcessingSettings.Neutral;
    void ConfigurePostProcessing(GraphicsPostProcessingSettings settings)
    {
        settings.Validate();
        if (settings != GraphicsPostProcessingSettings.Neutral) throw new NotSupportedException("Postprocessing is unsupported.");
    }
    bool SupportsParticleRendering => false;
    void ConfigureParticleDepth(bool enabled) { if (enabled) throw new NotSupportedException("Depth-aware particles are unsupported."); }
    GraphicsAtmosphereSettings Atmosphere => GraphicsAtmosphereSettings.Default;
    void ConfigureAtmosphere(GraphicsAtmosphereSettings settings)
    {
        settings.Validate();
        if (settings.Enabled) throw new NotSupportedException("Atmospheric depth composition is unsupported by this graphics device.");
    }
    GraphicsDirectionalShadowSettings DirectionalShadows => default;
    void ConfigureDirectionalShadows(GraphicsDirectionalShadowSettings settings)
    {
        settings.Validate();
        if (settings.Enabled) throw new NotSupportedException("Directional shadows are unsupported by this graphics device.");
    }
    // The device and its resources have one creating-thread owner. Command contexts
    // are valid only inside RenderFrame's callback. A frame index is reusable only
    // after its previous GPU submission completes. Resource disposal invalidates
    // the wrapper immediately; native release follows its final GPU use.
    GraphicsDiagnostics Diagnostics { get; }

    GraphicsSceneOutputSettings SceneOutput => new(false, 1, true);

    void ConfigureSceneOutput(GraphicsSceneOutputSettings settings)
    {
        settings.Validate();
        if (settings.Enabled)
            throw new NotSupportedException("This graphics device does not support linear scene composition.");
    }

    // Advances only after a non-occluded successful swap-chain presentation.
    ulong PresentedFrameCount => Diagnostics.Surface.PresentedFrameCount;

    IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description);

    IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description);

    IGraphicsTexture CreateTexture(
        GraphicsTextureData texture) =>
        throw new NotSupportedException(
            "This graphics device does not support textures.");

    void RenderFrame(
        GraphicsColor clearColor,
        Action<IGraphicsCommandContext>? recordCommands = null);

    void Resize(int width, int height);

    void WaitForIdle();
}
