using System.Diagnostics;
using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation.EffectSubmission;

internal sealed class ParticleEffectSubmission
{
    private const int EmitterCapacity = 2048;
    private readonly ParticleSettings _settings;
    private readonly bool[] _available;
    private readonly GraphicsParticle[] _particles;
    private readonly ParticleDepthKey[] _depthKeys;
    private readonly Candidate[] _candidates = new Candidate[PresentationExtractor.MaximumSnapshotEffects];
    private readonly Dictionary<EffectKey, int> _emitters = new(EmitterCapacity);
    private readonly double[] _starts = new double[EmitterCapacity];
    private readonly ulong[] _cycles = new ulong[EmitterCapacity];
    private readonly bool[] _occupied = new bool[EmitterCapacity];
    private readonly HashSet<EffectKey> _meshFallback = new(128);
    private readonly HashSet<EffectKey> _currentEffects = new(PresentationExtractor.MaximumSnapshotEffects);
    // Authorization covers the complete published bound, before visibility/cost
    // selection. A rank of -1 preserves authorized offscreen emitter continuity.
    private readonly Dictionary<EffectKey, int> _authorized = new(PresentationExtractor.MaximumSnapshotEffects);
    private readonly EffectKey[] _retire = new EffectKey[EmitterCapacity];
    private SimulationSessionId _session;
    private ulong _lastTick;
    private double _time;
    private int _count, _peak;
    private ulong _emitted, _dropped;
    private bool _prepared;

    internal ParticleEffectSubmission(ParticleSettings settings, ReadOnlySpan<bool> available)
    {
        settings.Validate();
        _settings = settings;
        _available = new bool[ParticleEffectDefinitions.All.Length];
        if (available.Length != _available.Length) throw new ArgumentException("Effect availability must cover the catalog.", nameof(available));
        if (settings.Capacity > 0) available.CopyTo(_available);
        _particles = new GraphicsParticle[settings.Capacity];
        _depthKeys = new ParticleDepthKey[settings.Capacity];
    }

    internal ReadOnlySpan<GraphicsParticle> Particles => _particles.AsSpan(0, _count);
    internal ParticleVfxDiagnostics Diagnostics { get; private set; }
    internal int RetainedEmitterCount => _emitters.Count;
    internal bool Replaces(in RenderInstance instance) => _prepared && instance.VfxFeature.IsSpecified &&
        (instance.Visibility & RenderVisibilityMask.World) != 0 && _currentEffects.Contains(Key(instance)) && !_meshFallback.Contains(Key(instance));

