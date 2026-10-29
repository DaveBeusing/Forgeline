using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct ChunkDecorationRenderSettings
{
    public ChunkDecorationRenderSettings() { }
    public int MaximumResidentChunks { get; init; } = 32;
    public int MaximumResidentInstances { get; init; } = 65_536;
    public int MaximumAdmissionsPerPrepare { get; init; } = 32;
    public void Validate()
    {
        if (MaximumResidentChunks is < 1 or > 256 || MaximumResidentInstances is < 1 or > 65_536 ||
            MaximumAdmissionsPerPrepare is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(MaximumResidentChunks));
    }
}

public readonly record struct ChunkDecorationDiagnostics(int SourceChunks, int SourceInstances, int RequiredChunks,
    int CandidateInstances, int ResidentChunks, int ResidentInstances, int Admissions, int Evictions,
    long ResidentInstanceBytes, double PreparationMilliseconds)
{
    public InstanceRenderDiagnostics Render { get; init; }
    public long UploadBytes { get; init; }
    public long InstanceBufferBytes { get; init; }
    public double CullingAndAdmissionMilliseconds { get; init; }
    public double UploadPackingMilliseconds { get; init; }
    public long SnapshotInstanceBytes { get; init; }
    public double SubmissionMilliseconds { get; init; }
    public ForgeLine.Presentation.RenderSubmission.SubmissionWorkMetrics Work { get; init; }
    public InstanceRenderDiagnostics Shadow { get; init; }
    public RuntimeMaterialDiagnostics SharedAssets { get; init; }
}

internal sealed class ChunkDecorationResidency
{
    private sealed record Entry(RenderInstance[] Instances, ulong Use);
    private readonly Dictionary<ChunkCoordinate, Entry> _resident = [];
    private ChunkDecorationSnapshot? _snapshot;
    private SimulationSessionId _session;
    private ulong _use;
    private readonly PresentationSnapshotBuffer _buffer = new();
    internal RenderWorld World { get; } = new();
    internal ChunkDecorationDiagnostics Diagnostics { get; private set; }
    internal bool Prepared { get; private set; }
    private Matrix4x4 _view, _shadow;
    private int _width, _height;
    private bool _hasShadow;

