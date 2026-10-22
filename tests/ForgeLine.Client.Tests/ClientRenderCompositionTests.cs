using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

// Source contracts awaiting manual execution; fake devices do not qualify native rendering.
public sealed class ClientRenderCompositionTests
{
    [Fact]
    public void UnsupportedEffectsAreDisabledIndividuallyWithoutChangingRequestedSettings()
    {
        using var device = new CompositionDevice { SupportsAtmosphere = false, SupportsDirectionalShadows = false };
        var requested = new RenderQualitySettings { Quality = RenderQuality.Ultra };
        var configuration = Resolve(device, requested);
        Assert.True(configuration.Profile.LinearScene);
        Assert.Equal(DirectionalShadowQuality.Disabled, configuration.Profile.Shadows.Quality);
        Assert.Equal(AtmosphereQuality.Disabled, configuration.Profile.Atmosphere);
        Assert.True(configuration.Profile.Particles.Capacity > 0);
        Assert.Contains("Directional shadows are unsupported", configuration.Profile.FallbackReason);
        Assert.Contains("Atmospheric depth composition is unsupported", configuration.Profile.FallbackReason);
        Assert.Equal(requested, configuration.Profile.RequestedSettings);
        configuration.Apply(device);
        Assert.Equal(configuration.Profile.PostProcessing, device.PostProcessing);
    }

    [Fact]
    public void UnsupportedCompositionUsesNativeDirectOutputAndKeepsRequestedScale()
    {
        using var device = new CompositionDevice { SupportsPostProcessing = false };
        var configuration = Resolve(device, new() { Quality = RenderQuality.High, ResolutionScale = .75f });
        Assert.False(configuration.Profile.LinearScene);
        Assert.Equal(1, configuration.Profile.ResolutionScale);
        Assert.Equal(.75f, configuration.Profile.RequestedSettings.ResolutionScale);
        configuration.Apply(device);
        Assert.False(device.SceneOutput.Enabled);
        Assert.False(device.Atmosphere.Enabled);
        Assert.False(device.DirectionalShadows.Enabled);
        Assert.Equal(0, configuration.Profile.Particles.Capacity);
    }

    [Fact]
    public void ActualSwapchainBufferCountDeterminesTargetAdmission()
    {
        using var device = new CompositionDevice { Buffers = 2, AvailableMemoryBudgetBytes = 50UL * 1024 * 1024 };
        var requested = new RenderQualitySettings
        {
            Quality = RenderQuality.High, Shadows = DirectionalShadowQuality.Disabled,
            Particles = ParticleQuality.Disabled, Textures = TextureDetailQuality.Quarter
        };
        Assert.True(ClientRenderConfiguration.Resolve(device, requested, 1920, 1080,
            SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default).Profile.LinearScene);
        device.Buffers = 3;
        Assert.False(ClientRenderConfiguration.Resolve(device, requested, 1920, 1080,
            SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default).Profile.LinearScene);
    }

