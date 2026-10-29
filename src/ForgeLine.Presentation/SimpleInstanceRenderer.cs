using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Buffers;
using ForgeLine.Presentation.RenderSubmission;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Graphics.RenderContracts;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed partial class SimpleInstanceRenderer : IDisposable
{
    private const int MatrixRootConstantCount = 16;
    private const int RootConstantCount =
        MatrixRootConstantCount +
        SceneLightingSettings.ShaderConstantCount + 5;
    private const int FallbackVertexStride = 48;
    private const int InstanceStride = 112;
    private const int MinimumInstanceCapacity = 64;
    private const int MaximumRetainedScratchInstances = 65_536;
    private const int MaximumRetainedScratchBatches = 256;
    private const int MaximumRetainedBatchSlots = MaximumRetainedScratchBatches * 4;
    private bool _stagingPooled;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsPipeline? _shadowPipeline;
    private readonly IGraphicsPipeline? _texturedPipeline;
    private readonly IGraphicsBuffer _vertexBuffer;
    private readonly IGraphicsBuffer _indexBuffer;
    private readonly RenderAssetSession? _runtimeAssets;
    private readonly SceneLightingSettings _lighting;
    private readonly float _lodDiameterScale;
    private readonly bool _vegetationMotion;
    private readonly bool _clusterCulling;
    private readonly VegetationRenderSettings _vegetation;
    private readonly Dictionary<int, FrameInstanceBuffer> _frameInstanceBuffers = [];
    private readonly Dictionary<int, FrameInstanceBuffer> _shadowFrameInstanceBuffers = [];
    private readonly Dictionary<InstanceBatchKey, InstanceBatch> _batchLookup = [];
    private readonly List<InstanceBatch> _batches = [];
    private readonly ConditionalWeakTable<RenderWorld, WorldLodHistory> _worldLods = new();
    private CameraLodHistory? _activeLodHistory;
    private Dictionary<(EntityId Entity, uint Identity), int> _previousLods = [];
    private Dictionary<(EntityId Entity, uint Identity), int> _currentLods = [];
    private InstanceRenderData[] _instanceStaging = [];
    private readonly SpatialSubmissionIndex _spatialVisibility = new();
    private readonly SubmissionFrameCache _preparedRoots = new();
    private readonly Dictionary<string, (bool Found, RuntimeMeshBuffers Mesh, AssetId Id)> _passMeshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeMaterialResources> _passMaterials = new(StringComparer.Ordinal);
    private int _meshResolutions, _materialResolutions;
    public SubmissionWorkMetrics WorkMetrics { get; private set; }

    private int _activeBatchCount;
    private int _renderRootIndex;
    private bool _collectRootIndices;
    private long _snapshotIndexBytes;
    private bool _disposed;

    public SimpleInstanceRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null, float lodDiameterScale = 1, TextureDetailQuality textureDetail = TextureDetailQuality.Full,
        bool vegetationMotion = true, WorldEnvironmentSettings? environment = null, VegetationRenderSettings? vegetation = null, bool clusterCulling = true)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _lighting =
            lighting ??
            SceneLightingSettings.Default;
        _lighting.Validate();
        if (!float.IsFinite(lodDiameterScale) || lodDiameterScale is < .8f or > 1.25f)
            throw new ArgumentOutOfRangeException(nameof(lodDiameterScale));
        _lodDiameterScale = lodDiameterScale;
        _vegetationMotion = vegetationMotion;
        _clusterCulling = clusterCulling;
        WorldEnvironmentSettings appearance = environment ?? new();
        appearance.Validate();
        _vegetation = vegetation ?? new();
        _vegetation.Validate();

        _decorationCatalog = runtimeAssets;
        _decorationFactory = () => new SimpleInstanceRenderer(graphics, runtimeAssets, lighting, lodDiameterScale, textureDetail,
            vegetationMotion, environment, vegetation, clusterCulling) { _decorationPass = true };
        _graphics = graphics;
        try
        {
            _pipeline = InstanceMaterialPipeline.CreatePipeline(graphics, appearance);
            if (graphics.DirectionalShadows.Enabled) _shadowPipeline = DirectionalShadowCasterPipeline.Create(graphics, true);
            _runtimeAssets =
                runtimeAssets is null
                    ? null
                    : new RenderAssetSession(
                        graphics,
                        runtimeAssets, textureDetail);
            _texturedPipeline =
                _runtimeAssets is null
                    ? null
                    : InstanceMaterialPipeline.CreateTexturedPipeline(
                        graphics, appearance);

            SimpleVertex[] vertices =
            [
                new(-0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),

            new(-0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),

            new(-0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),

            new(-0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),

            new( 0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new( 0.5f,  0.5f, -0.5f,  1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new( 0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new( 0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f),

            new(-0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new(-0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new(-0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f),
            new(-0.5f,  0.5f, -0.5f, -1.0f,  0.0f,  0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f)
            ];

            ushort[] indices =
            [
                 0,  2,  1,  0,  3,  2,
             4,  5,  6,  4,  6,  7,
             8,  9, 10,  8, 10, 11,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23
            ];

            _vertexBuffer = graphics.CreateInitializedStaticBuffer(
                new(checked((ulong)vertices.Length * FallbackVertexStride), StaticGraphicsBufferUsage.Vertex),
                MemoryMarshal.AsBytes(vertices.AsSpan()));
            _indexBuffer = graphics.CreateInitializedStaticBuffer(
                new(checked((ulong)indices.Length * sizeof(ushort)), StaticGraphicsBufferUsage.Index),
                MemoryMarshal.AsBytes(indices.AsSpan()));
        }
        catch
        {
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();
            _texturedPipeline?.Dispose();
            _skinFrames?.Dispose();
        _runtimeAssets?.Dispose();
            _pipeline?.Dispose();
            _shadowPipeline?.Dispose();
            throw;
        }
    }

    public bool AssetsPrepared => _runtimeAssets?.IsPrepared == true;
    public RenderAssetFrameMetrics? AssetFrameMetrics => _runtimeAssets?.Metrics;

    public void ObserveFrameDiagnostics(GraphicsDiagnostics diagnostics)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostics);
        _runtimeAssets?.ObserveFrameDiagnostics(diagnostics);
        _decorations?.Observe(diagnostics);
    }

    public void Prepare(RtsCamera camera, RenderWorld world, in RenderPreparationRequest request, ParticleVfxRenderer? particles = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        if (_graphics.IsRecording) throw new InvalidOperationException("Prepare assets before command recording.");
        if (request.Width < 1 || request.Height < 1) throw new ArgumentOutOfRangeException(nameof(request));
        PrepareDecorations(camera, world, request);
        if (_runtimeAssets is null) return;
        _runtimeAssets.MetricsEnabled = request.MetricsEnabled;
        _runtimeAssets.BeginPrepare(world.CurrentSnapshot?.SessionId ?? default, world.CurrentSnapshot?.Tick.Value ?? 0);
        try
        {
            RenderCore(null, camera, world, request.Alpha, presentationSeconds: request.PresentationSeconds,
                particles: particles, prepareWidth: request.Width, prepareHeight: request.Height);
            if (request.Shadow is { } shadow)
            {
                shadow.Validate();
                RenderCore(null, camera, world, request.Alpha, shadow.ViewProjection, request.MaximumShadowCasters,
                    presentationSeconds: request.PresentationSeconds, prepareWidth: request.Width, prepareHeight: request.Height);
            }
            _runtimeAssets.EndPrepare();
        }
        catch { _runtimeAssets.AbortPrepare(); throw; }
    }
    public void Submit()
    {
        _decorations?.Submit();
        // A suspended/occluded device can skip the recording callback entirely.
        if (_runtimeAssets?.IsPrepared == true) { _runtimeAssets.AbortPrepare(); return; }
        if (_runtimeAssets?.FrameActive == true) _runtimeAssets.Submit();
    }
    public void Retire() { _decorations?.Retire(); if (_runtimeAssets?.FrameActive == true) _runtimeAssets.Retire(); }

    public InstanceRenderDiagnostics LastShadowDiagnostics { get; private set; }
    public InstanceRenderDiagnostics LastDiagnostics { get; private set; }

    public InstanceSubmissionMetrics SubmissionMetrics
    {
        get
        {
            long retained = 0;
            foreach (InstanceBatch batch in _batches) retained += batch.Instances.Capacity;
            int mechanical = 0;
            for (int i = 0; i < _activeBatchCount; i++)
                if (_batches[i].RuntimeMesh.MechanicalParts is { Length: > 0 }) mechanical += _batches[i].Instances.Count;
            int submitted = LastDiagnostics.RuntimeMeshInstances + LastDiagnostics.FallbackMeshInstances;
            return new InstanceSubmissionMetrics(_instanceStaging.Length, retained, _batches.Count,
                submitted, (long)submitted * InstanceStride, WorkMetrics.PipelineBindings, WorkMetrics.TextureBindings)
            { SnapshotIndexBytes = _snapshotIndexBytes, MechanicalPartInstances = mechanical, MechanicalTransformScratchBytes = MechanicalMeshValidation.MaximumParts * 64, SkinEvaluatedPoses = _skinFrames?.EvaluatedPoses ?? 0, SkinStaticFallbacks = _skinFrames?.StaticFallbacks ?? 0, SkinUploadBytes = _skinFrames?.ResidentBytes ?? 0 };
        }
    }

    public SceneLightingSettings Lighting =>
        _lighting;

    public RuntimeMaterialDiagnostics MaterialDiagnostics =>
        _runtimeAssets?.MaterialDiagnostics ??
        default;

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera,
        RenderWorld world,
        float alpha,
        double? presentationSeconds = null,
        ParticleVfxRenderer? particles = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (world.CurrentSnapshot?.ChunkDecorations is not null)
            (_decorations ?? throw new InvalidOperationException("Prepare decoration before recording.")).Validate(context, camera, world);
        _runtimeAssets?.BeginRecord();
        try { RenderCore(context, camera, world, alpha, presentationSeconds: presentationSeconds, particles: particles); }
        finally { _runtimeAssets?.EndRecord(); }
        if (world.CurrentSnapshot?.ChunkDecorations is not null)
            (_decorations ?? throw new InvalidOperationException("Prepare decoration before recording.")).Render(context, camera, world, presentationSeconds);
    }

    public void RenderShadow(IGraphicsCommandContext context, RtsCamera camera, RenderWorld world, float alpha,
        GraphicsDirectionalShadowFrame frame, int maximumCasters, int maximumDraws, double? presentationSeconds = null)
    {
        frame.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCasters);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDraws);
        InstanceRenderDiagnostics prior = LastDiagnostics;
        ResourceDepositRenderDiagnostics priorResources = ResourceDiagnostics;
        ArgumentNullException.ThrowIfNull(context);
        if (world.CurrentSnapshot?.ChunkDecorations is not null)
            (_decorations ?? throw new InvalidOperationException("Prepare decoration before recording.")).Validate(context, camera, world, frame.ViewProjection);
        _runtimeAssets?.BeginRecord();
        try { RenderCore(context, camera, world, alpha, frame.ViewProjection, maximumCasters, maximumDraws, presentationSeconds); }
        finally { _runtimeAssets?.EndRecord(); }
        LastShadowDiagnostics = LastDiagnostics;
        if (world.CurrentSnapshot?.ChunkDecorations is not null)
            (_decorations ?? throw new InvalidOperationException("Prepare decoration before recording.")).RenderShadow(context, camera, world, frame,
                Math.Max(0, maximumCasters - LastDiagnostics.VisibleInstances), Math.Max(0, maximumDraws - LastDiagnostics.DrawCalls), presentationSeconds);
        LastDiagnostics = prior;
        ResourceDiagnostics = priorResources;
    }

    private void RenderCore(IGraphicsCommandContext? context, RtsCamera camera, RenderWorld world, float alpha,
        Matrix4x4? shadowProjection = null, int maximumCasters = int.MaxValue, int maximumDraws = int.MaxValue,
        double? presentationSeconds = null, ParticleVfxRenderer? particles = null, int prepareWidth = 0, int prepareHeight = 0)
    {
        ThrowIfDisposed();
        long cullingStarted = _decorationPass ? Stopwatch.GetTimestamp() : 0;
        if (_decorationPass) { DecorationCullingMilliseconds = 0; DecorationUploadMilliseconds = 0; }
        bool preparing = context is null;
        int viewportWidth = context?.Width ?? prepareWidth;
        int viewportHeight = context?.Height ?? prepareHeight;
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        if (presentationSeconds is { } seconds && (!double.IsFinite(seconds) || seconds < 0))
            throw new ArgumentOutOfRangeException(nameof(presentationSeconds));

        if (!preparing) { _skinContextFrame = context!.FrameIndex; _skinContextShadow = shadowProjection.HasValue; }
        _snapshotIndexBytes = world.IndexRetainedBytes;
        if (!_vegetationMotion) presentationSeconds = null;

        CameraMatrices matrices =
            camera.GetMatrices(
                viewportWidth,
                viewportHeight);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(
                shadowProjection ?? matrices.ViewProjection);
        var session = world.CurrentSnapshot?.SessionId ?? default;
        WorldLodHistory worldHistory = _worldLods.GetValue(world, static _ => new WorldLodHistory());
        if (worldHistory.Session != session) { worldHistory.Cameras.Clear(); worldHistory.Session = session; }
        _activeLodHistory = worldHistory.Cameras.GetValue(camera, static _ => new CameraLodHistory());
        _previousLods = _activeLodHistory.Previous; _currentLods = _activeLodHistory.Current;
        if (!preparing)
        {
            ResetScratch(shadowProjection.HasValue);
            _skinFrames?.Begin(context!.FrameIndex, shadowProjection.HasValue);
            if (!shadowProjection.HasValue) _currentLods.Clear();
        }
        var batchLookup = _batchLookup;
        var batches = _batches;

        bool spatialCulling = _clusterCulling && world.InstanceCount <= SpatialSubmissionIndex.MaximumRetainedRoots;
        if (spatialCulling)
        {
            _spatialVisibility.Update(world);
            _spatialVisibility.Query(frustum, world.InstanceCount);
        }
        else if (world.InstanceCount > SpatialSubmissionIndex.MaximumRetainedRoots) _spatialVisibility.Clear();
        _snapshotIndexBytes = world.IndexRetainedBytes + _spatialVisibility.RetainedBytes;
        int clusterTests = spatialCulling ? _spatialVisibility.ClusterCount : 0;
        int rejectedClusters = spatialCulling ? _spatialVisibility.RejectedClusters : 0;
        int clusterCulled = spatialCulling ? _spatialVisibility.RejectedRoots : 0, instanceTests = 0;
        _preparedRoots.Begin(world, alpha, presentationSeconds);
        _passMeshes.Clear(); _passMaterials.Clear();
        _resourceBindings.Clear();
        ResourceDiagnostics = default;
        _meshResolutions = _materialResolutions = 0;
        _collectRootIndices = shadowProjection.HasValue && (maximumCasters != int.MaxValue || maximumDraws != int.MaxValue);
        int candidates = 0, batchedRoots = 0;
        int visible = 0;
        int animatedVegetation = 0;
        int visibleVegetation = 0;
        int skippedDecalResources = 0;
        int effectRequests = 0, meshFallbackEffects = 0;
        int highLod = 0;
        int reducedLod = 0;
        int lod1Instances = 0;
        int lod2Instances = 0;

        int index = -1;
        var candidateIndices = _spatialVisibility.Candidates();
        while (spatialCulling ? candidateIndices.MoveNext() : ++index < world.InstanceCount)
        {
            if (spatialCulling) index = candidateIndices.Current;
            _renderRootIndex = index;
            if (_clusterCulling && !spatialCulling && index % 32 == 0)
            {
                clusterTests++;
                if (!world.ClusterVisible(index, frustum))
                {
                    int skipped = Math.Min(32, world.InstanceCount - index);
                    rejectedClusters++; clusterCulled += skipped; index += skipped - 1; continue;
                }
            }
            candidates++;
            ref readonly PreparedRoot preparedRoot = ref _preparedRoots.Get(world, index);
            RenderInstance rootInstance = preparedRoot.Instance;
            ResourceDepositField? resourceField = _runtimeAssets is null ? null : rootInstance.ResourceField;
            bool rootVisible = false, rootBatched = false;
            for (int fieldPart = 0; fieldPart < (resourceField?.Clusters.Count ?? 1); fieldPart++)
            {
                ResourceDepositFieldCluster? fieldCluster = resourceField?.Clusters[fieldPart];
                RenderInstance instance = fieldCluster is { } mineral ? rootInstance with
                {
                    Transform = mineral.Transform,
                    ResourceField = null,
                    WorldFeature = FieldResourceFeature(rootInstance.WorldFeature, mineral),
                    DebugIdentity = 0xD000_0000U + (uint)mineral.Ordinal
                } : rootInstance;
                if (shadowProjection.HasValue && (instance.VfxFeature.IsSpecified || instance.DecalFeature.IsSpecified)) continue;


                if ((instance.Visibility & RenderVisibilityMask.World) == 0 ||
                    !_vegetation.Includes(instance, camera.Position) ||
                    !instance.Mesh.IsValid ||
                    !instance.Material.IsValid)
                {
                    continue;
                }

                if (instance.VfxFeature.IsSpecified && ++effectRequests > PresentationExtractor.MaximumSnapshotEffects) continue;
                if (instance.WorldFeature.Kind == ForgeLine.Game.WorldPresentationKind.ResourceDeposit &&
                    !ValidResourceTransform(instance.Transform))
                {
                    ResourceDiagnostics = ResourceDiagnostics with { InvalidTransforms = ResourceDiagnostics.InvalidTransforms + 1 };
                    continue;
                }
                if (!shadowProjection.HasValue && particles?.Replaces(instance) == true) continue;
                if (instance.VfxFeature.IsSpecified && ++meshFallbackEffects > (particles?.MaximumMeshFallbackEffects ?? 128)) continue;
                AxisAlignedBounds bounds = fieldCluster.HasValue
                    ? ResourceDepositFieldComposition.Bounds(instance.Transform) : preparedRoot.Bounds;

                instanceTests++;
                if (Finite(bounds.Minimum) && Finite(bounds.Maximum) && !frustum.Intersects(bounds))
                {
                    continue;
                }

                bool firstVisiblePart = !rootVisible;
                if (firstVisiblePart) { visible++; rootVisible = true; }
                if (instance.WorldFeature.Kind == ForgeLine.Game.WorldPresentationKind.Vegetation) visibleVegetation++;

                RuntimeMeshBuffers runtimeMesh =
                    default;
                AssetId runtimeMeshId =
                    default;
                bool usesRuntimeMesh =
                    false;
                string? materialAssetId =
                    null;
                float distance =
                    Vector3.Distance(
                        camera.Position,
                        instance.Transform.Position);
                float projectedDiameter = ScreenSpaceLod.ProjectedDiameter(
                    instance.Transform.Position,
                    PresentationBounds.ResolveLocalHalfExtents(instance).Length(),
                    matrices, viewportHeight);
                var lodKey = (instance.Entity, instance.DebugIdentity);
                bool hasLodIdentity = instance.Entity.IsValid || instance.DebugIdentity != 0;
                var lodHistory = preparing || shadowProjection.HasValue ? _currentLods : _previousLods;
                int previousLod = hasLodIdentity && lodHistory.TryGetValue(lodKey, out int retainedLod) ? retainedLod : 0;
                UnitAssetLod unitLod =
                    UnitAssetLod.Lod0;
                UnitPresentationDefinition unitDefinition =
                    default;
                bool hasUnitDefinition =
                    false;
                BuildingAssetLod buildingLod =
                    BuildingAssetLod.Lod0;
                bool hasBuildingDefinition =
                    false;

                if (instance.UnitFeature.IsSpecified)
                {
                    unitLod =
                        (UnitAssetLod)SkinPresentationBounds.SelectLod(instance, projectedDiameter, previousLod, _lodDiameterScale);
                    if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = (int)unitLod;
                    unitDefinition =
                        UnitPresentationCatalog.Get(
                            instance.UnitFeature.Unit);
                    materialAssetId =
                        unitDefinition.MaterialAssetId;
                    hasUnitDefinition =
                        true;
                    string meshAssetId =
                        instance.UnitFeature.IsWreck && !(unitLod == UnitAssetLod.Lod0 && instance.MechanicalPose.Infantry is { IsSpecified: true, Action: InfantryAction.Death })
                            ? UnitPresentationCatalog.ResolveWreckAssetId(instance.UnitFeature)
                            : unitDefinition.GetMeshAssetId(
                                unitLod);

                    usesRuntimeMesh =
                        _runtimeAssets is not null &&
                        TryGetMesh(
                            meshAssetId,
                            out runtimeMesh,
                            out runtimeMeshId) &&
                        runtimeMesh.IsValid;

                    if (!usesRuntimeMesh && _runtimeAssets is not null && unitLod != UnitAssetLod.Lod0 && !instance.UnitFeature.IsWreck)
                    {
                        unitLod = UnitAssetLod.Lod0;
                        usesRuntimeMesh = TryGetMesh(unitDefinition.MeshAssetId,
                            out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                        if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = (int)unitLod;
                    }
                    if (unitLod == UnitAssetLod.Lod1) lod1Instances++;
                    if (unitLod == UnitAssetLod.Lod2) lod2Instances++;

                    if (unitLod == UnitAssetLod.Lod0)
                    {
                        highLod++;
                    }
                    else
                    {
                        reducedLod++;
                    }
                }
                else if (instance.BuildingFeature.IsSpecified)
                {
                    buildingLod =
                        instance.BuildingFeature.IsConstruction || instance.BuildingFeature.IsDestroyed
                            ? BuildingAssetLod.Lod0
                            : (BuildingAssetLod)ScreenSpaceLod.Select(projectedDiameter, previousLod, 150.0f, 45.0f, _lodDiameterScale);
                    if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = (int)buildingLod;
                    hasBuildingDefinition =
                        BuildingPresentationCatalog.TryGet(
                            instance.BuildingFeature.Building,
                            out _);
                    materialAssetId =
                        BuildingPresentationCatalog.ResolveMaterialAssetId(
                            instance.BuildingFeature);

                    string meshAssetId =
                        BuildingPresentationCatalog.ResolveMeshAssetId(
                            instance.BuildingFeature,
                            buildingLod);

                    usesRuntimeMesh =
                        _runtimeAssets is not null &&
                        TryGetMesh(
                            meshAssetId,
                            out runtimeMesh,
                            out runtimeMeshId) &&
                        runtimeMesh.IsValid;

                    if (!usesRuntimeMesh && _runtimeAssets is not null && buildingLod != BuildingAssetLod.Lod0)
                    {
                        buildingLod = BuildingAssetLod.Lod0;
                        usesRuntimeMesh = TryGetMesh(
                            BuildingPresentationCatalog.ResolveMeshAssetId(instance.BuildingFeature, buildingLod),
                            out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                        if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = (int)buildingLod;
                    }

                    if (buildingLod == BuildingAssetLod.Lod1) lod1Instances++;
                    if (buildingLod == BuildingAssetLod.Lod2) lod2Instances++;

                    if (buildingLod ==
                        BuildingAssetLod.Lod0)
                    {
                        highLod++;
                    }
                    else
                    {
                        reducedLod++;
                    }
                }
                else if (instance.VfxFeature.IsSpecified)
                {
                    VfxPresentationDefinition definition =
                        VfxPresentationCatalog.Get(
                            instance.VfxFeature.Kind);
                    materialAssetId =
                        definition.MaterialAssetId;

                    if (!VfxPresentationCatalog.ShouldRender(
                            instance.VfxFeature.Kind,
                            distance))
                    {
                        visible--;
                        continue;
                    }

                    usesRuntimeMesh =
                        _runtimeAssets is not null &&
                        TryGetMesh(
                            definition.MeshAssetId,
                            out runtimeMesh,
                            out runtimeMeshId) &&
                        runtimeMesh.IsValid;

                    highLod++;
                }
                else if (instance.ConveyorFeature.IsSpecified)
                {
                    materialAssetId="material.infrastructure.directorate.conveyor";
                    float conveyorDistance=Vector3.Distance(camera.Position,instance.Transform.Position);
                    int conveyorLod=conveyorDistance>420?2:conveyorDistance>140?1:0;
                    usesRuntimeMesh=_runtimeAssets is not null &&
                        TryGetMesh(instance.ConveyorFeature.Mesh(conveyorLod),out runtimeMesh,out runtimeMeshId) && runtimeMesh.IsValid;
                    highLod++;
                }
                else if (instance.InfrastructureFeature.IsSpecified)
                {
                    materialAssetId =
                        InfrastructurePresentationCatalog.ResolveMaterialAssetId(
                            instance.InfrastructureFeature);
                    string meshAssetId =
                        InfrastructurePresentationCatalog.ResolveMeshAssetId(
                            instance.InfrastructureFeature);

                    usesRuntimeMesh =
                        _runtimeAssets is not null &&
                        TryGetMesh(
                            meshAssetId,
                            out runtimeMesh,
                            out runtimeMeshId) &&
                        runtimeMesh.IsValid;

                    highLod++;
                }
                else if (instance.WorldFeature.Kind == ForgeLine.Game.WorldPresentationKind.ResourceDeposit &&
                         ResourceDepositVariantCatalog.IsResource(instance.WorldFeature.Visual))
                {
                    WorldAssetLod requestedLod = ScreenSpaceLod.Select(projectedDiameter, previousLod, 65.0f, 18.0f, _lodDiameterScale) > 0
                        ? WorldAssetLod.Reduced : WorldAssetLod.High;
                    if (!TryResolveResource(instance.WorldFeature, requestedLod, out runtimeMesh, out runtimeMeshId,
                        out materialAssetId, out WorldAssetLod actualLod)) continue;
                    usesRuntimeMesh = true;
                    if (hasLodIdentity && !shadowProjection.HasValue && !preparing)
                        _currentLods[lodKey] = requestedLod == WorldAssetLod.Reduced ? 1 : 0;
                    if (firstVisiblePart)
                    {
                        if (actualLod == WorldAssetLod.Reduced) { lod1Instances++; reducedLod++; }
                        else highLod++;
                    }
                }
                else if (instance.Decoration.IsSpecified)
                {
                    WorldAssetLod lod = ScreenSpaceLod.Select(projectedDiameter, previousLod, 65.0f, 18.0f, _lodDiameterScale) > 0
                        ? WorldAssetLod.Reduced : WorldAssetLod.High;
                    string mesh = DecorationMesh(instance.Decoration, lod == WorldAssetLod.Reduced);
                    usesRuntimeMesh = _runtimeAssets is not null && TryGetMesh(mesh, out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                    if (_runtimeAssets is not null && !usesRuntimeMesh)
                    {
                        mesh = instance.Decoration.MeshAssetId!;
                        usesRuntimeMesh = TryGetMesh(mesh, out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                    }
                    if (_runtimeAssets is not null && !usesRuntimeMesh) continue;
                    _ = DecorationNormalization(instance.Decoration, runtimeMeshId);
                    if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = lod == WorldAssetLod.Reduced ? 1 : 0;
                    if (lod == WorldAssetLod.Reduced) { lod1Instances++; reducedLod++; } else highLod++;
                    materialAssetId = usesRuntimeMesh && runtimeMesh.HasMaterial ? runtimeMesh.MaterialId.Value :
                        WorldPresentationCatalog.Get(instance.WorldFeature.Visual).MaterialAssetId;
                }
                else if (instance.WorldFeature.IsSpecified)
                {
                    materialAssetId =
                        WorldPresentationCatalog.Get(
                            instance.WorldFeature.Visual)
                        .MaterialAssetId;
                    WorldAssetLod worldLod =
                        ScreenSpaceLod.Select(projectedDiameter, previousLod, 65.0f, 18.0f, _lodDiameterScale) > 0
                            ? WorldAssetLod.Reduced : WorldAssetLod.High;
                    if (hasLodIdentity && !shadowProjection.HasValue && !preparing) _currentLods[lodKey] = worldLod == WorldAssetLod.Reduced ? 1 : 0;

                    usesRuntimeMesh =
                        _runtimeAssets is not null &&
                        TryGetMesh(instance.WorldFeature, worldLod, out runtimeMesh, out runtimeMeshId) &&
                        runtimeMesh.IsValid;

                    if (worldLod == WorldAssetLod.Reduced && firstVisiblePart)
                    {
                        lod1Instances++;
                        reducedLod++;
                    }
                    else if (firstVisiblePart)
                    {
                        highLod++;
                    }
                }
                else
                {
                    highLod++;
                }

                if (usesRuntimeMesh &&
                    runtimeMesh.HasMaterial &&
                    !hasBuildingDefinition &&
                    instance.WorldFeature.Kind != ForgeLine.Game.WorldPresentationKind.ResourceDeposit)
                {
                    materialAssetId =
                        runtimeMesh.MaterialId.Value;
                }

                bool hasRuntimeMaterial =
                    _runtimeAssets is not null &&
                    materialAssetId is not null;
                RuntimeMaterialResources runtimeMaterial =
                    hasRuntimeMaterial
                        ? ResolveMaterial(
                            materialAssetId!)
                        : default;
                bool usesRuntimeMaterial =
                    hasRuntimeMaterial &&
                    (!usesRuntimeMesh ||
                     runtimeMesh.SupportsTexturedMaterial);
                if (instance.DecalFeature.IsSpecified && (!usesRuntimeMesh || !usesRuntimeMaterial || runtimeMaterial.UsesDevelopmentFallback || !float.IsFinite(instance.DecalFeature.Opacity)))
                { skippedDecalResources++; continue; }
                if (preparing)
                {
                    // Resource demand needs no colors, animated transforms or mechanical
                    // weights. Resolve attachments in the same order, then leave that
                    // presentation work to the recording pass that consumes it.
                    if (_runtimeAssets is not null)
                    {
                        if (hasUnitDefinition && unitLod == UnitAssetLod.Lod0 &&
                            !instance.UnitFeature.IsWreck && unitDefinition.HasArticulatedTurret)
                            TryGetMesh(unitDefinition.TurretMeshAssetId!, out _);
                        string? preparedUnitState = hasUnitDefinition
                            ? UnitPresentationCatalog.ResolveStateAttachmentAssetId(instance.UnitFeature, unitLod) : null;
                        if (preparedUnitState is not null) TryGetMesh(preparedUnitState, out _);
                        string? preparedBuildingState = hasBuildingDefinition
                            ? BuildingPresentationCatalog.ResolveStateAttachmentAssetId(instance.BuildingFeature) : null;
                        if (preparedBuildingState is not null) TryGetMesh(preparedBuildingState, out _);
                    }
                    continue;
                }
                Vector4 color =
                    ResolveColor(
                        instance,
                        hasRuntimeMaterial
                            ? runtimeMaterial.BaseColorFactor
                            : null);
                if (instance.DecalFeature.IsSpecified) color.W *= Math.Clamp(instance.DecalFeature.Opacity, 0, .65f);
                Matrix4x4 worldMatrix =
                    presentationSeconds.HasValue ? VegetationVisualMotion.Transform(instance, projectedDiameter, presentationSeconds.Value) :
                        fieldCluster.HasValue ? instance.Transform.ToMatrix() : preparedRoot.Transform;
                if (presentationSeconds.HasValue && projectedDiameter > 18 && VegetationVisualMotion.Applies(instance)) animatedVegetation++;

                if (instance.Decoration.IsSpecified)
                {
                    worldMatrix = DecorationNormalization(instance.Decoration, runtimeMeshId) * worldMatrix;
                    color *= new Vector4(instance.Decoration.Tint, instance.Decoration.Tint, instance.Decoration.Tint, 1);
                }

                if (instance.InfrastructureFeature.IsSpecified)
                {
                    worldMatrix =
                        InfrastructurePresentationCatalog.AdjustWorldTransform(
                            instance.InfrastructureFeature,
                            worldMatrix);
                }

                _skinDetail = MechanicalDetailWeight(projectedDiameter, 90, _lodDiameterScale);
                float motionWeight = MechanicalDetailWeight(projectedDiameter, hasBuildingDefinition ? 150 : 90, _lodDiameterScale);
                if (!preparing) AddMechanicalBatches(instance, worldMatrix, color, usesRuntimeMesh, runtimeMesh,
                    runtimeMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
                if (hasUnitDefinition &&
                    unitLod == UnitAssetLod.Lod0 &&
                    !instance.UnitFeature.IsWreck &&
                    unitDefinition.HasArticulatedTurret &&
                    _runtimeAssets is not null &&
                    TryGetMesh(
                        unitDefinition.TurretMeshAssetId!,
                        out RuntimeMeshBuffers turretMesh,
                        out AssetId turretMeshId) &&
                    turretMesh.IsValid)
                {
                    if (!preparing) AddMechanicalBatches(instance,
                        CreateArticulatedTransform(worldMatrix, unitDefinition.TurretPivot, instance.UnitFeature.AimYawRadians * motionWeight),
                        color, true, turretMesh, turretMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
                }
                string? unitStateAssetId = hasUnitDefinition
                    ? UnitPresentationCatalog.ResolveStateAttachmentAssetId(instance.UnitFeature, unitLod) : null;
                if (unitStateAssetId is not null && _runtimeAssets is not null &&
                    TryGetMesh(unitStateAssetId, out RuntimeMeshBuffers unitStateMesh, out AssetId unitStateMeshId) &&
                    unitStateMesh.IsValid)
                {
                    if (!preparing) AddMechanicalBatches(instance, worldMatrix, color, true, unitStateMesh,
                        unitStateMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
                }
                string? buildingStateAssetId =
                    hasBuildingDefinition
                        ? BuildingPresentationCatalog.ResolveStateAttachmentAssetId(
                            instance.BuildingFeature)
                        : null;

                if (buildingStateAssetId is not null &&
                    _runtimeAssets is not null &&
                    TryGetMesh(
                        buildingStateAssetId,
                        out RuntimeMeshBuffers stateMesh,
                        out AssetId stateMeshId) &&
                    stateMesh.IsValid)
                {
                    if (preparing) continue;
                    var stateKey =
                        new InstanceBatchKey(
                            true,
                            stateMeshId,
                            usesRuntimeMaterial,
                            runtimeMaterial.MaterialId);

                    if (!batchLookup.TryGetValue(
                            stateKey,
                            out InstanceBatch? stateBatch))
                    {
                        stateBatch =
                            RentBatch(
                                true,
                                stateMesh,
                                usesRuntimeMaterial,
                                runtimeMaterial);
                        batchLookup.Add(
                            stateKey,
                            stateBatch);
                    }

                    if (_collectRootIndices) stateBatch.RootIndices.Add(_renderRootIndex);
                    stateBatch.Instances.Add(
                        CreateInstanceRenderData(
                            worldMatrix,
                            color,
                            usesRuntimeMaterial,
                            runtimeMaterial));
                }
                if (!preparing && !rootBatched) { batchedRoots++; rootBatched = true; }
            }
        }

        WorkMetrics = new(world.InstanceCount, candidates, _preparedRoots.Interpolations, _preparedRoots.Hits,
            instanceTests, _meshResolutions, _materialResolutions, visible, 0, 0, 0, 0)
        { PreparedRootBytes = _preparedRoots.RetainedBytes, SpatialIndexBytes = _spatialVisibility.RetainedBytes };
        if (_decorationPass) DecorationCullingMilliseconds = Stopwatch.GetElapsedTime(cullingStarted).TotalMilliseconds;
        if (context is null) return;

        int preparedParts = 0;
        for (int batchIndex = 0; batchIndex < _activeBatchCount; batchIndex++) preparedParts = checked(preparedParts + batches[batchIndex].Instances.Count);
        if (shadowProjection.HasValue && (preparedParts > maximumCasters || _activeBatchCount > maximumDraws) && world.CurrentSnapshot is { } snapshot)
        {
            using var budget = new ShadowRootBudget(snapshot);
            for (int batchIndex = 0; batchIndex < _activeBatchCount; batchIndex++)
            {
                InstanceBatch batch = batches[batchIndex];
                for (int i = 0; i < batch.RootIndices.Count; i++) budget.IncludePart(batch.RootIndices[i], batchIndex < maximumDraws);
            }
            int drawnRoots = budget.Select(maximumCasters);
            for (int batchIndex = 0; drawnRoots != batchedRoots && batchIndex < _activeBatchCount; batchIndex++)
            {
                InstanceBatch batch = batches[batchIndex]; int write = 0;
                for (int i = 0; i < batch.Instances.Count; i++)
                    if (budget.Selected(batch.RootIndices[i]))
                    { batch.Instances[write] = batch.Instances[i]; batch.RootIndices[write++] = batch.RootIndices[i]; }
                batch.Instances.Truncate(write); batch.RootIndices.Truncate(write);
            }
            WorkMetrics = WorkMetrics with { DrawnRoots = drawnRoots };
        }
        else WorkMetrics = WorkMetrics with { DrawnRoots = batchedRoots };

        if (visible == 0 || _activeBatchCount == 0)
        {
            LastDiagnostics =
                new InstanceRenderDiagnostics(
                    world.InstanceCount,
                    visible,
                    world.InstanceCount - visible,
                    0,
                    highLod,
                    reducedLod) { Lod1Instances = lod1Instances, Lod2Instances = lod2Instances, ResourceDeposits = ResourceDiagnostics, SkippedDecalResources = skippedDecalResources, ClusterTests = clusterTests, RejectedClusters = rejectedClusters, ClusterCulledInstances = clusterCulled, InstanceBoundsTests = instanceTests };
            return;
        }

        int submittedInstances =
            0;
        int runtimeMeshInstances =
            0;
        int texturedRuntimeMeshInstances =
            0;
        int fallbackMeshInstances =
            0;

        for (int batchIndex = 0;
             batchIndex < _activeBatchCount;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];
            int batchInstanceCount =
                batch.Instances.Count;

            submittedInstances =
                checked(
                    submittedInstances +
                    batchInstanceCount);

            if (batch.UsesRuntimeMesh)
            {
                runtimeMeshInstances =
                    checked(
                        runtimeMeshInstances +
                        batchInstanceCount);

                if (batch.UsesRuntimeMaterial)
                {
                    texturedRuntimeMeshInstances =
                        checked(
                            texturedRuntimeMeshInstances +
                            batchInstanceCount);
                }
            }
            else
            {
                fallbackMeshInstances =
                    checked(
                        fallbackMeshInstances +
                        batchInstanceCount);
            }
        }

        if (submittedInstances == 0)
        {
            LastDiagnostics = new(world.InstanceCount, visible, world.InstanceCount - visible, 0) { ResourceDeposits = ResourceDiagnostics };
            return;
        }
        long uploadStarted = _decorationPass ? Stopwatch.GetTimestamp() : 0;
        IGraphicsBuffer instanceBuffer =
            GetFrameInstanceBuffer(
                context.FrameIndex,
                submittedInstances, shadowProjection.HasValue);
        if (_instanceStaging.Length < submittedInstances)
        {
            int capacity = Math.Max(MinimumInstanceCapacity, _instanceStaging.Length);
            while (capacity < submittedInstances) capacity = checked(capacity * 2);
            if (_stagingPooled) ArrayPool<InstanceRenderData>.Shared.Return(_instanceStaging);
            _stagingPooled = capacity > MaximumRetainedScratchInstances;
            _instanceStaging = _stagingPooled ? ArrayPool<InstanceRenderData>.Shared.Rent(capacity) : new InstanceRenderData[capacity];
        }
        var instanceData = _instanceStaging;
        int writeOffset = 0;

        for (int batchIndex = 0;
             batchIndex < _activeBatchCount;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];
            batch.InstanceOffset =
                writeOffset;
            batch.Instances.CopyTo(
                instanceData,
                writeOffset);
            writeOffset +=
                batch.Instances.Count;
        }

        instanceBuffer.SetData<InstanceRenderData>(
            instanceData.AsSpan(0, submittedInstances));
        if (_decorationPass) DecorationUploadMilliseconds = Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;

        Span<float> constants =
            stackalloc float[RootConstantCount];
        WriteMatrix(
            shadowProjection ?? matrices.ViewProjection,
            constants);
        _lighting.WriteShaderConstants(
            constants[
                MatrixRootConstantCount..]);

        constants[30] = 0; constants[31] = 0;
        constants[32] = camera.Position.X; constants[33] = camera.Position.Y; constants[34] = camera.Position.Z;

        int draws = 0;
        int shadowSubmitted = 0, pipelineBindings = 0, textureBindings = 0;
        IGraphicsPipeline? boundPipeline = null;
        AssetId boundMaterial = default;
        bool materialBound = false;

        for (int batchIndex = 0;
             batchIndex < _activeBatchCount;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];
            if (batch.Instances.Count == 0) continue;

            IGraphicsPipeline pipeline =
                shadowProjection.HasValue ? _shadowPipeline ?? throw new NotSupportedException("Object shadows were not configured at construction.") : batch.UsesRuntimeMaterial
                    ? _texturedPipeline ??
                      throw new InvalidOperationException(
                          "The textured instance pipeline is unavailable.")
                    : _pipeline;
            if (!ReferenceEquals(boundPipeline, pipeline))
            {
                context.SetPipeline(pipeline);
                context.SetVertexConstants(shadowProjection.HasValue ? constants[..16] : constants);
                boundPipeline = pipeline; pipelineBindings++; materialBound = false;
            }

            if (batch.UsesRuntimeMaterial && !shadowProjection.HasValue &&
                (!materialBound || boundMaterial != batch.RuntimeMaterial.MaterialId))
            {
                boundMaterial = batch.RuntimeMaterial.MaterialId; materialBound = true; textureBindings += 4;
                context.SetPixelTexture(
                    0,
                    batch.RuntimeMaterial.BaseColorTexture);
                context.SetPixelTexture(
                    1,
                    batch.RuntimeMaterial.NormalTexture);
                context.SetPixelTexture(
                    2,
                    batch.RuntimeMaterial.OrmTexture);
                context.SetPixelTexture(
                    3,
                    batch.RuntimeMaterial.EmissiveTexture);
            }

            if (batch.UsesRuntimeMesh)
            {
                context.SetVertexBuffer(
                    batch.RuntimeMesh.VertexBuffer,
                    batch.RuntimeMesh.VertexStride);
                context.SetIndexBuffer(
                    batch.RuntimeMesh.IndexBuffer,
                    GraphicsIndexFormat.ThirtyTwoBit);
            }
            else
            {
                context.SetVertexBuffer(
                    _vertexBuffer,
                    FallbackVertexStride);
                context.SetIndexBuffer(
                    _indexBuffer,
                    GraphicsIndexFormat.SixteenBit);
            }

            context.SetVertexBuffer(
                instanceBuffer,
                InstanceStride,
                checked(
                    batch.InstanceOffset *
                    InstanceStride),
                inputSlot: 1);
            int drawInstances = shadowProjection.HasValue ? Math.Min(batch.Instances.Count, maximumCasters - shadowSubmitted) : batch.Instances.Count;
            if (drawInstances == 0 || draws >= maximumDraws) break;
            context.DrawIndexedInstanced(
                batch.UsesRuntimeMesh
                    ? batch.RuntimeMesh.IndexCount
                    : 36,
                drawInstances,
                startIndex: batch.UsesRuntimeMesh ? batch.RuntimeMesh.FirstIndex : 0);
            shadowSubmitted += drawInstances;
            draws++;
        }

        WorkMetrics = WorkMetrics with { UploadedParts = submittedInstances, DrawnParts = shadowSubmitted,
            PipelineBindings = pipelineBindings, TextureBindings = textureBindings };
        LastDiagnostics =
            new InstanceRenderDiagnostics(
                world.InstanceCount,
                visible,
                world.InstanceCount - visible,
                draws,
                highLod,
                reducedLod,
                runtimeMeshInstances,
                texturedRuntimeMeshInstances,
                fallbackMeshInstances) { Lod1Instances = lod1Instances, Lod2Instances = lod2Instances,
                    AnimatedVegetationInstances = animatedVegetation, VisibleVegetationInstances = visibleVegetation, ResourceDeposits = ResourceDiagnostics, SkippedDecalResources = skippedDecalResources, ClusterTests = clusterTests, RejectedClusters = rejectedClusters, ClusterCulledInstances = clusterCulled, InstanceBoundsTests = instanceTests };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        string? batchReport = Environment.GetEnvironmentVariable("FORGELINE_INSTANCE_BATCH_REPORT");
        if (!string.IsNullOrWhiteSpace(batchReport))
        {
            try
            {
                var batches = _batchLookup.Select(pair => new { Mesh = pair.Key.RuntimeMeshId.ToString(),
                    Material = pair.Key.RuntimeMaterialId.ToString(), Part = pair.Key.PartIndex,
                    Instances = pair.Value.Instances.Count, Indices = pair.Value.UsesRuntimeMesh ? pair.Value.RuntimeMesh.IndexCount : 36 }).ToArray();
                File.WriteAllText(batchReport, System.Text.Json.JsonSerializer.Serialize(batches));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine($"[graphics:batch-report-unavailable] {error.GetType().Name}"); }
        }

        foreach (FrameInstanceBuffer frameBuffer in
                 _frameInstanceBuffers.Values)
        {
            frameBuffer.Buffer?.Dispose();
        }

        _frameInstanceBuffers.Clear();
        foreach (FrameInstanceBuffer frameBuffer in _shadowFrameInstanceBuffers.Values) frameBuffer.Buffer?.Dispose();
        _shadowFrameInstanceBuffers.Clear();
        _batchLookup.Clear();
        foreach (InstanceBatch batch in _batches) { batch.Instances.Release(); batch.RootIndices.Release(); }
        _batches.Clear();
        if (_stagingPooled) ArrayPool<InstanceRenderData>.Shared.Return(_instanceStaging);
        _stagingPooled = false;
        _instanceStaging = [];
        _spatialVisibility.Clear();
        _preparedRoots.Clear(); _passMeshes.Clear(); _passMaterials.Clear();
        _previousLods.Clear();
        _currentLods.Clear(); _worldLods.Clear(); _activeLodHistory = null;
        _decorations?.Dispose(); _decorations = null;
        _skinFrames?.Dispose(); _skinFrames = null;
        _runtimeAssets?.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _texturedPipeline?.Dispose();
        _pipeline.Dispose();
        _shadowPipeline?.Dispose();
        _disposed = true;
    }

    private void ResetScratch(bool shadow = false)
    {
        if (!shadow)
        {
            (_previousLods, _currentLods) = (_currentLods, _previousLods);
            if (_activeLodHistory is { } history) { history.Previous = _previousLods; history.Current = _currentLods; }
        }
        // Only the render owner uses scratch; no span escapes synchronous SetData.
        _batchLookup.Clear();
        long retainedInstances = 0;
        foreach (InstanceBatch batch in _batches)
        {
            retainedInstances += batch.Instances.Capacity;
            batch.Reset();
        }
        if (retainedInstances > MaximumRetainedScratchInstances)
            foreach (InstanceBatch batch in _batches) { batch.Instances.Release(); batch.RootIndices.Release(); }
        // Empty metadata slots are cheap; retain a bounded escape band around 256
        // without retaining their payload leases or native resource references.
        if (_batches.Count > MaximumRetainedBatchSlots)
        {
            _batches.Clear(); _batches.TrimExcess(); _batchLookup.TrimExcess();
        }
        if (_stagingPooled)
        {
            ArrayPool<InstanceRenderData>.Shared.Return(_instanceStaging);
            _instanceStaging = []; _stagingPooled = false;
        }
        _activeBatchCount = 0;
    }

    private InstanceBatch RentBatch(
        bool usesRuntimeMesh,
        RuntimeMeshBuffers runtimeMesh,
        bool usesRuntimeMaterial,
        RuntimeMaterialResources runtimeMaterial)
    {
        if (_activeBatchCount == _batches.Count)
            _batches.Add(new InstanceBatch());
        InstanceBatch batch = _batches[_activeBatchCount++];
        batch.UsesRuntimeMesh = usesRuntimeMesh;
        batch.RuntimeMesh = runtimeMesh;
        batch.UsesRuntimeMaterial = usesRuntimeMaterial;
        batch.RuntimeMaterial = runtimeMaterial;
        return batch;
    }

    private IGraphicsBuffer GetFrameInstanceBuffer(
        int frameIndex,
        int requiredInstances, bool shadow = false)
    {
        Dictionary<int, FrameInstanceBuffer> frameBuffers = shadow ? _shadowFrameInstanceBuffers : _frameInstanceBuffers;
        ArgumentOutOfRangeException.ThrowIfNegative(
            frameIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            requiredInstances);

        if (!frameBuffers.TryGetValue(
                frameIndex,
                out FrameInstanceBuffer? frameBuffer))
        {
            frameBuffer =
                new FrameInstanceBuffer();
            frameBuffers.Add(
                frameIndex,
                frameBuffer);
        }

        if (frameBuffer.Buffer is not null &&
            frameBuffer.Capacity >= requiredInstances)
        {
            return frameBuffer.Buffer;
        }

        int capacity =
            Math.Max(
                frameBuffer.Capacity,
                MinimumInstanceCapacity);

        while (capacity < requiredInstances)
        {
            capacity =
                checked(
                    capacity * 2);
        }

        IGraphicsBuffer replacement =
            _graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked(
                        (ulong)capacity *
                        InstanceStride),
                    GraphicsBufferMemory.Upload));

        frameBuffer.Buffer?.Dispose();
        frameBuffer.Buffer =
            replacement;
        frameBuffer.Capacity =
            capacity;

        return replacement;
    }

    internal static float MechanicalDetailWeight(float diameter, float threshold, float scale)
    {
        float t = (float)Math.Clamp(((double)diameter * scale - threshold * (1 + ScreenSpaceLod.Hysteresis)) / (threshold * .5), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private SkinFrameCache? _skinFrames;
    private int _skinContextFrame;
    private bool _skinContextShadow;
    private float _skinDetail;

    private void AddMechanicalBatches(in RenderInstance instance, Matrix4x4 world, Vector4 color,
        bool usesMesh, RuntimeMeshBuffers mesh, AssetId meshId, bool usesMaterial, RuntimeMaterialResources material, float motionWeight)
    {
        int skinVariant = -1;
        if (usesMesh && mesh.Skin is not null && instance.MechanicalPose.Infantry.IsSpecified)
        {
            if (_skinFrames is null) { _skinFrames = new SkinFrameCache(_graphics); _skinFrames.Begin(_skinContextFrame, _skinContextShadow); }
            // Skin LOD uses the ordinary projected object diameter; distant meshes have no skin reference.
            float skinDetail = _skinDetail;
            mesh = _skinFrames.Resolve(meshId, mesh, instance.MechanicalPose.Infantry, skinDetail, out skinVariant);
        }
        ReadOnlySpan<MechanicalPartBinding> parts = instance.MechanicalPose.IsSpecified && motionWeight > 0
            ? mesh.MechanicalParts : null;
        Span<Matrix4x4> transforms = stackalloc Matrix4x4[MechanicalMeshValidation.MaximumParts];
        if (usesMesh && parts.Length > 0)
            MechanicalTransforms.Compose(parts, instance, world, transforms, motionWeight, mesh.Clip);
        int count = usesMesh && parts.Length > 0 ? parts.Length : 1;
        for (int i = 0; i < count; i++)
        {
            bool articulated = usesMesh && parts.Length > 0;
            int geometryPart = articulated ? parts[i].GeometryPartIndex >= 0 ? parts[i].GeometryPartIndex : i : skinVariant >= 0 ? -2 - skinVariant : -1;
            RuntimeMeshBuffers slice = articulated ? mesh with
            {
                FirstIndex = parts[geometryPart].Part.FirstIndex,
                IndexCount = parts[geometryPart].Part.IndexCount
            } : mesh;
            var key = new InstanceBatchKey(usesMesh, meshId, usesMaterial,
                usesMaterial ? material.MaterialId : default, geometryPart);
            if (!_batchLookup.TryGetValue(key, out InstanceBatch? batch))
            {
                batch = RentBatch(usesMesh, slice, usesMaterial, material);
                _batchLookup.Add(key, batch);
            }
            if (_collectRootIndices) batch.RootIndices.Add(_renderRootIndex);
            batch.Instances.Add(CreateInstanceRenderData(articulated ? Matrix4x4.CreateTranslation(parts[i].GeometryOffset) * transforms[i] : world,
                color, usesMaterial, material));
        }
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private bool TryGetMesh(string id, out RuntimeMeshBuffers mesh, out AssetId resolved)
    {
        if (!_passMeshes.TryGetValue(id, out var entry))
        {
            _meshResolutions++;
            bool found = _runtimeAssets!.TryGetMesh(id, out mesh, out resolved);
            entry = (found, mesh, resolved); _passMeshes.Add(id, entry);
        }
        mesh = entry.Mesh; resolved = entry.Id; return entry.Found;
    }
    private bool TryGetMesh(string id, out RuntimeMeshBuffers mesh) => TryGetMesh(id, out mesh, out _);
    private bool TryGetMesh(in WorldFeaturePresentationMetadata feature, WorldAssetLod lod, out RuntimeMeshBuffers mesh, out AssetId resolved)
    {
        WorldPresentationDefinition definition = WorldPresentationCatalog.Get(feature.Visual);
        return TryGetMesh(lod == WorldAssetLod.Reduced ? definition.ReducedLodAssetId : definition.MeshAssetId, out mesh, out resolved);
    }
    private RuntimeMaterialResources ResolveMaterial(string id)
    {
        if (_passMaterials.TryGetValue(id, out var material)) return material;
        _materialResolutions++; material = _runtimeAssets!.ResolveMaterial(id);
        _passMaterials.Add(id, material); return material;
    }

    private static Matrix4x4 CreateArticulatedTransform(
        Matrix4x4 world,
        Vector3 pivot,
        float yawRadians)
    {
        float yaw =
            float.IsFinite(
                yawRadians)
                ? yawRadians
                : 0.0f;

        if (!Matrix4x4.Decompose(world, out Vector3 scale, out _, out _)) scale = Vector3.One;
        return
            Matrix4x4.CreateTranslation(
                -pivot) *
            MechanicalMotionSpace.Rotation(scale, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)) *
            Matrix4x4.CreateTranslation(
                pivot) *
            world;
    }

    private static void WriteMatrix(
        Matrix4x4 matrix,
        Span<float> destination)
    {
        destination[0] = matrix.M11;
        destination[1] = matrix.M12;
        destination[2] = matrix.M13;
        destination[3] = matrix.M14;
        destination[4] = matrix.M21;
        destination[5] = matrix.M22;
        destination[6] = matrix.M23;
        destination[7] = matrix.M24;
        destination[8] = matrix.M31;
        destination[9] = matrix.M32;
        destination[10] = matrix.M33;
        destination[11] = matrix.M34;
        destination[12] = matrix.M41;
        destination[13] = matrix.M42;
        destination[14] = matrix.M43;
        destination[15] = matrix.M44;
    }

    private static Vector4 ResolveColor(
        in RenderInstance instance,
        Vector4? runtimeTint)
    {
        if (instance.UnitFeature.IsSpecified)
        {
            UnitPresentationDefinition definition =
                UnitPresentationCatalog.Get(
                    instance.UnitFeature.Unit);
            Vector4 baseTint =
                runtimeTint ??
                definition.FallbackTint;

            return UnitPresentationCatalog.ApplyDamageTint(
                instance.UnitFeature,
                baseTint);
        }

        if (instance.VfxFeature.IsSpecified)
        {
            VfxPresentationDefinition definition =
                VfxPresentationCatalog.Get(
                    instance.VfxFeature.Kind);

            return runtimeTint ??
                definition.FallbackTint;
        }

        if (instance.BuildingFeature.IsSpecified)
        {
            return runtimeTint ??
                BuildingPresentationCatalog.ResolveFallbackTint(
                    instance.BuildingFeature);
        }

        if (instance.ConveyorFeature.IsSpecified) return runtimeTint ?? Vector4.One;

        if (instance.InfrastructureFeature.IsSpecified)
        {
            return runtimeTint ??
                InfrastructurePresentationCatalog.ResolveFallbackTint(
                    instance.InfrastructureFeature);
        }

        if (instance.WorldFeature.IsSpecified)
        {
            Vector4 family = runtimeTint ?? WorldPresentationCatalog.Get(instance.WorldFeature.Visual).Tint;
            return WorldPresentationCatalog.ApplyStateTint(instance.WorldFeature,
                family * ResourceDepositVariantCatalog.InstanceTint(instance.WorldFeature));
        }

        uint hash =
            instance.DebugIdentity *
            2_654_435_761U;

        return new Vector4(
            0.35f +
            ((hash & 0xFFU) / 255.0f) *
            0.45f,
            0.45f +
            (((hash >> 8) & 0xFFU) / 255.0f) *
            0.35f,
            0.15f +
            (((hash >> 16) & 0xFFU) / 255.0f) *
            0.35f,
            1.0f);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static InstanceRenderData CreateInstanceRenderData(
        Matrix4x4 world,
        Vector4 color,
        bool usesRuntimeMaterial,
        in RuntimeMaterialResources material) =>
        usesRuntimeMaterial
            ? new InstanceRenderData(
                world,
                color,
                new Vector4(
                    material.UvScale.X,
                    material.UvScale.Y,
                    material.RoughnessFactor,
                    material.MetallicFactor),
                new Vector4(
                    material.EmissiveMultiplier,
                    material.UvOffset.X,
                    material.UvOffset.Y,
                    0.0f))
            : new InstanceRenderData(
                world,
                color,
                new Vector4(
                    1.0f,
                    1.0f,
                    1.0f,
                    0.0f),
                Vector4.Zero);

    private readonly record struct InstanceBatchKey(
        bool UsesRuntimeMesh,
        AssetId RuntimeMeshId,
        bool UsesRuntimeMaterial,
        AssetId RuntimeMaterialId,
        int PartIndex = -1);

    private sealed class InstanceBatch
    {
        public bool UsesRuntimeMesh { get; set; }

        public RuntimeMeshBuffers RuntimeMesh { get; set; }

        public bool UsesRuntimeMaterial { get; set; }

        public RuntimeMaterialResources RuntimeMaterial { get; set; }

        public PooledSubmissionList<InstanceRenderData> Instances { get; } = new();
        public PooledSubmissionList<int> RootIndices { get; } = new();

        public int InstanceOffset { get; set; }

        public void Reset()
        {
            Instances.Clear(); RootIndices.Clear();
            UsesRuntimeMesh = false;
            RuntimeMesh = default;
            UsesRuntimeMaterial = false;
            RuntimeMaterial = default;
            InstanceOffset = 0;
        }
    }

    private sealed class WorldLodHistory
    {
        internal ForgeLine.Simulation.SimulationSessionId Session;
        internal readonly ConditionalWeakTable<RtsCamera, CameraLodHistory> Cameras = new();
    }

    private sealed class CameraLodHistory
    {
        internal Dictionary<(EntityId Entity, uint Identity), int> Previous = [];
        internal Dictionary<(EntityId Entity, uint Identity), int> Current = [];
    }

    private sealed class FrameInstanceBuffer
    {
        public IGraphicsBuffer? Buffer { get; set; }

        public int Capacity { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct InstanceRenderData(
        Matrix4x4 World,
        Vector4 Color,
        Vector4 Material0,
        Vector4 Material1);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SimpleVertex(
        float X,
        float Y,
        float Z,
        float NormalX,
        float NormalY,
        float NormalZ,
        float U,
        float V,
        float TangentX,
        float TangentY,
        float TangentZ,
        float TangentW);
}
