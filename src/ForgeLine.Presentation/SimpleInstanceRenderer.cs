using ForgeLine.Graphics.RenderContracts;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed class SimpleInstanceRenderer : IDisposable
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

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsPipeline? _shadowPipeline;
    private readonly IGraphicsPipeline? _texturedPipeline;
    private readonly IGraphicsBuffer _vertexBuffer;
    private readonly IGraphicsBuffer _indexBuffer;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly SceneLightingSettings _lighting;
    private readonly float _lodDiameterScale;
    private readonly bool _vegetationMotion;
    private readonly bool _clusterCulling;
    private readonly VegetationRenderSettings _vegetation;
    private readonly Dictionary<int, FrameInstanceBuffer> _frameInstanceBuffers = [];
    private readonly Dictionary<int, FrameInstanceBuffer> _shadowFrameInstanceBuffers = [];
    private readonly Dictionary<InstanceBatchKey, InstanceBatch> _batchLookup = [];
    private readonly List<InstanceBatch> _batches = [];
    private Dictionary<(EntityId Entity, uint Identity), int> _previousLods = [];
    private Dictionary<(EntityId Entity, uint Identity), int> _currentLods = [];
    private InstanceRenderData[] _instanceStaging = [];
    private int _activeBatchCount;
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

        _graphics = graphics;
        try
        {
            _pipeline = InstanceMaterialPipeline.CreatePipeline(graphics, appearance);
            if (graphics.DirectionalShadows.Enabled) _shadowPipeline = DirectionalShadowCasterPipeline.Create(graphics, true);
            _runtimeAssets =
                runtimeAssets is null
                    ? null
                    : new RuntimeWorldAssetResources(
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
            int textured = 0;
            for (int i = 0; i < _activeBatchCount; i++)
                if (_batches[i].UsesRuntimeMaterial) textured++;
            int submitted = LastDiagnostics.RuntimeMeshInstances + LastDiagnostics.FallbackMeshInstances;
            return new InstanceSubmissionMetrics(_instanceStaging.Length, retained, _batches.Count,
                submitted, (long)submitted * InstanceStride, LastDiagnostics.DrawCalls, textured * 4)
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
        => RenderCore(context, camera, world, alpha, presentationSeconds: presentationSeconds, particles: particles);

    public void RenderShadow(IGraphicsCommandContext context, RtsCamera camera, RenderWorld world, float alpha,
        GraphicsDirectionalShadowFrame frame, int maximumCasters, int maximumDraws, double? presentationSeconds = null)
    {
        frame.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCasters);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDraws);
        InstanceRenderDiagnostics prior = LastDiagnostics;
        RenderCore(context, camera, world, alpha, frame.ViewProjection, maximumCasters, maximumDraws, presentationSeconds);
        LastDiagnostics = prior;
    }

    private void RenderCore(IGraphicsCommandContext context, RtsCamera camera, RenderWorld world, float alpha,
        Matrix4x4? shadowProjection = null, int maximumCasters = int.MaxValue, int maximumDraws = int.MaxValue,
        double? presentationSeconds = null, ParticleVfxRenderer? particles = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        if (presentationSeconds is { } seconds && (!double.IsFinite(seconds) || seconds < 0))
            throw new ArgumentOutOfRangeException(nameof(presentationSeconds));

        _skinContextFrame = context.FrameIndex; _skinContextShadow = shadowProjection.HasValue;
        _snapshotIndexBytes = world.IndexRetainedBytes;
        if (!_vegetationMotion) presentationSeconds = null;

        CameraMatrices matrices =
            camera.GetMatrices(
                context.Width,
                context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(
                shadowProjection ?? matrices.ViewProjection);
        ResetScratch(shadowProjection.HasValue);
        _skinFrames?.Begin(context.FrameIndex, shadowProjection.HasValue);
        if (!shadowProjection.HasValue) _currentLods.Clear();
        var batchLookup = _batchLookup;
        var batches = _batches;

        int clusterTests = 0, rejectedClusters = 0, clusterCulled = 0, instanceTests = 0;
        int visible = 0;
        int animatedVegetation = 0;
        int visibleVegetation = 0;
        int skippedDecalResources = 0;
        int effectRequests = 0, meshFallbackEffects = 0;
        int highLod = 0;
        int reducedLod = 0;
        int lod1Instances = 0;
        int lod2Instances = 0;

        for (int index = 0; index < world.InstanceCount; index++)
        {
            if (visible >= maximumCasters) break;
            if (_clusterCulling && world.InstanceCount >= RenderSnapshotIndex.ClusterSize && index % RenderSnapshotIndex.ClusterSize == 0)
            {
                clusterTests++;
                if (!world.ClusterVisible(index, frustum))
                {
                    int skipped = Math.Min(RenderSnapshotIndex.ClusterSize, world.InstanceCount - index);
                    rejectedClusters++; clusterCulled += skipped; index += skipped - 1;
                    continue;
                }
            }
            RenderInstance instance =
                world.GetInterpolatedInstance(
                    index,
                    alpha);
            if (shadowProjection.HasValue && (instance.VfxFeature.IsSpecified || instance.DecalFeature.IsSpecified)) continue;


            if ((instance.Visibility & RenderVisibilityMask.World) == 0 ||
                !_vegetation.Includes(instance, camera.Position) ||
                !instance.Mesh.IsValid ||
                !instance.Material.IsValid)
            {
                continue;
            }

            if (instance.VfxFeature.IsSpecified && ++effectRequests > PresentationExtractor.MaximumSnapshotEffects) continue;
            if (!shadowProjection.HasValue && particles?.Replaces(instance) == true) continue;
            if (instance.VfxFeature.IsSpecified && ++meshFallbackEffects > (particles?.MaximumMeshFallbackEffects ?? 128)) continue;
            AxisAlignedBounds bounds =
                instance.MechanicalPose.Infantry.IsSpecified ? SkinPresentationBounds.Resolve(instance) :
                presentationSeconds.HasValue ? VegetationVisualMotion.CullingBounds(instance) :
                    SkinPresentationBounds.Resolve(instance);

            instanceTests++;
            if (!frustum.Intersects(
                    bounds))
            {
                continue;
            }

            visible++;
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
                matrices, context.Height);
            var lodKey = (instance.Entity, instance.DebugIdentity);
            bool hasLodIdentity = instance.Entity.IsValid || instance.DebugIdentity != 0;
            var lodHistory = shadowProjection.HasValue ? _currentLods : _previousLods;
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
                if (hasLodIdentity && !shadowProjection.HasValue) _currentLods[lodKey] = (int)unitLod;
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
                    _runtimeAssets.TryGetMesh(
                        meshAssetId,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

                if (!usesRuntimeMesh && _runtimeAssets is not null && unitLod != UnitAssetLod.Lod0 && !instance.UnitFeature.IsWreck)
                {
                    unitLod = UnitAssetLod.Lod0;
                    usesRuntimeMesh = _runtimeAssets.TryGetMesh(unitDefinition.MeshAssetId,
                        out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                    if (hasLodIdentity && !shadowProjection.HasValue) _currentLods[lodKey] = (int)unitLod;
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
                if (hasLodIdentity && !shadowProjection.HasValue) _currentLods[lodKey] = (int)buildingLod;
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
                    _runtimeAssets.TryGetMesh(
                        meshAssetId,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

                if (!usesRuntimeMesh && _runtimeAssets is not null && buildingLod != BuildingAssetLod.Lod0)
                {
                    buildingLod = BuildingAssetLod.Lod0;
                    usesRuntimeMesh = _runtimeAssets.TryGetMesh(
                        BuildingPresentationCatalog.ResolveMeshAssetId(instance.BuildingFeature, buildingLod),
                        out runtimeMesh, out runtimeMeshId) && runtimeMesh.IsValid;
                    if (hasLodIdentity && !shadowProjection.HasValue) _currentLods[lodKey] = (int)buildingLod;
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
                    _runtimeAssets.TryGetMesh(
                        definition.MeshAssetId,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

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
                    _runtimeAssets.TryGetMesh(
                        meshAssetId,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

                highLod++;
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
                if (hasLodIdentity && !shadowProjection.HasValue) _currentLods[lodKey] = worldLod == WorldAssetLod.Reduced ? 1 : 0;

                usesRuntimeMesh =
                    _runtimeAssets is not null &&
                    _runtimeAssets.TryGetMesh(
                        instance.WorldFeature,
                        worldLod,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

                if (worldLod == WorldAssetLod.Reduced)
                {
                    lod1Instances++;
                    reducedLod++;
                }
                else
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
                !hasBuildingDefinition)
            {
                materialAssetId =
                    runtimeMesh.MaterialId.Value;
            }

            bool hasRuntimeMaterial =
                _runtimeAssets is not null &&
                materialAssetId is not null;
            RuntimeMaterialResources runtimeMaterial =
                hasRuntimeMaterial
                    ? _runtimeAssets!.ResolveMaterial(
                        materialAssetId!)
                    : default;
            bool usesRuntimeMaterial =
                hasRuntimeMaterial &&
                (!usesRuntimeMesh ||
                 runtimeMesh.SupportsTexturedMaterial);
            if (instance.DecalFeature.IsSpecified && (!usesRuntimeMesh || !usesRuntimeMaterial || runtimeMaterial.UsesDevelopmentFallback || !float.IsFinite(instance.DecalFeature.Opacity)))
            { skippedDecalResources++; continue; }
            Vector4 color =
                ResolveColor(
                    instance,
                    hasRuntimeMaterial
                        ? runtimeMaterial.BaseColorFactor
                        : null);
            if (instance.DecalFeature.IsSpecified) color.W *= Math.Clamp(instance.DecalFeature.Opacity, 0, .65f);
            Matrix4x4 worldMatrix =
                presentationSeconds.HasValue ? VegetationVisualMotion.Transform(instance, projectedDiameter, presentationSeconds.Value) :
                    instance.Transform.ToMatrix();
            if (presentationSeconds.HasValue && projectedDiameter > 18 && VegetationVisualMotion.Applies(instance)) animatedVegetation++;

            if (instance.InfrastructureFeature.IsSpecified)
            {
                worldMatrix =
                    InfrastructurePresentationCatalog.AdjustWorldTransform(
                        instance.InfrastructureFeature,
                        worldMatrix);
            }

            _skinDetail = MechanicalDetailWeight(projectedDiameter, 90, _lodDiameterScale);
            float motionWeight = MechanicalDetailWeight(projectedDiameter, hasBuildingDefinition ? 150 : 90, _lodDiameterScale);
            AddMechanicalBatches(instance, worldMatrix, color, usesRuntimeMesh, runtimeMesh,
                runtimeMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
            if (hasUnitDefinition &&
                unitLod == UnitAssetLod.Lod0 &&
                !instance.UnitFeature.IsWreck &&
                unitDefinition.HasArticulatedTurret &&
                _runtimeAssets is not null &&
                _runtimeAssets.TryGetMesh(
                    unitDefinition.TurretMeshAssetId!,
                    out RuntimeMeshBuffers turretMesh,
                    out AssetId turretMeshId) &&
                turretMesh.IsValid)
            {
                AddMechanicalBatches(instance,
                    CreateArticulatedTransform(worldMatrix, unitDefinition.TurretPivot, instance.UnitFeature.AimYawRadians * motionWeight),
                    color, true, turretMesh, turretMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
            }
            string? unitStateAssetId = hasUnitDefinition
                ? UnitPresentationCatalog.ResolveStateAttachmentAssetId(instance.UnitFeature, unitLod) : null;
            if (unitStateAssetId is not null && _runtimeAssets is not null &&
                _runtimeAssets.TryGetMesh(unitStateAssetId, out RuntimeMeshBuffers unitStateMesh, out AssetId unitStateMeshId) &&
                unitStateMesh.IsValid)
            {
                AddMechanicalBatches(instance, worldMatrix, color, true, unitStateMesh,
                    unitStateMeshId, usesRuntimeMaterial, runtimeMaterial, motionWeight);
            }
            string? buildingStateAssetId =
                hasBuildingDefinition
                    ? BuildingPresentationCatalog.ResolveStateAttachmentAssetId(
                        instance.BuildingFeature)
                    : null;

            if (buildingStateAssetId is not null &&
                _runtimeAssets is not null &&
                _runtimeAssets.TryGetMesh(
                    buildingStateAssetId,
                    out RuntimeMeshBuffers stateMesh,
                    out AssetId stateMeshId) &&
                stateMesh.IsValid)
            {
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

                stateBatch.Instances.Add(
                    CreateInstanceRenderData(
                        worldMatrix,
                        color,
                        usesRuntimeMaterial,
                        runtimeMaterial));
            }
        }

        if (visible == 0 || _activeBatchCount == 0)
        {
            LastDiagnostics =
                new InstanceRenderDiagnostics(
                    world.InstanceCount,
                    0,
                    world.InstanceCount,
                    0,
                    highLod,
                    reducedLod) { Lod1Instances = lod1Instances, Lod2Instances = lod2Instances, SkippedDecalResources = skippedDecalResources, ClusterTests = clusterTests, RejectedClusters = rejectedClusters, ClusterCulledInstances = clusterCulled, InstanceBoundsTests = instanceTests };
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

        IGraphicsBuffer instanceBuffer =
            GetFrameInstanceBuffer(
                context.FrameIndex,
                submittedInstances, shadowProjection.HasValue);
        if (_instanceStaging.Length < submittedInstances)
        {
            int capacity = Math.Max(MinimumInstanceCapacity, _instanceStaging.Length);
            while (capacity < submittedInstances) capacity = checked(capacity * 2);
            _instanceStaging = new InstanceRenderData[capacity];
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
        int shadowSubmitted = 0;

        for (int batchIndex = 0;
             batchIndex < _activeBatchCount;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];

            IGraphicsPipeline pipeline =
                shadowProjection.HasValue ? _shadowPipeline ?? throw new NotSupportedException("Object shadows were not configured at construction.") : batch.UsesRuntimeMaterial
                    ? _texturedPipeline ??
                      throw new InvalidOperationException(
                          "The textured instance pipeline is unavailable.")
                    : _pipeline;
            context.SetPipeline(
                pipeline);
            context.SetVertexConstants(
                shadowProjection.HasValue ? constants[..16] : constants);

            if (batch.UsesRuntimeMaterial && !shadowProjection.HasValue)
            {
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
                    AnimatedVegetationInstances = animatedVegetation, VisibleVegetationInstances = visibleVegetation, SkippedDecalResources = skippedDecalResources, ClusterTests = clusterTests, RejectedClusters = rejectedClusters, ClusterCulledInstances = clusterCulled, InstanceBoundsTests = instanceTests };
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
        _batches.Clear();
        _instanceStaging = [];
        _previousLods.Clear();
        _currentLods.Clear();
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
        if (!shadow) (_previousLods, _currentLods) = (_currentLods, _previousLods);
        // Only the render owner uses scratch; no span escapes synchronous SetData.
        _batchLookup.Clear();
        long retainedInstances = 0;
        foreach (InstanceBatch batch in _batches)
        {
            retainedInstances += batch.Instances.Capacity;
            batch.Reset();
        }
        if (_batches.Count > MaximumRetainedScratchBatches ||
            retainedInstances > MaximumRetainedScratchInstances)
        {
            _batches.Clear();
            _batches.TrimExcess();
            _batchLookup.TrimExcess();
        }
        if (_instanceStaging.Length > MaximumRetainedScratchInstances)
            _instanceStaging = [];
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
            batch.Instances.Add(CreateInstanceRenderData(articulated ? Matrix4x4.CreateTranslation(parts[i].GeometryOffset) * transforms[i] : world,
                color, usesMaterial, material));
        }
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

        if (instance.InfrastructureFeature.IsSpecified)
        {
            return runtimeTint ??
                InfrastructurePresentationCatalog.ResolveFallbackTint(
                    instance.InfrastructureFeature);
        }

        if (instance.WorldFeature.IsSpecified)
        {
            return runtimeTint.HasValue
                ? WorldPresentationCatalog.ApplyStateTint(
                    instance.WorldFeature,
                    runtimeTint.Value)
                : WorldPresentationCatalog.ResolveTint(
                    instance.WorldFeature);
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

        public List<InstanceRenderData> Instances { get; } =
            [];

        public int InstanceOffset { get; set; }

        public void Reset()
        {
            Instances.Clear();
            UsesRuntimeMesh = false;
            RuntimeMesh = default;
            UsesRuntimeMaterial = false;
            RuntimeMaterial = default;
            InstanceOffset = 0;
        }
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