    internal void Prepare(ChunkDecorationSnapshot snapshot, SimulationSessionId session, RtsCamera camera,
        in RenderPreparationRequest request, ChunkDecorationRenderSettings settings)
    {
        settings.Validate();
        if (request.Width < 1 || request.Height < 1) throw new ArgumentOutOfRangeException(nameof(request));
        var watch = Stopwatch.StartNew();
        Matrix4x4 view = camera.GetMatrices(request.Width, request.Height).ViewProjection;
        ViewFrustum frustum = ViewFrustum.FromViewProjection(view);
        bool hasShadow = request.Shadow.HasValue;
        ViewFrustum shadow = request.Shadow is { } frame ? ViewFrustum.FromViewProjection(frame.ViewProjection) : default;
        var required = snapshot.Chunks.Where(c => c.Instances.Length > 0 &&
            (frustum.Intersects(c.Bounds) || hasShadow && shadow.Intersects(c.Bounds))).ToArray();
        int candidates = required.Sum(c => c.Instances.Length);
        bool changed = !ReferenceEquals(snapshot, _snapshot) || session != _session;
        int admissions = required.Count(c => changed || !_resident.ContainsKey(c.Coordinate));
        if (required.Length > settings.MaximumResidentChunks || candidates > settings.MaximumResidentInstances ||
            admissions > settings.MaximumAdmissionsPerPrepare)
            throw new InvalidOperationException("Visible decoration exceeds the chunk, instance or admission budget; increase the explicit budget before recording.");
        // Budget rejection leaves the prior generation/cache intact.
        Prepared = false;
        int evictions = changed ? _resident.Count : 0;
        if (changed) { _resident.Clear(); _snapshot = snapshot; _session = session; }
        _use++;
        var keys = required.Select(c => c.Coordinate).ToHashSet();
        int newInstances = required.Where(c => !_resident.ContainsKey(c.Coordinate)).Sum(c => c.Instances.Length);
        foreach (var pair in _resident.Where(p => !keys.Contains(p.Key)).OrderBy(p => p.Value.Use)
            .ThenBy(p => p.Key.Z).ThenBy(p => p.Key.X).ToArray())
        {
            if (_resident.Count + admissions <= settings.MaximumResidentChunks &&
                _resident.Values.Sum(e => e.Instances.Length) + newInstances <= settings.MaximumResidentInstances) break;
            _resident.Remove(pair.Key); evictions++;
        }
        foreach (var chunk in required)
        {
            if (_resident.TryGetValue(chunk.Coordinate, out var prior)) _resident[chunk.Coordinate] = prior with { Use = _use };
            else _resident.Add(chunk.Coordinate, new(Expand(chunk), _use));
        }
        if (changed || admissions > 0 || evictions > 0)
        {
            // Never publish into the gameplay snapshot or its picking/intelligence indices.
            RenderInstance[] instances = _resident.OrderBy(p => p.Key.Z).ThenBy(p => p.Key.X).SelectMany(p => p.Value.Instances).ToArray();
            _buffer.Publish(new(new SimulationTick(_use), TimeSpan.Zero, 0, instances, sessionId: session));
            World.Update(_buffer);
        }
        _view = view; _width = request.Width; _height = request.Height;
        _hasShadow = request.Shadow.HasValue; _shadow = request.Shadow?.ViewProjection ?? default;
        Prepared = true;
        int residentCount = _resident.Values.Sum(e => e.Instances.Length);
        Diagnostics = new(snapshot.ChunkCount, snapshot.InstanceCount, required.Length, candidates, _resident.Count,
            residentCount, admissions, evictions, (long)residentCount * Unsafe.SizeOf<RenderInstance>(), watch.Elapsed.TotalMilliseconds)
            { SnapshotInstanceBytes = (long)(World.InstanceCount + (World.PreviousSnapshot?.InstanceCount ?? 0)) * Unsafe.SizeOf<RenderInstance>() };
    }

    internal void AssertPrepared(RtsCamera camera, int width, int height, Matrix4x4? shadow)
    {
        if (!Prepared || width != _width || height != _height || camera.GetMatrices(width, height).ViewProjection != _view ||
            shadow.HasValue && (!_hasShadow || shadow.Value != _shadow))
            throw new InvalidOperationException("Prepare decoration for this camera, viewport and shadow before recording.");
    }

    private static RenderInstance[] Expand(ChunkDecorationChunk chunk)
    {
        var result = new RenderInstance[chunk.Instances.Length];
        for (int index = 0; index < result.Length; index++)
        {
            var item = chunk.Instances[index]; var species = chunk.Species[item.SpeciesIndex]; var transform = chunk.Transform(item);
            WorldVisualId visual = species.Classification switch
            {
                ScatterClassification.Tree => species.AssetId.Contains("broadleaf", StringComparison.Ordinal) ? WorldVisualId.VegetationBroadleaf : WorldVisualId.VegetationConifer,
                ScatterClassification.Shrub => WorldVisualId.VegetationScrub,
                ScatterClassification.Grass => WorldVisualId.VegetationGrassClump,
                _ => WorldVisualId.PropRock
            };
            WorldPresentationKind kind = species.Classification is ScatterClassification.Tree or ScatterClassification.Shrub or ScatterClassification.Grass
                ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop;
            result[index] = new(default, new(transform.PositionMeters,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, transform.YawDegrees * MathF.PI / 180), transform.SizeMeters),
                new(5000u + (uint)visual), RenderMaterialHandle.Default, RenderVisibilityMask.World, chunk.FirstOrdinal + (uint)index,
                WorldFeature: new(visual, kind, default, Inspectable: false))
                { Decoration = new(item.StableId, species.AssetId, species.PivotHeightPermille / 1000f, transform.TintMultiplier) };
        }
        return result;
    }
}
