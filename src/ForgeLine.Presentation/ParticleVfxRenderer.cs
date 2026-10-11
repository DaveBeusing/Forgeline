using System.Diagnostics;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct ParticleVfxDiagnostics(ParticleQuality Quality, int ActiveParticles, int PeakParticles,
    int ActiveEmitters, int Capacity, ulong EmittedParticles, ulong DroppedParticles, int FallbackEffects,
    int DrawCalls, long BufferBytes, double PreparationCpuMilliseconds, float ScreenCoverage)
{
    public ParticleTypeCounts ByType { get; init; }
}

public readonly record struct ParticleTypeCounts(int Smoke, int Dust, int Sparks, int Blast, int Fire, int Muzzle);

public sealed class ParticleVfxRenderer : IDisposable
{
    private const int EmitterCapacity = 2048;
    private readonly ParticleSettings _settings;
    private readonly SceneLightingSettings _lighting;
    private readonly GpuParticleRenderer? _renderer;
    private readonly bool _linearOutput;
    private readonly bool[] _available = new bool[37];
    private readonly GraphicsParticle[] _particles;
    private readonly Candidate[] _candidates = new Candidate[EmitterCapacity];
    private readonly Dictionary<EffectKey, int> _emitters = new(EmitterCapacity);
    private readonly double[] _starts = new double[EmitterCapacity];
    private readonly ulong[] _cycles = new ulong[EmitterCapacity];
    private readonly bool[] _occupied = new bool[EmitterCapacity];
    private readonly HashSet<EffectKey> _replaced = new(EmitterCapacity);
    private readonly HashSet<EffectKey> _authorized = new(EmitterCapacity);
    private readonly EffectKey[] _retire = new EffectKey[EmitterCapacity];
    private readonly ParticleDepthComparer _depthComparer = new();
    private SimulationSessionId _session;
    private ulong _lastTick;
    private double _time;
    private int _count, _peak;
    private ulong _emitted, _dropped;
    private bool _disposed;