    internal void Prepare(RenderWorld world, RtsCamera camera, int width, int height, double deltaSeconds, bool paused)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        long started = Stopwatch.GetTimestamp();
        _count = 0;
        _meshFallback.Clear();
        _currentEffects.Clear();
        _authorized.Clear();
        _prepared = true;
        var snapshot = world.CurrentSnapshot;
        if (snapshot is not null && (snapshot.SessionId != _session || snapshot.Tick.Value < _lastTick))
        {
            _emitters.Clear();
            Array.Clear(_occupied);
            _time = 0;
            _emitted = _dropped = 0;
            _peak = 0;
            _session = snapshot.SessionId;
        }
        if (snapshot is not null) _lastTick = snapshot.Tick.Value;
        if (!paused) _time += Math.Min(deltaSeconds, .1);
        int candidateCount = 0, fallback = 0, suppressed = 0;
        float coverage = 0;
        Span<int> byType = stackalloc int[6];
        byType.Clear();
        var matrices = camera.GetMatrices(width, height);
        var frustum = ViewFrustum.FromViewProjection(matrices.ViewProjection);
        if (snapshot is not null)
        for (int i = 0; i < world.InstanceCount; i++)
        {
            RenderInstance instance = snapshot.GetInstance(i);
            if (!instance.VfxFeature.IsSpecified || (instance.Visibility & RenderVisibilityMask.World) == 0) continue;
            Vector3 position = instance.Transform.Position;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) || !instance.Mesh.IsValid || !instance.Material.IsValid)
                continue;
            if (_currentEffects.Count == PresentationExtractor.MaximumSnapshotEffects) { suppressed++; continue; }
            EffectKey key = Key(instance);
            if (!_currentEffects.Add(key)) continue;
            int kind = (int)instance.VfxFeature.Kind;
            ParticleEffectDefinition definition = (uint)kind < ParticleEffectDefinitions.All.Length ? ParticleEffectDefinitions.All[kind] : default;
            float distance = Vector3.Distance(camera.Position, position);
            bool useFallback = (uint)kind >= _available.Length || !_available[kind] || !definition.Supported;
            if (!useFallback) _authorized.Add(key, -1);
            var descriptor = definition.Descriptor;
            if (!float.IsFinite(distance) || definition.MaximumDistance <= 0 || distance > definition.MaximumDistance) continue;
            float reach = useFallback ? MathF.Max(definition.Reach, instance.Transform.Scale.Length() * 2) : definition.Reach;
            if (!float.IsFinite(reach)) continue;
            var bounds = new AxisAlignedBounds(position - new Vector3(reach), position + new Vector3(reach));
            if (!frustum.Intersects(bounds)) continue;
            if (useFallback)
            {
                _candidates[candidateCount++] = new(key, i, distance, 0, 0, 0, definition.AdmissionPriority, true);
                continue;
            }
            float radius = descriptor.RadiusMeters;
            float nearestDepth = MathF.Max(camera.Settings.NearPlane, -Vector3.Transform(position, matrices.View).Z - reach);
            float pixelsPerMeter = height * matrices.Projection.M22 / nearestDepth * .5f;
            if (descriptor.Priority == 2 && radius * pixelsPerMeter * 2 < 2) continue;
            if (descriptor.Visual == ParticleVisualKind.Explosion) radius = MathF.Min(8, MathF.Max(radius, 3 / pixelsPerMeter));
            radius = MathF.Min(radius, 24 / MathF.Max(.001f, pixelsPerMeter));
            int count = _settings.Quality == ParticleQuality.Low ? Math.Max(2, descriptor.Count / 2) : descriptor.Count;
            float area = count * MathF.Pow(radius * 4 * pixelsPerMeter, 2) / (width * (float)height);
            _candidates[candidateCount++] = new(key, i, distance, radius, count, area, definition.AdmissionPriority, false);
        }

        int retireCount = 0;
        foreach (var pair in _emitters) if (!_authorized.ContainsKey(pair.Key)) _retire[retireCount++] = pair.Key;
        for (int i = 0; i < retireCount; i++) Retire(_retire[i]);
        SortCandidates(_candidates.AsSpan(0, candidateCount));
        for (int i = 0; i < candidateCount; i++)
            if (!_candidates[i].Fallback) _authorized[_candidates[i].Key] = i;

        int activeEmitters = 0;
        for (int i = 0; i < candidateCount; i++)
        {
            Candidate candidate = _candidates[i];
            if (candidate.Fallback)
            {
                if (fallback < _settings.MaximumFallbackEffects) { _meshFallback.Add(candidate.Key); fallback++; }
                else suppressed++;
                continue;
            }
            RenderInstance instance = snapshot!.GetInstance(candidate.InstanceIndex);
            ParticleEffectDefinition definition = ParticleEffectDefinitions.All[(int)candidate.Key.Kind];
            ParticleEmitterDescriptor descriptor = definition.Descriptor;
            bool existing = _emitters.TryGetValue(candidate.Key, out int state);
            double sourceAge = instance.VfxFeature.SpawnedAtTick > 0 && snapshot.Tick.Value >= instance.VfxFeature.SpawnedAtTick
                ? (snapshot.Tick.Value - instance.VfxFeature.SpawnedAtTick) * snapshot.TickDuration.TotalSeconds : 0;
            float elapsed = (float)Math.Max(0, existing ? _time - _starts[state] : sourceAge);
            float life = descriptor.LifetimeSeconds;
            if (!instance.VfxFeature.Continuous && elapsed >= life) continue;
            if (candidate.Count > _particles.Length - _count || coverage + candidate.Area > _settings.MaximumScreenCoverage)
            { _dropped += (uint)candidate.Count; suppressed++; continue; }
            if (!existing)
            {
                // Dormant cached sources must not block a higher-ranked visible
                // event when the fixed emitter pool is full. Never evict a source
                // already admitted earlier in this deterministic pass.
                if (_emitters.Count == EmitterCapacity && !RetireLowerRanked(i))
                { _dropped += (uint)candidate.Count; suppressed++; continue; }
                state = Array.IndexOf(_occupied, false);
                _occupied[state] = true;
                _starts[state] = _time - sourceAge;
                _cycles[state] = 0;
                _emitters.Add(candidate.Key, state);
                _emitted += (uint)candidate.Count;
            }
            activeEmitters++;
            byType[(int)descriptor.Visual] += candidate.Count;
            coverage += candidate.Area;
            for (int particle = 0; particle < candidate.Count; particle++)
            {
                uint seed = Seed(candidate.Key, particle);
                float angle = (seed & 65535) / 65535f * MathF.Tau;
                float spread = .3f + ((seed >> 16) & 255) / 255f * .7f;
                Vector3 velocity = descriptor.InitialVelocity(angle, spread, instance.Transform.Rotation);
                float age = instance.VfxFeature.Continuous ? (elapsed + particle * life / candidate.Count) % life : elapsed;
                _particles[_count++] = new(new(instance.Transform.Position, age), new(velocity, life), definition.Tint,
                    new(candidate.Radius, descriptor.Gravity, (float)descriptor.Visual, angle));
            }
            if (instance.VfxFeature.Continuous)
            {
                ulong cycles = (ulong)(elapsed / life);
                if (cycles > _cycles[state]) _emitted += (cycles - _cycles[state]) * (uint)candidate.Count;
                _cycles[state] = cycles;
            }
        }
        ParticleDepthOrdering.Sort(_particles.AsSpan(0, _count), _depthKeys, matrices.View);
        _peak = Math.Max(_peak, _count);
        Diagnostics = new(_settings.Quality, _count, _peak, activeEmitters, _settings.Capacity, _emitted, _dropped, fallback,
            0, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds, coverage)
        {
            ByType = new(byType[0], byType[1], byType[2], byType[3], byType[4], byType[5]),
            SuppressedEffects = suppressed
        };
    }

    internal void Clear()
    {
        _emitters.Clear();
        _meshFallback.Clear();
        _currentEffects.Clear();
        _authorized.Clear();
        _prepared = false;
        _count = 0;
    }

    private bool RetireLowerRanked(int currentRank)
    {
        EffectKey victim = default;
        int worstRank = currentRank;
        foreach (var pair in _emitters)
        {
            int rank = _authorized[pair.Key];
            if (rank < 0) rank = int.MaxValue;
            if (rank > worstRank || (rank == worstRank && CompareKeys(pair.Key, victim) > 0))
            {
                victim = pair.Key;
                worstRank = rank;
            }
        }
        if (worstRank <= currentRank) return false;
        Retire(victim);
        return true;
    }

    private void Retire(EffectKey key)
    {
        _occupied[_emitters[key]] = false;
        _emitters.Remove(key);
    }

    private static EffectKey Key(in RenderInstance instance) => new(instance.VfxFeature.EmitterId != 0 ? instance.VfxFeature.EmitterId :
        ((ulong)instance.Entity.Generation << 32) | instance.Entity.Index, instance.VfxFeature.EmitterGeneration, instance.VfxFeature.Kind);

    private static uint Seed(EffectKey key, int index)
    {
        uint value = unchecked((uint)key.Id ^ (uint)(key.Id >> 32) ^ key.Generation * 747796405U ^ (uint)key.Kind * 2891336453U ^ (uint)index * 277803737U);
        value ^= value >> 16;
        value = unchecked(value * 2246822519U);
        return value ^ (value >> 13);
    }

    private static int CompareKeys(EffectKey a, EffectKey b)
    {
        int order = a.Id.CompareTo(b.Id);
        if (order != 0) return order;
        order = a.Generation.CompareTo(b.Generation);
        return order != 0 ? order : a.Kind.CompareTo(b.Kind);
    }

    private static int CompareCandidates(in Candidate a, in Candidate b)
    {
        int order = a.Priority.CompareTo(b.Priority);
        if (order != 0) return order;
        order = a.Distance.CompareTo(b.Distance);
        return order != 0 ? order : CompareKeys(a.Key, b.Key);
    }

    private static void SortCandidates(Span<Candidate> values)
    {
        for (int root = values.Length / 2 - 1; root >= 0; root--) SiftCandidates(values, root);
        for (int end = values.Length - 1; end > 0; end--)
        {
            (values[0], values[end]) = (values[end], values[0]);
            SiftCandidates(values[..end], 0);
        }
    }

    private static void SiftCandidates(Span<Candidate> values, int root)
    {
        while (root * 2 + 1 < values.Length)
        {
            int child = root * 2 + 1;
            if (child + 1 < values.Length && CompareCandidates(values[child], values[child + 1]) < 0) child++;
            if (CompareCandidates(values[root], values[child]) >= 0) return;
            (values[root], values[child]) = (values[child], values[root]);
            root = child;
        }
    }

    private readonly record struct EffectKey(ulong Id, uint Generation, VfxEffectKind Kind);
    private readonly record struct Candidate(EffectKey Key, int InstanceIndex, float Distance, float Radius,
        int Count, float Area, int Priority, bool Fallback);
}
