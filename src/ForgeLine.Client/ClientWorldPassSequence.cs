using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal interface IClientWorldPassRecorder
{
    int TerrainShadow(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumDraws);
    void InstanceShadows(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumCasters, int maximumDraws);
    void Terrain(IGraphicsCommandContext context);
    void PrepareParticles(IGraphicsCommandContext context);
    void Instances(IGraphicsCommandContext context);
    void Particles(IGraphicsCommandContext context);
}

// Fixed synchronous execution, not a registry of allegedly implemented effects.
internal static class ClientWorldPassSequence
{
    internal static void Record<T>(IGraphicsCommandContext context, GraphicsAtmosphereView? atmosphere,
        GraphicsDirectionalShadowFrame? shadow, GraphicsDirectionalShadowSettings shadows, ref T recorder)
        where T : struct, IClientWorldPassRecorder
    {
        ArgumentNullException.ThrowIfNull(context);
        shadows.Validate(); atmosphere?.Validate(); shadow?.Validate();
        if (shadows.Enabled != shadow.HasValue)
            throw new ArgumentException("Enabled shadows require a matching frame input; disabled shadows must not submit one.");
        context.BeginPass(GraphicsFramePass.World);
        if (atmosphere.HasValue) context.SetAtmosphereView(atmosphere.Value);
        if (shadow is { } shadowFrame)
        {
            context.BeginDirectionalShadowPass(shadowFrame);
            try
            {
                int terrainBudget = Math.Min(128, Math.Min(shadows.MaximumDraws, shadows.MaximumCasters));
                int draws = recorder.TerrainShadow(context, shadowFrame, terrainBudget);
                if (draws < 0 || draws > terrainBudget)
                    throw new InvalidOperationException("Terrain shadow submission exceeded its existing draw/caster budget.");
                recorder.InstanceShadows(context, shadowFrame, shadows.MaximumCasters - draws, shadows.MaximumDraws - draws);
            }
            finally { context.EndDirectionalShadowPass(); }
        }
        recorder.Terrain(context);
        recorder.PrepareParticles(context);
        recorder.Instances(context);
        recorder.Particles(context);
        // D3D12 executes its existing composition draw and restores native overlay
        // targets/depth here. No world submission is allowed after this marker.
        context.BeginPass(GraphicsFramePass.Overlay);
    }
}

internal readonly struct ClientWorldPassRecorder(TerrainRenderer terrain, SimpleInstanceRenderer instances,
    ParticleVfxRenderer particles, RenderWorld world, RtsCamera camera, float alpha, double visualSeconds,
    double particleDelta, bool paused) : IClientWorldPassRecorder
{
    public int TerrainShadow(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumDraws) =>
        terrain.RenderShadow(context, frame, maximumDraws);
    public void InstanceShadows(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumCasters, int maximumDraws) =>
        instances.RenderShadow(context, camera, world, alpha, frame, maximumCasters, maximumDraws, visualSeconds);
    public void Terrain(IGraphicsCommandContext context) => terrain.Render(context, camera);
    public void PrepareParticles(IGraphicsCommandContext context) =>
        particles.Prepare(world, camera, context.Width, context.Height, particleDelta, paused);
    public void Instances(IGraphicsCommandContext context) => instances.Render(context, camera, world, alpha, visualSeconds, particles);
    public void Particles(IGraphicsCommandContext context) => particles.Render(context, camera);
}
