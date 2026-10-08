using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class SimpleInstanceRenderer : IDisposable
{
    private const int MatrixRootConstantCount = 16;
    private const int RootConstantCount =
        MatrixRootConstantCount +
        SceneLightingSettings.ShaderConstantCount;
    private const int FallbackVertexStride = 48;
    private const int InstanceStride = 112;
    private const int MinimumInstanceCapacity = 64;
    private const int MaximumRetainedScratchInstances = 65_536;
    private const int MaximumRetainedScratchBatches = 256;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsPipeline? _texturedPipeline;
    private readonly IGraphicsBuffer _vertexBuffer;
    private readonly IGraphicsBuffer _indexBuffer;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly SceneLightingSettings _lighting;
    private readonly Dictionary<int, FrameInstanceBuffer> _frameInstanceBuffers = [];
    private readonly Dictionary<InstanceBatchKey, InstanceBatch> _batchLookup = [];
    private readonly List<InstanceBatch> _batches = [];
    private InstanceRenderData[] _instanceStaging = [];
    private int _activeBatchCount;
    private bool _disposed;

    public SimpleInstanceRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _lighting =
            lighting ??
            SceneLightingSettings.Default;
        _lighting.Validate();

        _graphics = graphics;
        try
        {
            _pipeline = CreatePipeline(graphics);
            _runtimeAssets =
                runtimeAssets is null
                    ? null
                    : new RuntimeWorldAssetResources(
                        graphics,
                        runtimeAssets);
            _texturedPipeline =
                _runtimeAssets is null
                    ? null
                    : CreateTexturedPipeline(
                        graphics);

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

            _vertexBuffer = graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked((ulong)vertices.Length * FallbackVertexStride),
                    GraphicsBufferMemory.Upload));
            _indexBuffer = graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked((ulong)indices.Length * sizeof(ushort)),
                    GraphicsBufferMemory.Upload));

            _vertexBuffer.SetData<SimpleVertex>(
                vertices);
            _indexBuffer.SetData<ushort>(
                indices);
        }
        catch
        {
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();
            _texturedPipeline?.Dispose();
            _runtimeAssets?.Dispose();
            _pipeline?.Dispose();
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
            int textured = 0;
            for (int i = 0; i < _activeBatchCount; i++)
                if (_batches[i].UsesRuntimeMaterial) textured++;
            int submitted = LastDiagnostics.RuntimeMeshInstances + LastDiagnostics.FallbackMeshInstances;
            return new InstanceSubmissionMetrics(_instanceStaging.Length, retained, _batches.Count,
                submitted, (long)submitted * InstanceStride, LastDiagnostics.DrawCalls, textured * 4);
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
        float alpha)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);

        CameraMatrices matrices =
            camera.GetMatrices(
                context.Width,
                context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(
                matrices.ViewProjection);
        ResetScratch();
        var batchLookup = _batchLookup;
        var batches = _batches;

        int visible = 0;
        int highLod = 0;
        int reducedLod = 0;

        for (int index = 0; index < world.InstanceCount; index++)
        {
            RenderInstance instance =
                world.GetInterpolatedInstance(
                    index,
                    alpha);

            if ((instance.Visibility & RenderVisibilityMask.World) == 0 ||
                !instance.Mesh.IsValid ||
                !instance.Material.IsValid)
            {
                continue;
            }

            AxisAlignedBounds bounds =
                PresentationBounds.ResolveAxisAlignedBounds(
                    instance);

            if (!frustum.Intersects(
                    bounds))
            {
                continue;
            }

            visible++;

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
                    UnitPresentationCatalog.SelectLod(
                        instance.UnitFeature,
                        distance);
                unitDefinition =
                    UnitPresentationCatalog.Get(
                        instance.UnitFeature.Unit);
                materialAssetId =
                    unitDefinition.MaterialAssetId;
                hasUnitDefinition =
                    true;
                string meshAssetId =
                    instance.UnitFeature.IsWreck
                        ? unitDefinition.Lod2AssetId
                        : unitDefinition.GetMeshAssetId(
                            unitLod);

                usesRuntimeMesh =
                    _runtimeAssets is not null &&
                    _runtimeAssets.TryGetMesh(
                        meshAssetId,
                        out runtimeMesh,
                        out runtimeMeshId) &&
                    runtimeMesh.IsValid;

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
                    BuildingPresentationCatalog.SelectLod(
                        instance.BuildingFeature,
                        distance);
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
                    WorldPresentationCatalog.SelectLod(
                        instance.WorldFeature,
                        distance);

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
                runtimeMesh.HasMaterial)
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
            Vector4 color =
                ResolveColor(
                    instance,
                    hasRuntimeMaterial
                        ? runtimeMaterial.BaseColorFactor
                        : null);
            Matrix4x4 worldMatrix =
                instance.Transform.ToMatrix();

            if (instance.InfrastructureFeature.IsSpecified)
            {
                worldMatrix =
                    InfrastructurePresentationCatalog.AdjustWorldTransform(
                        instance.InfrastructureFeature,
                        worldMatrix);
            }

            var key =
                new InstanceBatchKey(
                    usesRuntimeMesh,
                    runtimeMeshId,
                    usesRuntimeMaterial,
                    usesRuntimeMaterial
                        ? runtimeMaterial.MaterialId
                        : default);

            if (!batchLookup.TryGetValue(
                    key,
                    out InstanceBatch? batch))
            {
                batch =
                    RentBatch(
                        usesRuntimeMesh,
                        runtimeMesh,
                        usesRuntimeMaterial,
                        runtimeMaterial);
                batchLookup.Add(
                    key,
                    batch);
            }

            batch.Instances.Add(
                CreateInstanceRenderData(
                    worldMatrix,
                    color,
                    usesRuntimeMaterial,
                    runtimeMaterial));

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
                var turretKey =
                    new InstanceBatchKey(
                        true,
                        turretMeshId,
                        usesRuntimeMaterial,
                        runtimeMaterial.MaterialId);

                if (!batchLookup.TryGetValue(
                        turretKey,
                        out InstanceBatch? turretBatch))
                {
                    turretBatch =
                        RentBatch(
                            true,
                            turretMesh,
                            usesRuntimeMaterial,
                            runtimeMaterial);
                    batchLookup.Add(
                        turretKey,
                        turretBatch);
                }

                turretBatch.Instances.Add(
                    CreateInstanceRenderData(
                        CreateArticulatedTransform(
                            worldMatrix,
                            unitDefinition.TurretPivot,
                            instance.UnitFeature.AimYawRadians),
                        color,
                        usesRuntimeMaterial,
                        runtimeMaterial));
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

        if (visible == 0)
        {
            LastDiagnostics =
                new InstanceRenderDiagnostics(
                    world.InstanceCount,
                    0,
                    world.InstanceCount,
                    0,
                    highLod,
                    reducedLod);
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
                submittedInstances);
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
            matrices.ViewProjection,
            constants);
        _lighting.WriteShaderConstants(
            constants[
                MatrixRootConstantCount..]);

        int draws = 0;

        for (int batchIndex = 0;
             batchIndex < _activeBatchCount;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];

            IGraphicsPipeline pipeline =
                batch.UsesRuntimeMaterial
                    ? _texturedPipeline ??
                      throw new InvalidOperationException(
                          "The textured instance pipeline is unavailable.")
                    : _pipeline;
            context.SetPipeline(
                pipeline);
            context.SetVertexConstants(
                constants);

            if (batch.UsesRuntimeMaterial)
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
            context.DrawIndexedInstanced(
                batch.UsesRuntimeMesh
                    ? batch.RuntimeMesh.IndexCount
                    : 36,
                batch.Instances.Count);
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
                fallbackMeshInstances);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (FrameInstanceBuffer frameBuffer in
                 _frameInstanceBuffers.Values)
        {
            frameBuffer.Buffer?.Dispose();
        }

        _frameInstanceBuffers.Clear();
        _batchLookup.Clear();
        _batches.Clear();
        _instanceStaging = [];
        _runtimeAssets?.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _texturedPipeline?.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private void ResetScratch()
    {
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
        int requiredInstances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            frameIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            requiredInstances);

        if (!_frameInstanceBuffers.TryGetValue(
                frameIndex,
                out FrameInstanceBuffer? frameBuffer))
        {
            frameBuffer =
                new FrameInstanceBuffer();
            _frameInstanceBuffers.Add(
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

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer InstanceFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float4 WorldRow0 : INSTANCEWORLD0;
                float4 WorldRow1 : INSTANCEWORLD1;
                float4 WorldRow2 : INSTANCEWORLD2;
                float4 WorldRow3 : INSTANCEWORLD3;
                float4 Color : INSTANCECOLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldNormal : TEXCOORD0;
                nointerpolation float4 SceneLight : TEXCOORD1;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD2;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD3;
                nointerpolation float2 SceneGroundTone : TEXCOORD4;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                row_major float4x4 world =
                    float4x4(
                        input.WorldRow0,
                        input.WorldRow1,
                        input.WorldRow2,
                        input.WorldRow3);
                float4 worldPosition =
                    mul(
                        float4(
                            input.Position,
                            1.0f),
                        world);
                float3x3 worldBasis =
                    (float3x3)world;

                output.Position =
                    mul(
                        worldPosition,
                        ViewProjection);
                output.Color =
                    input.Color;
                output.WorldNormal =
                    normalize(
                        mul(
                            input.Normal,
                            worldBasis));
                output.SceneLight =
                    SceneLightDirectionIntensity;
                output.SceneDirectionalAmbient =
                    SceneDirectionalColorAmbientIntensity;
                output.SceneAmbientExposure =
                    SceneAmbientColorExposure;
                output.SceneGroundTone =
                    SceneGroundAmbientToneMapping;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldNormal : TEXCOORD0;
                nointerpolation float4 SceneLight : TEXCOORD1;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD2;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD3;
                nointerpolation float2 SceneGroundTone : TEXCOORD4;
            };

            float3 ToneMapAces(float3 value)
            {
                const float a = 2.51f;
                const float b = 0.03f;
                const float c = 2.43f;
                const float d = 0.59f;
                const float e = 0.14f;

                return saturate(
                    (value *
                        (a * value + b)) /
                    (value *
                        (c * value + d) +
                     e));
            }

            float3 LinearToSrgb(float3 value)
            {
                value =
                    max(
                        value,
                        0.0f);
                float3 low =
                    value *
                    12.92f;
                float3 high =
                    1.055f *
                    pow(
                        max(
                            value,
                            0.0031308f),
                        1.0f / 2.4f) -
                    0.055f;

                return lerp(
                    low,
                    high,
                    step(
                        float3(
                            0.0031308f,
                            0.0031308f,
                            0.0031308f),
                        value));
            }

            float3 ApplySceneLighting(
                float3 albedo,
                float3 worldNormal,
                float ambientOcclusion,
                float roughness,
                float metallic,
                float3 emissive,
                float4 sceneLight,
                float4 sceneDirectionalAmbient,
                float4 sceneAmbientExposure,
                float2 sceneGroundTone)
            {
                float3 normal =
                    normalize(
                        worldNormal);
                float lightFacing =
                    saturate(
                        dot(
                            normal,
                            normalize(
                                sceneLight.xyz)));
                float hemisphere =
                    saturate(
                        normal.y *
                            0.5f +
                        0.5f);
                float ambientShape =
                    lerp(
                        sceneGroundTone.x,
                        1.0f,
                        hemisphere);
                float aoResponse =
                    lerp(
                        0.55f,
                        1.0f,
                        saturate(
                            ambientOcclusion));
                float3 ambient =
                    sceneAmbientExposure.rgb *
                    sceneDirectionalAmbient.w *
                    ambientShape *
                    aoResponse;
                float3 direct =
                    sceneDirectionalAmbient.rgb *
                    sceneLight.w *
                    lightFacing;
                float diffuseEnergy =
                    lerp(
                        1.0f,
                        0.72f,
                        saturate(
                            metallic));
                float roughnessResponse =
                    lerp(
                        1.02f,
                        0.94f,
                        saturate(
                            roughness));
                float3 linearColor =
                    albedo *
                    (ambient + direct) *
                    diffuseEnergy *
                    roughnessResponse +
                    emissive;

                linearColor *=
                    sceneAmbientExposure.w;

                if (sceneGroundTone.y >= 0.5f)
                {
                    linearColor =
                        ToneMapAces(
                            linearColor);
                }
                else
                {
                    linearColor =
                        saturate(
                            linearColor);
                }

                return LinearToSrgb(
                    saturate(
                        linearColor));
            }

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float3 color =
                    ApplySceneLighting(
                        input.Color.rgb,
                        input.WorldNormal,
                        1.0f,
                        0.82f,
                        0.0f,
                        0.0f,
                        input.SceneLight,
                        input.SceneDirectionalAmbient,
                        input.SceneAmbientExposure,
                        input.SceneGroundTone);

                return float4(
                    color,
                    input.Color.a);
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "InstanceVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "InstancePixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        0),
                    new GraphicsVertexElement(
                        "NORMAL",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        12),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        0,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        16,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        2,
                        GraphicsVertexElementFormat.Float4,
                        32,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        3,
                        GraphicsVertexElementFormat.Float4,
                        48,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCECOLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        64,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1)
                ],
                VertexRootConstantCount =
                    RootConstantCount,
                DepthEnabled =
                    true
            });
    }

    private static IGraphicsPipeline CreateTexturedPipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer InstanceFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float2 Uv : TEXCOORD0;
                float4 Tangent : TANGENT;
                float4 WorldRow0 : INSTANCEWORLD0;
                float4 WorldRow1 : INSTANCEWORLD1;
                float4 WorldRow2 : INSTANCEWORLD2;
                float4 WorldRow3 : INSTANCEWORLD3;
                float4 Color : INSTANCECOLOR0;
                float4 Material0 : INSTANCEMATERIAL0;
                float4 Material1 : INSTANCEMATERIAL1;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float2 Uv : TEXCOORD0;
                float3 WorldNormal : TEXCOORD1;
                float4 WorldTangent : TEXCOORD2;
                float4 Material0 : TEXCOORD3;
                float4 Material1 : TEXCOORD4;
                nointerpolation float4 SceneLight : TEXCOORD5;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD6;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD7;
                nointerpolation float2 SceneGroundTone : TEXCOORD8;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                row_major float4x4 world =
                    float4x4(
                        input.WorldRow0,
                        input.WorldRow1,
                        input.WorldRow2,
                        input.WorldRow3);
                float4 worldPosition =
                    mul(
                        float4(
                            input.Position,
                            1.0f),
                        world);
                float3x3 worldBasis =
                    (float3x3)world;

                output.Position =
                    mul(
                        worldPosition,
                        ViewProjection);
                output.Color =
                    input.Color;
                output.Uv =
                    input.Uv;
                output.WorldNormal =
                    normalize(
                        mul(
                            input.Normal,
                            worldBasis));
                output.WorldTangent =
                    float4(
                        mul(
                            input.Tangent.xyz,
                            worldBasis),
                        input.Tangent.w);
                output.Material0 =
                    input.Material0;
                output.Material1 =
                    input.Material1;
                output.SceneLight =
                    SceneLightDirectionIntensity;
                output.SceneDirectionalAmbient =
                    SceneDirectionalColorAmbientIntensity;
                output.SceneAmbientExposure =
                    SceneAmbientColorExposure;
                output.SceneGroundTone =
                    SceneGroundAmbientToneMapping;
                return output;
            }
            """;

        const string pixelShaderSource = """
            Texture2D BaseColorTexture : register(t0);
            Texture2D NormalTexture : register(t1);
            Texture2D OrmTexture : register(t2);
            Texture2D EmissiveTexture : register(t3);
            SamplerState WorldMaterialSampler : register(s0);

            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float2 Uv : TEXCOORD0;
                float3 WorldNormal : TEXCOORD1;
                float4 WorldTangent : TEXCOORD2;
                float4 Material0 : TEXCOORD3;
                float4 Material1 : TEXCOORD4;
                nointerpolation float4 SceneLight : TEXCOORD5;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD6;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD7;
                nointerpolation float2 SceneGroundTone : TEXCOORD8;
            };

            float3 BuildFallbackTangent(float3 normal)
            {
                float3 axis =
                    abs(normal.y) < 0.999f
                        ? float3(0.0f, 1.0f, 0.0f)
                        : float3(1.0f, 0.0f, 0.0f);
                return normalize(
                    cross(
                        axis,
                        normal));
            }

            float3 ToneMapAces(float3 value)
            {
                const float a = 2.51f;
                const float b = 0.03f;
                const float c = 2.43f;
                const float d = 0.59f;
                const float e = 0.14f;

                return saturate(
                    (value *
                        (a * value + b)) /
                    (value *
                        (c * value + d) +
                     e));
            }

            float3 LinearToSrgb(float3 value)
            {
                value =
                    max(
                        value,
                        0.0f);
                float3 low =
                    value *
                    12.92f;
                float3 high =
                    1.055f *
                    pow(
                        max(
                            value,
                            0.0031308f),
                        1.0f / 2.4f) -
                    0.055f;

                return lerp(
                    low,
                    high,
                    step(
                        float3(
                            0.0031308f,
                            0.0031308f,
                            0.0031308f),
                        value));
            }

            float3 ApplySceneLighting(
                float3 albedo,
                float3 worldNormal,
                float ambientOcclusion,
                float roughness,
                float metallic,
                float3 emissive,
                float4 sceneLight,
                float4 sceneDirectionalAmbient,
                float4 sceneAmbientExposure,
                float2 sceneGroundTone)
            {
                float3 normal =
                    normalize(
                        worldNormal);
                float lightFacing =
                    saturate(
                        dot(
                            normal,
                            normalize(
                                sceneLight.xyz)));
                float hemisphere =
                    saturate(
                        normal.y *
                            0.5f +
                        0.5f);
                float ambientShape =
                    lerp(
                        sceneGroundTone.x,
                        1.0f,
                        hemisphere);
                float aoResponse =
                    lerp(
                        0.55f,
                        1.0f,
                        saturate(
                            ambientOcclusion));
                float3 ambient =
                    sceneAmbientExposure.rgb *
                    sceneDirectionalAmbient.w *
                    ambientShape *
                    aoResponse;
                float3 direct =
                    sceneDirectionalAmbient.rgb *
                    sceneLight.w *
                    lightFacing;
                float diffuseEnergy =
                    lerp(
                        1.0f,
                        0.72f,
                        saturate(
                            metallic));
                float roughnessResponse =
                    lerp(
                        1.02f,
                        0.94f,
                        saturate(
                            roughness));
                float3 linearColor =
                    albedo *
                    (ambient + direct) *
                    diffuseEnergy *
                    roughnessResponse +
                    emissive;

                linearColor *=
                    sceneAmbientExposure.w;

                if (sceneGroundTone.y >= 0.5f)
                {
                    linearColor =
                        ToneMapAces(
                            linearColor);
                }
                else
                {
                    linearColor =
                        saturate(
                            linearColor);
                }

                return LinearToSrgb(
                    saturate(
                        linearColor));
            }

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float2 uv =
                    input.Uv *
                    input.Material0.xy;
                float4 baseColor =
                    BaseColorTexture.Sample(
                        WorldMaterialSampler,
                        uv);
                float3 tangentNormal =
                    NormalTexture.Sample(
                        WorldMaterialSampler,
                        uv).xyz *
                    2.0f -
                    1.0f;
                float3 orm =
                    OrmTexture.Sample(
                        WorldMaterialSampler,
                        uv).rgb;
                float3 emissive =
                    EmissiveTexture.Sample(
                        WorldMaterialSampler,
                        uv).rgb;

                float3 normal =
                    normalize(
                        input.WorldNormal);
                float3 tangent =
                    input.WorldTangent.xyz -
                    normal *
                    dot(
                        normal,
                        input.WorldTangent.xyz);
                tangent =
                    dot(
                        tangent,
                        tangent) >
                    1e-8f
                        ? normalize(tangent)
                        : BuildFallbackTangent(
                            normal);
                float handedness =
                    input.WorldTangent.w < 0.0f
                        ? -1.0f
                        : 1.0f;
                float3 bitangent =
                    normalize(
                        cross(
                            normal,
                            tangent)) *
                    handedness;
                float3 worldNormal =
                    normalize(
                        tangent *
                        tangentNormal.x +
                        bitangent *
                        tangentNormal.y +
                        normal *
                        tangentNormal.z);

                float ambientOcclusion =
                    saturate(
                        orm.r);
                float roughness =
                    saturate(
                        orm.g *
                        input.Material0.z);
                float metallic =
                    saturate(
                        orm.b *
                        input.Material0.w);
                float3 color =
                    ApplySceneLighting(
                        baseColor.rgb *
                        input.Color.rgb,
                        worldNormal,
                        ambientOcclusion,
                        roughness,
                        metallic,
                        emissive *
                        input.Material1.x,
                        input.SceneLight,
                        input.SceneDirectionalAmbient,
                        input.SceneAmbientExposure,
                        input.SceneGroundTone);

                return float4(
                    color,
                    baseColor.a *
                    input.Color.a);
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "InstanceMaterialVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "InstanceMaterialPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        0),
                    new GraphicsVertexElement(
                        "NORMAL",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        12),
                    new GraphicsVertexElement(
                        "TEXCOORD",
                        0,
                        GraphicsVertexElementFormat.Float2,
                        24),
                    new GraphicsVertexElement(
                        "TANGENT",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        32),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        0,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        16,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        2,
                        GraphicsVertexElementFormat.Float4,
                        32,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        3,
                        GraphicsVertexElementFormat.Float4,
                        48,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCECOLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        64,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEMATERIAL",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        80,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEMATERIAL",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        96,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1)
                ],
                VertexRootConstantCount =
                    RootConstantCount,
                PixelTextureCount =
                    4,
                DepthEnabled =
                    true
            });
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

        return
            Matrix4x4.CreateTranslation(
                -pivot) *
            Matrix4x4.CreateRotationY(
                yaw) *
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
                    0.0f,
                    0.0f,
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
        AssetId RuntimeMaterialId);

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
