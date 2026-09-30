using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class SimpleInstanceRenderer : IDisposable
{
    private const int RootConstantCount = 16;
    private const int VertexStride = 12;
    private const int InstanceStride = 80;
    private const int MinimumInstanceCapacity = 64;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
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

        SimpleVertex[] vertices =
        [
            new(-0.5f, -0.5f, -0.5f),
            new( 0.5f, -0.5f, -0.5f),
            new( 0.5f,  0.5f, -0.5f),
            new(-0.5f,  0.5f, -0.5f),
            new(-0.5f, -0.5f,  0.5f),
            new( 0.5f, -0.5f,  0.5f),
            new( 0.5f,  0.5f,  0.5f),
            new(-0.5f,  0.5f,  0.5f)
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
                checked((ulong)vertices.Length * VertexStride),
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
            float distance =
                Vector3.Distance(
                    camera.Position,
                    instance.Transform.Position);

            if (instance.UnitFeature.IsSpecified)
            {
                UnitAssetLod unitLod =
                    UnitPresentationCatalog.SelectLod(
                        instance.UnitFeature,
                        distance);
                UnitPresentationDefinition definition =
                    UnitPresentationCatalog.Get(
                        instance.UnitFeature.Unit);
                string meshAssetId =
                    instance.UnitFeature.IsWreck
                        ? definition.Lod2AssetId
                        : definition.GetMeshAssetId(
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
            else if (instance.WorldFeature.IsSpecified)
            {
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
            var key =
                new InstanceBatchKey(
                    usesRuntimeMesh,
                    runtimeMeshId);

            if (!batchLookup.TryGetValue(
                    key,
                    out InstanceBatch? batch))
            {
                batch =
                    new InstanceBatch(
                        usesRuntimeMesh,
                        runtimeMesh);
                batchLookup.Add(
                    key,
                    batch);
                batches.Add(
                    batch);
            }

            batch.Instances.Add(
                new InstanceRenderData(
                    instance.Transform.ToMatrix(),
                    ResolveColor(
                        instance)));
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

        IGraphicsBuffer instanceBuffer =
            GetFrameInstanceBuffer(
                context.FrameIndex,
                visible);
        var instanceData =
            new InstanceRenderData[visible];
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

        context.SetPipeline(
            _pipeline);

        Span<float> constants =
            stackalloc float[RootConstantCount];
        WriteMatrix(
            matrices.ViewProjection,
            constants);
        context.SetVertexConstants(
            constants);

        int draws = 0;

        for (int batchIndex = 0;
             batchIndex < batches.Count;
             batchIndex++)
        {
            InstanceBatch batch =
                batches[batchIndex];

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
                    VertexStride);
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

    private Vector4 ResolveColor(
        in RenderInstance instance)
    {
        if (instance.UnitFeature.IsSpecified)
        {
            UnitPresentationDefinition definition =
                UnitPresentationCatalog.Get(
                    instance.UnitFeature.Unit);
            Vector4 baseTint =
                _runtimeAssets is not null &&
                _runtimeAssets.TryGetMaterialTint(
                    definition.MaterialAssetId,
                    out Vector4 runtimeTint)
                    ? runtimeTint
                    : definition.FallbackTint;

            return UnitPresentationCatalog.ApplyDamageTint(
                instance.UnitFeature,
                baseTint);
        }

        if (instance.WorldFeature.IsSpecified)
        {
            return
                _runtimeAssets is not null &&
                _runtimeAssets.TryGetMaterialTint(
                    instance.WorldFeature,
                    out Vector4 runtimeTint)
                    ? runtimeTint
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

    private readonly record struct InstanceBatchKey(
        bool UsesRuntimeMesh,
        AssetId RuntimeMeshId);

    private sealed class InstanceBatch(
        bool usesRuntimeMesh,
        RuntimeMeshBuffers runtimeMesh)
    {
        public bool UsesRuntimeMesh { get; } =
            usesRuntimeMesh;

        public RuntimeMeshBuffers RuntimeMesh { get; } =
            runtimeMesh;

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
        Vector4 Color);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SimpleVertex(
        float X,
        float Y,
        float Z);
}