    [Fact]
    public void ExistingEffectsAreRetiredBeforeSceneReconfigurationAndParticleDepthDoesNotLeak()
    {
        using var device = new CompositionDevice();
        device.ConfigureSceneOutput(new(true, 1, true, .75f));
        device.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Height });
        device.ConfigureParticleDepth(true);
        device.Calls.Clear();
        Resolve(device, new() { Quality = RenderQuality.Low }).Apply(device);
        Assert.Equal(new[] { "post", "atmosphere", "shadows", "particles", "scene", "atmosphere", "shadows", "post" }, device.Calls);
        Assert.False(device.ParticleDepthEnabled);
        Assert.Equal(1, device.SceneOutput.ResolutionScale);
    }

    [Fact]
    public void FailedConfigurationRestoresPreviousEffectsAndReportsOriginalFailure()
    {
        using var device = new CompositionDevice();
        var old = Resolve(device, new() { Quality = RenderQuality.High, ResolutionScale = .75f });
        old.Apply(device);
        device.ConfigureParticleDepth(true);
        device.FailNext = "scene";
        Assert.Throws<InvalidOperationException>(() => Resolve(device, new() { Quality = RenderQuality.Low }).Apply(device));
        Assert.True(device.SceneOutput.Enabled);
        Assert.Equal(.75f, device.SceneOutput.ResolutionScale);
        Assert.Equal(old.Atmosphere, device.Atmosphere);
        Assert.Equal(old.Profile.Shadows, device.DirectionalShadows);
        Assert.Equal(old.Profile.PostProcessing, device.PostProcessing);
        Assert.True(device.ParticleDepthEnabled);
    }

    [Fact]
    public void FailedRestorationBecomesAnExplicitGraphicsSessionFault()
    {
        using var device = new CompositionDevice { FailAlways = "scene" };
        var error = Assert.Throws<GraphicsDeviceException>(() => Resolve(device, new()).Apply(device));
        var causes = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(2, causes.InnerExceptions.Count);
    }

    [Fact]
    public void RecordingRejectsReconfigurationBeforeAnyDeviceMutation()
    {
        using var device = new CompositionDevice { IsRecording = true };
        var configuration = Resolve(device, new());
        Assert.Throws<InvalidOperationException>(() => configuration.Apply(device));
        Assert.Throws<InvalidOperationException>(() => ClientRenderConfiguration.Reset(device));
        Assert.Empty(device.Calls);
    }

    [Fact]
    public void EmptySurfaceSuspendsOnceAndResumeQueuesLatestSizeWithoutChangingStateOnFailure()
    {
        using var device = new CompositionDevice();
        var surface = new ClientRenderSurface(640, 360, false);
        Assert.False(surface.Update(device, 0, 360, false, out bool suspended));
        Assert.True(suspended);
        Assert.False(surface.Update(device, 0, 0, true, out bool repeated));
        Assert.False(repeated);
        Assert.Equal(new[] { (0, 0) }, device.ResizeRequests);
        device.FailNext = "resize";
        Assert.Throws<InvalidOperationException>(() => surface.Update(device, 1920, 1080, false, out _));
        Assert.True(surface.Suspended);
        Assert.Equal(640, surface.Width);
        Assert.True(surface.Update(device, 3840, 2160, false, out bool resumed));
        Assert.True(resumed);
        Assert.Equal(3840, surface.Width);
        Assert.True(surface.Update(device, 3840, 2160, false, out bool same));
        Assert.False(same);
        Assert.Equal(3, device.ResizeRequests.Count);
    }

    private static ClientRenderConfiguration Resolve(IGraphicsDevice device, RenderQualitySettings requested) =>
        ClientRenderConfiguration.Resolve(device, requested, 640, 360, SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default);

    private sealed class CompositionDevice : IGraphicsDevice
    {
        public bool SupportsPostProcessing { get; init; } = true;
        public bool SupportsAtmosphere { get; init; } = true;
        public bool SupportsDirectionalShadows { get; init; } = true;
        public bool SupportsParticleRendering => true;
        public bool IsRecording { get; init; }
        public int Buffers { get; set; } = 3;
        public ulong? AvailableMemoryBudgetBytes { get; init; }
        public GraphicsSceneOutputSettings SceneOutput { get; private set; } = new(false, 1, true);
        public GraphicsAtmosphereSettings Atmosphere { get; private set; } = GraphicsAtmosphereSettings.Default;
        public GraphicsDirectionalShadowSettings DirectionalShadows { get; private set; }
        public GraphicsPostProcessingSettings PostProcessing { get; private set; } = GraphicsPostProcessingSettings.Neutral;
        public bool ParticleDepthEnabled { get; private set; }
        public List<string> Calls { get; } = [];
        public List<(int Width, int Height)> ResizeRequests { get; } = [];
        internal string? FailNext { get; set; }
        internal string? FailAlways { get; init; }
        public GraphicsDiagnostics Diagnostics => new(new("composition", 0, false, "test", false),
            new(640, 360, Buffers, 0, false, "test"));
        public void ConfigureSceneOutput(GraphicsSceneOutputSettings settings)
        {
            settings.Validate(); Touch("scene");
            if (!settings.Enabled && (Atmosphere.Enabled || PostProcessing != GraphicsPostProcessingSettings.Neutral))
                throw new InvalidOperationException("Dependent effects must retire first.");
            SceneOutput = settings;
        }
        public void ConfigureAtmosphere(GraphicsAtmosphereSettings settings) { settings.Validate(); Touch("atmosphere"); Atmosphere = settings; }
        public void ConfigureDirectionalShadows(GraphicsDirectionalShadowSettings settings) { settings.Validate(); Touch("shadows"); DirectionalShadows = settings; }
        public void ConfigurePostProcessing(GraphicsPostProcessingSettings settings) { settings.Validate(); Touch("post"); PostProcessing = settings; }
        public void ConfigureParticleDepth(bool enabled) { Touch("particles"); ParticleDepthEnabled = enabled; }
        public void Resize(int width, int height) { ResizeRequests.Add((width, height)); Touch("resize"); }
        private void Touch(string operation)
        {
            Calls.Add(operation);
            if (FailNext == operation) { FailNext = null; throw new InvalidOperationException("Injected " + operation); }
            if (FailAlways == operation) throw new InvalidOperationException("Persistent " + operation);
        }
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => throw new NotSupportedException();
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => throw new NotSupportedException();
        public void RenderFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands = null) => throw new NotSupportedException();
        public void WaitForIdle() { }
        public void Dispose() { }
    }
}
