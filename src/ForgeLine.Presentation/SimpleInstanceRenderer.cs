using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class SimpleInstanceRenderer : IDisposable
{
    private const int RootConstantCount = 36;
    private const int VertexStride = 12;

    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsBuffer _vertexBuffer;
    private readonly IGraphicsBuffer _indexBuffer;
    private bool _disposed;

    public SimpleInstanceRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _pipeline = CreatePipeline(graphics);

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
            _vertexBuffer.SetData<SimpleVertex>(vertices);
            _indexBuffer.SetData<ushort>(indices);
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

        CameraMatrices matrices = camera.GetMatrices(
            context.Width,
            context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(matrices.ViewProjection);

        context.SetPipeline(_pipeline);
        context.SetVertexBuffer(_vertexBuffer, VertexStride);
        context.SetIndexBuffer(
            _indexBuffer,
            GraphicsIndexFormat.SixteenBit);

        Span<float> constants = stackalloc float[RootConstantCount];
        WriteMatrix(matrices.ViewProjection, constants[..16]);

        int visible = 0;
        int draws = 0;

        for (int index = 0; index < world.InstanceCount; index++)
        {
            RenderInstance instance =
                world.GetInterpolatedInstance(index, alpha);

            if ((instance.Visibility & RenderVisibilityFlags.World) == 0 ||
                !instance.Mesh.IsValid ||
                !instance.Material.IsValid)
            {
                continue;
            }

            Vector3 extents = Vector3.Max(
                Vector3.Abs(instance.Transform.Scale) * 0.5f,
                new Vector3(0.05f));
            var bounds = new AxisAlignedBounds(
                instance.Transform.Position - extents,
                instance.Transform.Position + extents);

            if (!frustum.Intersects(bounds))
            {
                continue;
            }

            visible++;

            Matrix4x4 worldMatrix = instance.Transform.ToMatrix();
            WriteMatrix(worldMatrix, constants.Slice(16, 16));
            WriteColor(instance.DebugIdentity, constants.Slice(32, 4));

            context.SetVertexConstants(constants);
            context.DrawIndexed(36);
            draws++;
        }

        LastDiagnostics = new InstanceRenderDiagnostics(
            world.InstanceCount,
            visible,
            world.InstanceCount - visible,
            draws);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer InstanceFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                row_major float4x4 World;
                float4 Color;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                float4 worldPosition =
                    mul(float4(input.Position, 1.0f), World);
                output.Position =
                    mul(worldPosition, ViewProjection);
                output.Color = Color;
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

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader = compiler.Compile(
            vertexShaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "InstanceVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "InstancePixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(vertexShader, pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        0)
                ],
                VertexRootConstantCount = RootConstantCount,
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

    private static void WriteColor(
        uint identity,
        Span<float> destination)
    {
        uint hash = identity * 2_654_435_761U;
        destination[0] = 0.35f + ((hash & 0xFFU) / 255.0f) * 0.45f;
        destination[1] = 0.45f + (((hash >> 8) & 0xFFU) / 255.0f) * 0.35f;
        destination[2] = 0.15f + (((hash >> 16) & 0xFFU) / 255.0f) * 0.35f;
        destination[3] = 1.0f;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SimpleVertex(
        float X,
        float Y,
        float Z);
}
