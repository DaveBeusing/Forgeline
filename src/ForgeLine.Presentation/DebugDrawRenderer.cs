using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed class DebugDrawRenderer : IDisposable
{
    private const int MaxLines = 32_768;
    private const int VertexStride = 28;
    private const int RootConstantCount = 16;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private readonly DebugVertex[] _vertices =
        new DebugVertex[MaxLines * 2];
    private bool _disposed;

    public DebugDrawRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
        _pipeline = CreatePipeline(graphics);
    }

    public DebugDrawRenderDiagnostics LastDiagnostics { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera,
        DebugDraw debugDraw)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(debugDraw);

        ReadOnlySpan<DebugLine> lines = debugDraw.Lines;
        if (!debugDraw.Enabled || lines.IsEmpty)
        {
            LastDiagnostics = default;
            return;
        }

        int renderedLines = Math.Min(lines.Length, MaxLines);
        int vertexCount = renderedLines * 2;

        for (int index = 0; index < renderedLines; index++)
        {
            DebugLine line = lines[index];
            int vertexIndex = index * 2;
            _vertices[vertexIndex] =
                new DebugVertex(line.Start, line.Color);
            _vertices[vertexIndex + 1] =
                new DebugVertex(line.End, line.Color);
        }

        IGraphicsBuffer vertexBuffer = GetFrameVertexBuffer(context.FrameIndex);
        vertexBuffer.SetData<DebugVertex>(
            _vertices.AsSpan(0, vertexCount));

        CameraMatrices matrices =
            camera.GetMatrices(context.Width, context.Height);

        Span<float> constants =
            stackalloc float[RootConstantCount];
        WriteMatrix(matrices.ViewProjection, constants);

        context.SetPipeline(_pipeline);
        context.SetVertexConstants(constants);
        context.SetVertexBuffer(vertexBuffer, VertexStride);
        context.Draw(vertexCount);

        LastDiagnostics = new DebugDrawRenderDiagnostics(
            lines.Length,
            renderedLines,
            lines.Length - renderedLines,
            1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IGraphicsBuffer vertexBuffer in _vertexBuffers.Values)
        {
            vertexBuffer.Dispose();
        }

        _vertexBuffers.Clear();
        _pipeline.Dispose();
        _disposed = true;
    }

    private IGraphicsBuffer GetFrameVertexBuffer(int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(frameIndex, out IGraphicsBuffer? buffer))
        {
            return buffer;
        }

        buffer = _graphics.CreateBuffer(
            new GraphicsBufferDescription(
                checked((ulong)_vertices.Length * VertexStride),
                GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(frameIndex, buffer);
        return buffer;
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer DebugFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float4 Color : COLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position =
                    mul(float4(input.Position, 1.0f), ViewProjection);
                output.Color = input.Color;
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
            "DebugLineVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "DebugLinePixel.hlsl");

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
                        "COLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        12)
                ],
                VertexRootConstantCount = RootConstantCount,
                PrimitiveTopology = GraphicsPrimitiveTopology.LineList,
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DebugVertex(
        Vector3 Position,
        Vector4 Color);
}
