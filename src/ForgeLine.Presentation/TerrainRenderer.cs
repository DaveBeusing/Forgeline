using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class TerrainRenderer : IDisposable
{
    private const int TerrainRootConstantCount = 20;

    private readonly IGraphicsPipeline _pipeline;
    private readonly TerrainChunkRenderResource[] _resources;

    private bool _disposed;

    public TerrainRenderer(
        IGraphicsDevice graphics,
        TerrainWorld world,
        TerrainMeshSettings? meshSettings = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(world);

        TerrainMeshSettings resolvedMeshSettings =
            meshSettings ?? new TerrainMeshSettings();
        resolvedMeshSettings.Validate();

        _pipeline = CreateTerrainPipeline(graphics);
        var resources = new List<TerrainChunkRenderResource>(
            world.Chunks.Count);

        try
        {
            foreach (TerrainChunk chunk in world.Chunks)
            {
                TerrainMeshData mesh =
                    TerrainMeshGenerator.Generate(chunk, resolvedMeshSettings);

                ulong vertexBytes = checked(
                    (ulong)mesh.Vertices.Length *
                    TerrainVertex.SizeInBytes);
                ulong indexBytes = checked(
                    (ulong)mesh.Indices.Length *
                    sizeof(uint));

                IGraphicsBuffer vertexBuffer = graphics.CreateBuffer(
                    new GraphicsBufferDescription(
                        vertexBytes,
                        GraphicsBufferMemory.Upload));
                IGraphicsBuffer indexBuffer = graphics.CreateBuffer(
                    new GraphicsBufferDescription(
                        indexBytes,
                        GraphicsBufferMemory.Upload));

                try
                {
                    vertexBuffer.SetData<TerrainVertex>(mesh.Vertices);
                    indexBuffer.SetData<uint>(mesh.Indices);

                    resources.Add(
                        new TerrainChunkRenderResource(
                            chunk.Coordinate,
                            mesh.Bounds,
                            vertexBuffer,
                            indexBuffer,
                            mesh.Indices.Length,
                            mesh.TriangleCount));
                }
                catch
                {
                    indexBuffer.Dispose();
                    vertexBuffer.Dispose();
                    throw;
                }
            }

            _resources = resources.ToArray();
        }
        catch
        {
            foreach (TerrainChunkRenderResource resource in resources)
            {
                resource.Dispose();
            }

            _pipeline.Dispose();
            throw;
        }

        LastDiagnostics = new TerrainRenderDiagnostics(
            _resources.Length,
            0,
            _resources.Length,
            0,
            0,
            checked(_resources.Length * 2));
    }

    public bool DebugChunksEnabled { get; set; } = true;

    public TerrainRenderDiagnostics LastDiagnostics { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);

        CameraMatrices matrices = camera.GetMatrices(
            context.Width,
            context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(matrices.ViewProjection);

        int visibleChunks = 0;
        int drawCalls = 0;
        long submittedTriangles = 0;

        context.SetPipeline(_pipeline);

        Span<float> constants = stackalloc float[TerrainRootConstantCount];
        WriteMatrix(matrices.ViewProjection, constants);

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (!frustum.Intersects(resource.Bounds))
            {
                continue;
            }

            visibleChunks++;
            submittedTriangles += resource.TriangleCount;

            constants[16] = resource.Coordinate.X;
            constants[17] = resource.Coordinate.Z;
            constants[18] = DebugChunksEnabled ? 1.0f : 0.0f;
            constants[19] = 0.0f;

            context.SetVertexConstants(constants);
            context.SetVertexBuffer(
                resource.VertexBuffer,
                TerrainVertex.SizeInBytes);
            context.SetIndexBuffer(
                resource.IndexBuffer,
                GraphicsIndexFormat.UInt32);
            context.DrawIndexed(resource.IndexCount);
            drawCalls++;
        }

        LastDiagnostics = new TerrainRenderDiagnostics(
            _resources.Length,
            visibleChunks,
            _resources.Length - visibleChunks,
            submittedTriangles,
            drawCalls,
            checked(_resources.Length * 2));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            resource.Dispose();
        }

        _pipeline.Dispose();
        _disposed = true;
    }

    private static IGraphicsPipeline CreateTerrainPipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer TerrainFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float2 ChunkCoordinate;
                float DebugChunks;
                float Padding;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float2 LocalUv : TEXCOORD0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float3 WorldPosition : TEXCOORD0;
                float3 Normal : TEXCOORD1;
                float2 LocalUv : TEXCOORD2;
                float2 ChunkCoordinate : TEXCOORD3;
                float DebugChunks : TEXCOORD4;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position =
                    mul(float4(input.Position, 1.0f), ViewProjection);
                output.WorldPosition = input.Position;
                output.Normal = input.Normal;
                output.LocalUv = input.LocalUv;
                output.ChunkCoordinate = ChunkCoordinate;
                output.DebugChunks = DebugChunks;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float3 WorldPosition : TEXCOORD0;
                float3 Normal : TEXCOORD1;
                float2 LocalUv : TEXCOORD2;
                float2 ChunkCoordinate : TEXCOORD3;
                float DebugChunks : TEXCOORD4;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float3 normal = normalize(input.Normal);
                float slope = saturate(1.0f - normal.y);
                float elevation = saturate((input.WorldPosition.y + 30.0f) / 70.0f);

                float3 low = float3(0.16f, 0.28f, 0.13f);
                float3 high = float3(0.38f, 0.34f, 0.22f);
                float3 rock = float3(0.34f, 0.35f, 0.33f);
                float3 color = lerp(low, high, elevation);
                color = lerp(color, rock, saturate(slope * 1.8f));

                float light =
                    0.45f +
                    0.55f *
                    saturate(
                        dot(
                            normal,
                            normalize(float3(0.35f, 0.85f, -0.25f))));
                color *= light;

                if (input.DebugChunks > 0.5f)
                {
                    float edgeDistance = min(
                        min(input.LocalUv.x, 1.0f - input.LocalUv.x),
                        min(input.LocalUv.y, 1.0f - input.LocalUv.y));

                    if (edgeDistance < 0.0125f)
                    {
                        return float4(1.0f, 0.55f, 0.08f, 1.0f);
                    }

                    float coordinateParity = fmod(
                        abs(input.ChunkCoordinate.x) +
                        abs(input.ChunkCoordinate.y),
                        2.0f);
                    color *= lerp(0.92f, 1.08f, coordinateParity);
                }

                return float4(color, 1.0f);
            }
            """;

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader = compiler.Compile(
            vertexShaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "TerrainVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "TerrainPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(vertexShader, pixelShader)
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
                        24)
                ],
                VertexRootConstantCount = TerrainRootConstantCount,
                DepthEnabled = true
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class TerrainChunkRenderResource : IDisposable
    {
        internal TerrainChunkRenderResource(
            ChunkCoordinate coordinate,
            AxisAlignedBounds bounds,
            IGraphicsBuffer vertexBuffer,
            IGraphicsBuffer indexBuffer,
            int indexCount,
            int triangleCount)
        {
            Coordinate = coordinate;
            Bounds = bounds;
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            IndexCount = indexCount;
            TriangleCount = triangleCount;
        }

        internal ChunkCoordinate Coordinate { get; }

        internal AxisAlignedBounds Bounds { get; }

        internal IGraphicsBuffer VertexBuffer { get; }

        internal IGraphicsBuffer IndexBuffer { get; }

        internal int IndexCount { get; }

        internal int TriangleCount { get; }

        public void Dispose()
        {
            IndexBuffer.Dispose();
            VertexBuffer.Dispose();
        }
    }
}
