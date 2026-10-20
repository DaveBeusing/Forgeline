using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Presentation.EffectSubmission;

namespace ForgeLine.Presentation;

public readonly record struct ParticleVfxDiagnostics(ParticleQuality Quality, int ActiveParticles, int PeakParticles,
    int ActiveEmitters, int Capacity, ulong EmittedParticles, ulong DroppedParticles, int FallbackEffects,
    int DrawCalls, long BufferBytes, double PreparationCpuMilliseconds, float ScreenCoverage)
{
    public ParticleTypeCounts ByType { get; init; }
    public int SuppressedEffects { get; init; }
}

public readonly record struct ParticleTypeCounts(int Smoke, int Dust, int Sparks, int Blast, int Fire, int Muzzle);

public sealed class ParticleVfxRenderer : IDisposable
{
    private readonly ParticleSettings _settings;
    private readonly SceneLightingSettings _lighting;
    private readonly GpuParticleRenderer? _renderer;
    private readonly ParticleEffectSubmission _submission;
    private readonly bool _linearOutput;
    private bool _disposed;

    public ParticleVfxRenderer(IGraphicsDevice graphics, RuntimeAssetCatalog? catalog, ParticleSettings? settings = null,
        SceneLightingSettings? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _settings = settings ?? ParticleSettings.Default; _settings.Validate();
        _lighting = lighting ?? SceneLightingSettings.Default; _lighting.Validate();
        _linearOutput = graphics.SceneOutput.Enabled;
        var available = new bool[ParticleEffectDefinitions.All.Length];
        if (_settings.Capacity == 0 || !graphics.SupportsParticleRendering || catalog is null)
        {
            if (graphics.SupportsParticleRendering) graphics.ConfigureParticleDepth(false);
            _submission = new(_settings, available);
            return;
        }
        foreach (var definition in VfxPresentationCatalog.All)
        {
            if (!ParticleEmitterDescriptor.TryResolve(definition.Kind, out var descriptor)) continue;
            descriptor.Validate();
            available[(int)definition.Kind] = catalog.TryGet(AssetId.Parse(descriptor.MeshAssetId), out var mesh) && mesh?.Type == RuntimeAssetType.Mesh &&
                catalog.TryGet(AssetId.Parse(descriptor.MaterialAssetId), out var material) && material?.Type == RuntimeAssetType.Material;
            if (available[(int)definition.Kind])
            {
                try
                {
                    _ = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(descriptor.MeshAssetId)).Payload);
                    _ = RuntimeMaterialData.FromPayload(catalog.Read(AssetId.Parse(descriptor.MaterialAssetId)).Payload);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
                {
                    available[(int)definition.Kind] = false;
                }
            }
        }
        _submission = new(_settings, available);
        if (available.Contains(true)) _renderer = new(graphics, _settings.Capacity);
    }

    internal ReadOnlySpan<GraphicsParticle> Particles => _submission.Particles;
    public ParticleVfxDiagnostics Diagnostics { get; private set; }
    public int MaximumMeshFallbackEffects => _settings.MaximumFallbackEffects;
    public bool Replaces(in RenderInstance instance) => _submission.Replaces(instance);

    public void Prepare(RenderWorld world, RtsCamera camera, int width, int height, double deltaSeconds, bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _submission.Prepare(world, camera, width, height, deltaSeconds, paused);
        Diagnostics = _submission.Diagnostics with { BufferBytes = _renderer?.BufferBytes ?? 0 };
    }

    public void Render(IGraphicsCommandContext context, RtsCamera camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderer is null || Particles.IsEmpty) return;
        CameraMatrices matrices = camera.GetMatrices(context.Width, context.Height);
        Matrix4x4.Invert(matrices.View, out var inverse);
        _renderer.Render(context, Particles, matrices.ViewProjection, new(inverse.M11, inverse.M12, inverse.M13),
            new(inverse.M21, inverse.M22, inverse.M23), -matrices.Projection.M33, matrices.Projection.M43,
            _lighting.Exposure, _lighting.ToneMapping == SceneToneMappingMode.AcesFitted, _linearOutput);
        Diagnostics = Diagnostics with { DrawCalls = 1 };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _renderer?.Dispose();
        _submission.Clear();
        _disposed = true;
    }
}