    public ParticleVfxRenderer(IGraphicsDevice graphics, RuntimeAssetCatalog? catalog, ParticleSettings? settings = null,
        SceneLightingSettings? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _settings = settings ?? ParticleSettings.Default; _settings.Validate();
        _lighting = lighting ?? SceneLightingSettings.Default; _lighting.Validate();
        _linearOutput = graphics.SceneOutput.Enabled;
        _particles = new GraphicsParticle[_settings.Capacity];
        if (_settings.Capacity == 0 || !graphics.SupportsParticleRendering || catalog is null)
        {
            if (graphics.SupportsParticleRendering) graphics.ConfigureParticleDepth(false);
            return;
        }
        foreach (var definition in VfxPresentationCatalog.All)
        {
            if (!ParticleEmitterDescriptor.TryResolve(definition.Kind, out var descriptor)) continue;
            descriptor.Validate();
            _available[(int)definition.Kind] = catalog.TryGet(AssetId.Parse(descriptor.MeshAssetId), out var mesh) && mesh?.Type == RuntimeAssetType.Mesh &&
                catalog.TryGet(AssetId.Parse(descriptor.MaterialAssetId), out var material) && material?.Type == RuntimeAssetType.Material;
            if (_available[(int)definition.Kind])
            {
                try
                {
                    _ = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(descriptor.MeshAssetId)).Payload);
                    _ = RuntimeMaterialData.FromPayload(catalog.Read(AssetId.Parse(descriptor.MaterialAssetId)).Payload);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
                {
                    _available[(int)definition.Kind] = false;
                }
            }
        }
        if (_available.Contains(true)) _renderer = new(graphics, _settings.Capacity);
    }

    internal ReadOnlySpan<GraphicsParticle> Particles => _particles.AsSpan(0, _count);
    public ParticleVfxDiagnostics Diagnostics { get; private set; }
    public bool Replaces(in RenderInstance instance) => _replaced.Contains(Key(instance));

    public void Prepare(RenderWorld world, RtsCamera camera, int width, int height, double deltaSeconds, bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(camera);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        long started = Stopwatch.GetTimestamp();
        _count = 0; _replaced.Clear(); _authorized.Clear();
        var snapshot = world.CurrentSnapshot;
        if (snapshot is not null && (snapshot.SessionId != _session || snapshot.Tick.Value < _lastTick))
        {
            _emitters.Clear(); Array.Clear(_occupied); _time = 0; _emitted = _dropped = 0; _peak = 0; _session = snapshot.SessionId;
        }
        if (snapshot is not null) _lastTick = snapshot.Tick.Value;
        if (!paused) _time += Math.Min(deltaSeconds, .1);
        int candidateCount = 0, fallback = 0;
        float coverage = 0;
        Span<int> byType = stackalloc int[6];
        byType.Clear();
        var matrices = camera.GetMatrices(width,height);
        var frustum = ViewFrustum.FromViewProjection(matrices.ViewProjection);
        if (snapshot is not null)
        for (int i = 0; i < world.InstanceCount; i++)
        {
            RenderInstance instance = snapshot.GetInstance(i);
            if (!instance.VfxFeature.IsSpecified || (instance.Visibility & RenderVisibilityMask.World) == 0) continue;
            Vector3 position = instance.Transform.Position;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) || !instance.Mesh.IsValid || !instance.Material.IsValid)
                continue;
            if (_renderer is null || (uint)instance.VfxFeature.Kind >= _available.Length || !_available[(int)instance.VfxFeature.Kind])
            { fallback++; continue; }
            EffectKey key = Key(instance);
            if (_authorized.Count < EmitterCapacity) _authorized.Add(key);
            if (!_authorized.Contains(key)) { fallback++; continue; }
            ParticleEmitterDescriptor.TryResolve(instance.VfxFeature.Kind, out var descriptor);
            float distance = Vector3.Distance(camera.Position, instance.Transform.Position);
            if (!float.IsFinite(distance) || !VfxPresentationCatalog.ShouldRender(descriptor.Effect, distance)) continue;
            float reach = descriptor.SpeedMetersPerSecond * descriptor.LifetimeSeconds +
                .5f * MathF.Abs(descriptor.Gravity) * descriptor.LifetimeSeconds * descriptor.LifetimeSeconds + 16;
            var bounds = new AxisAlignedBounds(instance.Transform.Position - new Vector3(reach), instance.Transform.Position + new Vector3(reach));
            if (!frustum.Intersects(bounds)) continue;
            float radius = descriptor.RadiusMeters;
            float nearestDepth = MathF.Max(camera.Settings.NearPlane, -Vector3.Transform(position,matrices.View).Z - reach);
            float pixelsPerMeter = height * matrices.Projection.M22 / nearestDepth * .5f;
            if (descriptor.Priority == 2 && radius * pixelsPerMeter * 2 < 2) continue;
            if (descriptor.Visual == ParticleVisualKind.Explosion) radius = MathF.Min(8,MathF.Max(radius,3 / pixelsPerMeter));
            radius = MathF.Min(radius, 24 / MathF.Max(.001f, pixelsPerMeter)); // growth <=2 => maximum 96-pixel diameter
            int count = _settings.Quality == ParticleQuality.Low ? Math.Max(2, descriptor.Count / 2) : descriptor.Count;
            float area = count * MathF.Pow(radius * 4 * pixelsPerMeter, 2) / (width * (float)height);
            if (candidateCount == _candidates.Length) { _dropped += (uint)count; fallback++; continue; }
            _candidates[candidateCount++] = new(key, instance, descriptor, distance, radius, count, area);
        }
        int retireCount = 0;
        foreach (var pair in _emitters) if (!_authorized.Contains(pair.Key)) _retire[retireCount++] = pair.Key;
        for (int i = 0; i < retireCount; i++) { _occupied[_emitters[_retire[i]]] = false; _emitters.Remove(_retire[i]); }
        SortBounded(_candidates.AsSpan(0,candidateCount),CandidatePriorityComparer.Instance);
        int activeEmitters = 0;
        for (int i = 0; i < candidateCount; i++)
        {
            var candidate = _candidates[i];
            if (candidate.Count > _particles.Length - _count || coverage + candidate.Area > _settings.MaximumScreenCoverage)
            { _dropped += (uint)candidate.Count; fallback++; continue; }
            if (!_emitters.TryGetValue(candidate.Key, out var state))
            {
                if (_emitters.Count == EmitterCapacity) { _dropped += (uint)candidate.Count; fallback++; continue; }
                double sourceAge = candidate.Instance.VfxFeature.SpawnedAtTick > 0 && snapshot is not null && snapshot.Tick.Value >= candidate.Instance.VfxFeature.SpawnedAtTick
                    ? (snapshot.Tick.Value - candidate.Instance.VfxFeature.SpawnedAtTick) * snapshot.TickDuration.TotalSeconds : 0;
                state = Array.IndexOf(_occupied,false);
                _occupied[state] = true; _starts[state] = _time - sourceAge; _cycles[state] = 0;
                _emitters.Add(candidate.Key,state);
                if(candidate.Instance.VfxFeature.Continuous || sourceAge < candidate.Descriptor.LifetimeSeconds)
                    _emitted += (uint)candidate.Count;
            }
            _replaced.Add(candidate.Key);
            float elapsed = (float)Math.Max(0, _time - _starts[state]);
            float life = candidate.Descriptor.LifetimeSeconds;
            if (!candidate.Instance.VfxFeature.Continuous && elapsed >= life) continue;
            activeEmitters++;
            byType[(int)candidate.Descriptor.Visual] += candidate.Count;
            coverage += candidate.Area;
            for (int particle = 0; particle < candidate.Count; particle++)
            {
                uint seed = Seed(candidate.Key, particle);
                float a = (seed & 65535) / 65535f * MathF.Tau;
                float spread = .3f + ((seed >> 16) & 255) / 255f * .7f;
                Vector3 velocity = candidate.Descriptor.InitialVelocity(a, spread, candidate.Instance.Transform.Rotation);
                float age = candidate.Instance.VfxFeature.Continuous ? (elapsed + particle * life / candidate.Count) % life : elapsed;
                Vector4 tint = VfxPresentationCatalog.Get(candidate.Descriptor.Effect).FallbackTint;
                tint.W = candidate.Descriptor.Visual switch { ParticleVisualKind.Smoke => .24f, ParticleVisualKind.Dust => .18f, ParticleVisualKind.Spark => .65f, ParticleVisualKind.Fire => .48f, _ => .6f };
                _particles[_count++] = new(new(candidate.Instance.Transform.Position, age), new(velocity, life), tint,
                    new(candidate.Radius, candidate.Descriptor.Gravity, (float)candidate.Descriptor.Visual, a));
            }
            if (candidate.Instance.VfxFeature.Continuous)
            {
                ulong cycles = (ulong)(elapsed / life);
                if (cycles > _cycles[state]) _emitted += (cycles - _cycles[state]) * (uint)candidate.Count;
                _cycles[state] = cycles;
            }
        }
        // Stable far-to-near ordering within each priority does not cover layers
        // across priorities; sort the final bounded particle set by anchor depth.
        _depthComparer.Camera = camera.Position;
        SortBounded(_particles.AsSpan(0,_count),_depthComparer);
        _peak = Math.Max(_peak,_count);
        Diagnostics = new(_settings.Quality,_count,_peak,activeEmitters,_settings.Capacity,_emitted,_dropped,fallback,
            0,_renderer?.BufferBytes ?? 0,Stopwatch.GetElapsedTime(started).TotalMilliseconds,coverage)
        { ByType = new(byType[0], byType[1], byType[2], byType[3], byType[4], byType[5]) };
    }

    public void Render(IGraphicsCommandContext context, RtsCamera camera)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_renderer is null || _count == 0) return;
        CameraMatrices matrices = camera.GetMatrices(context.Width,context.Height);
        Matrix4x4.Invert(matrices.View,out var inverse);
        _renderer.Render(context,Particles,matrices.ViewProjection,new(inverse.M11,inverse.M12,inverse.M13),
            new(inverse.M21,inverse.M22,inverse.M23),matrices.Projection.M33,matrices.Projection.M43,
            _lighting.Exposure,_lighting.ToneMapping == SceneToneMappingMode.AcesFitted,_linearOutput);
        Diagnostics = Diagnostics with { DrawCalls = 1 };
    }

    private static EffectKey Key(in RenderInstance instance) => new(instance.VfxFeature.EmitterId != 0 ? instance.VfxFeature.EmitterId :
        ((ulong)instance.Entity.Generation << 32) | instance.Entity.Index,instance.VfxFeature.EmitterGeneration,instance.VfxFeature.Kind);
    private static uint Seed(EffectKey key,int index)
    {
        uint value = unchecked((uint)key.Id ^ (uint)(key.Id >> 32) ^ key.Generation * 747796405U ^ (uint)key.Kind * 2891336453U ^ (uint)index * 277803737U);
        value ^= value >> 16; value = unchecked(value * 2246822519U); return value ^ (value >> 13);
    }
    public void Dispose() { if (_disposed) return; _renderer?.Dispose(); _emitters.Clear(); _replaced.Clear(); _disposed = true; }
    // The framework comparer overload creates comparison delegates on each call.
    // Iterative heap sorting keeps both fixed-size render streams allocation-free.
    private static void SortBounded<T>(Span<T> values,IComparer<T> comparer)
    {
        for(int root=values.Length/2-1;root>=0;root--) Sift(values,root,comparer);
        for(int end=values.Length-1;end>0;end--)
        {
            (values[0],values[end])=(values[end],values[0]);
            Sift(values[..end],0,comparer);
        }
    }
    private static void Sift<T>(Span<T> values,int root,IComparer<T> comparer)
    {
        while(root*2+1<values.Length)
        {
            int child=root*2+1;
            if(child+1<values.Length && comparer.Compare(values[child],values[child+1])<0)child++;
            if(comparer.Compare(values[root],values[child])>=0)return;
            (values[root],values[child])=(values[child],values[root]); root=child;
        }
    }
    private readonly record struct EffectKey(ulong Id,uint Generation,VfxEffectKind Kind);
    private readonly record struct Candidate(EffectKey Key,RenderInstance Instance,ParticleEmitterDescriptor Descriptor,float Distance,float Radius,int Count,float Area);
    private sealed class CandidatePriorityComparer : IComparer<Candidate>
    {
        public static CandidatePriorityComparer Instance { get; } = new();
        public int Compare(Candidate a,Candidate b)
        {
            int order=a.Descriptor.Priority.CompareTo(b.Descriptor.Priority);
            if(order != 0)return order;
            order=a.Distance.CompareTo(b.Distance);
            if(order != 0)return order;
            order=a.Key.Id.CompareTo(b.Key.Id);
            if (order != 0) return order;
            order = a.Key.Generation.CompareTo(b.Key.Generation);
            return order != 0 ? order : a.Key.Kind.CompareTo(b.Key.Kind);
        }
    }
    private sealed class ParticleDepthComparer : IComparer<GraphicsParticle>
    {
        public Vector3 Camera { get; set; }
        public int Compare(GraphicsParticle a,GraphicsParticle b) => Vector3.DistanceSquared(new(b.OriginAge.X,b.OriginAge.Y,b.OriginAge.Z),Camera)
            .CompareTo(Vector3.DistanceSquared(new(a.OriginAge.X,a.OriginAge.Y,a.OriginAge.Z),Camera));
    }
}
