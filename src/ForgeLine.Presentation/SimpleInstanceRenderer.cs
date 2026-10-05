using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class SimpleInstanceRenderer : IDisposable
{
    private const int RootConstantCount = 16;
    private const int FallbackVertexStride = 48;
    private const int InstanceStride = 112;
    private const int MinimumInstanceCapacity = 64;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsPipeline? _texturedPipeline;
    private readonly IGraphicsBuffer _vertexBuffer;
    private readonly IGraphicsBuffer _indexBuffer;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly Dictionary<int, FrameInstanceBuffer> _frameInstanceBuffers = [];
    private bool _disposed;

    public SimpleInstanceRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
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
            new(-0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f,  0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new( 0.5f,  0.5f,  0.5f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f),
            new(-0.5f,  0.5f,  0.5f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f)
        ];

        ushort[] indices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            3, 7, 6, 3, 6, 2,
            1, 2, 6, 1, 6, 5,
            0, 4, 7, 0, 7, 3
        ];

        _vertexBuffer = graphics.CreateBuffer(
            new GraphicsBufferDescription(
                checked((ulong)vertices.Length * FallbackVertexStride),
                GraphicsBufferMemory.Upload));
        _indexBuffer = graphics.CreateBuffer(
            new GraphicsBufferDescription(
                checked((ulong)indices.Length * sizeof(ushort)),
                GraphicsBufferMemory.Upload));

        try
        {
            _vertexBuffer.SetData<SimpleVertex>(
                vertices);
            _indexBuffer.SetData<ushort>(
                indices);
        }
        catch
        {
            _indexBuffer.Dispose();
            _vertexBuffer.Dispose();
            _pipeline.Dispose();
            throw;
        }
    }

    public InstanceRenderDiagnostics LastDiagnostics { get; private set; }

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
        var batchLookup =
            new Dictionary<InstanceBatchKey, InstanceBatch>();
        var batches =
            new List<InstanceBatch>();

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

            Vector3 extents =
                Vector3.Max(
                    Vector3.Abs(
                        instance.Transform.Scale) *
                    0.5f,
                    new Vector3(
                        0.05f));
            var bounds =
                new AxisAlignedBounds(
                    instance.Transform.Position - extents,
                    instance.Transform.Position + extents);

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
                    new InstanceBatch(
                        usesRuntimeMesh,
                        runtimeMesh,
                        usesRuntimeMaterial,
                        runtimeMaterial);
                batchLookup.Add(
                    key,
                    batch);
                batches.Add(
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
                        new InstanceBatch(
                            true,
                            turretMesh,
                            usesRuntimeMaterial,
                            runtimeMaterial);
                    batchLookup.Add(
                        turretKey,
                        turretBatch);
                    batches.Add(
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
                        new InstanceBatch(
                            true,
                            stateMesh,
                            usesRuntimeMaterial,
                            runtimeMaterial);
                    batchLookup.Add(
                        stateKey,
                        stateBatch);
                    batches.Add(
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

        for (int batchIndex = 0;
             batchIndex < batches.Count;
             batchIndex++)
        {
            submittedInstances =
                checked(
                    submittedInstances +
                    batches[batchIndex].Instances.Count);
        }

        IGraphicsBuffer instanceBuffer =
            GetFrameInstanceBuffer(
                context.FrameIndex,
                submittedInstances);
        var instanceData =
            new InstanceRenderData[submittedInstances];
        int writeOffset = 0;

        for (int batchIndex = 0;
             batchIndex < batches.Count;
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
            instanceData);

        Span<float> constants =
            stackalloc float[RootConstantCount];
        WriteMatrix(
            matrices.ViewProjection,
            constants);

        int draws = 0;

        for (int batchIndex = 0;
             batchIndex < batches.Count;
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
                reducedLod);
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
        _runtimeAssets?.Dispose();
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _texturedPipeline?.Dispose();
        _pipeline.Dispose();
        _disposed = true;
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
            };

            struct VertexInput
            {
                float3 Position : POSITION;
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
                output.Position =
                    mul(
                        worldPosition,
                        ViewProjection);
                output.Color =
                    input.Color;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                return input.Color;
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
                    saturate(orm.r);
                float roughness =
                    saturate(
                        orm.g *
                        input.Material0.z);
                float metallic =
                    saturate(
                        orm.b *
                        input.Material0.w);
                float normalFacing =
                    saturate(
                        dot(
                            worldNormal,
                            normalize(
                                float3(
                                    0.35f,
                                    0.85f,
                                    0.4f))));

                float materialResponse =
                    lerp(
                        0.92f,
                        1.0f,
                        ambientOcclusion);
                materialResponse *=
                    lerp(
                        0.96f,
                        1.04f,
                        normalFacing);
                materialResponse *=
                    lerp(
                        1.0f,
                        0.98f,
                        roughness);
                materialResponse *=
                    lerp(
                        1.0f,
                        0.99f,
                        metallic);

                float3 rgb =
                    baseColor.rgb *
                    input.Color.rgb *
                    materialResponse +
                    emissive *
                    input.Material1.x;

                return float4(
                    rgb,
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

    private sealed class InstanceBatch(
        bool usesRuntimeMesh,
        RuntimeMeshBuffers runtimeMesh,
        bool usesRuntimeMaterial,
        RuntimeMaterialResources runtimeMaterial)
    {
        public bool UsesRuntimeMesh { get; } =
            usesRuntimeMesh;

        public RuntimeMeshBuffers RuntimeMesh { get; } =
            runtimeMesh;

        public bool UsesRuntimeMaterial { get; } =
            usesRuntimeMaterial;

        public RuntimeMaterialResources RuntimeMaterial { get; } =
            runtimeMaterial;

        public List<InstanceRenderData> Instances { get; } =
            [];

        public int InstanceOffset { get; set; }
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
