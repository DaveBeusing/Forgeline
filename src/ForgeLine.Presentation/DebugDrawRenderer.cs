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
    private readonly DebugVertex[] _vertices;
    private bool _disposed;
    private readonly ScreenLineVertex[]? _screenVertices;
    private readonly float _lineWidthPixels;

    public DebugDrawRenderer(
        IGraphicsDevice graphics,
        bool depthEnabled = true,
        float lineWidthPixels = 0.0f)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
        if (!float.IsFinite(lineWidthPixels) || lineWidthPixels < 0.0f || lineWidthPixels > 8.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(lineWidthPixels));
        }
        _lineWidthPixels = lineWidthPixels;
        _vertices = lineWidthPixels > 0.0f ? [] : new DebugVertex[MaxLines * 2];
        if (lineWidthPixels > 0.0f)
        {
            _screenVertices = new ScreenLineVertex[MaxLines * 6];
        }
        _pipeline =
            _screenVertices is not null ? CreateScreenLinePipeline(graphics, depthEnabled) : CreatePipeline(
                graphics,
                depthEnabled);
    }

    public DebugDrawRenderDiagnostics LastDiagnostics { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera,
        DebugDraw debugDraw,
        float uiScale = 1.0f)
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
        if (_screenVertices is not null)
        {
            RenderScreenLines(context, camera, lines, renderedLines, uiScale);
            return;
        }
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
                _screenVertices is null
                    ? checked((ulong)_vertices.Length * VertexStride)
                    : checked((ulong)_screenVertices.Length * 40),
                GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(frameIndex, buffer);
        return buffer;
    }

    private void RenderScreenLines(
        IGraphicsCommandContext context, RtsCamera camera, ReadOnlySpan<DebugLine> lines, int count, float uiScale)
    {
        if (context.Width <= 0 || context.Height <= 0)
        {
            LastDiagnostics = default;
            return;
        }
        Matrix4x4 viewProjection = camera.GetMatrices(context.Width, context.Height).ViewProjection;
        float halfWidth = _lineWidthPixels * (float.IsFinite(uiScale) ? Math.Clamp(uiScale, 0.5f, 4.0f) : 1.0f) * 0.5f;
        int vertexCount = 0;
        for (int index = 0; index < count; index++)
        {
            DebugLine line = lines[index];
            Vector4 first = Vector4.Transform(new Vector4(line.Start, 1.0f), viewProjection);
            Vector4 second = Vector4.Transform(new Vector4(line.End, 1.0f), viewProjection);
            if (first.W <= 1e-5f || second.W <= 1e-5f || first.Z < 0.0f || second.Z < 0.0f)
            {
                continue;
            }
            Vector2 delta = new((second.X / second.W - first.X / first.W) * context.Width,
                (second.Y / second.W - first.Y / first.W) * context.Height);
            if (!float.IsFinite(delta.LengthSquared()) || delta.LengthSquared() <= 1e-8f)
            {
                continue;
            }
            Vector2 perpendicular = Vector2.Normalize(new Vector2(-delta.Y, delta.X));
            Vector4 offset = new(perpendicular.X * halfWidth * 2.0f / context.Width,
                perpendicular.Y * halfWidth * 2.0f / context.Height, 0.0f, 0.0f);
            Vector4 firstOffset = offset * first.W;
            Vector4 secondOffset = offset * second.W;
            _screenVertices![vertexCount++] = new(first - firstOffset, line.Color, new Vector2(-1.0f, 0.0f));
            _screenVertices[vertexCount++] = new(first + firstOffset, line.Color, new Vector2(1.0f, 0.0f));
            _screenVertices[vertexCount++] = new(second + secondOffset, line.Color, new Vector2(1.0f, 0.0f));
            _screenVertices[vertexCount++] = new(first - firstOffset, line.Color, new Vector2(-1.0f, 0.0f));
            _screenVertices[vertexCount++] = new(second + secondOffset, line.Color, new Vector2(1.0f, 0.0f));
            _screenVertices[vertexCount++] = new(second - secondOffset, line.Color, new Vector2(-1.0f, 0.0f));
        }
        if (vertexCount > 0)
        {
            IGraphicsBuffer buffer = GetFrameVertexBuffer(context.FrameIndex);
            buffer.SetData<ScreenLineVertex>(_screenVertices.AsSpan(0, vertexCount));
            context.SetPipeline(_pipeline);
            context.SetVertexBuffer(buffer, 40);
            context.Draw(vertexCount);
        }
        LastDiagnostics = new(lines.Length, vertexCount / 6, lines.Length - vertexCount / 6, vertexCount > 0 ? 1 : 0);
    }

    private static IGraphicsPipeline CreateScreenLinePipeline(IGraphicsDevice graphics, bool depthEnabled)
    {
        const string source = """
            struct VertexInput { float4 Position : POSITION; float4 Color : COLOR0; float2 Edge : TEXCOORD0; };
            struct VertexOutput { float4 Position : SV_Position; float4 Color : COLOR0; noperspective float Edge : TEXCOORD0; };
            VertexOutput VSMain(VertexInput input) {
                VertexOutput output;
                output.Position = input.Position; output.Color = input.Color; output.Edge = input.Edge.x;
                return output;
            }
            float4 PSMain(VertexOutput input) : SV_Target0 {
                return float4(input.Color.rgb, input.Color.a * (1.0f - smoothstep(0.45f, 1.0f, abs(input.Edge))));
            }
            """;
        var compiler = new DxcShaderCompiler();
        return graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            compiler.Compile(source, GraphicsShaderStage.Vertex, "VSMain", "PlayerWorldLineVertex.hlsl"),
            compiler.Compile(source, GraphicsShaderStage.Pixel, "PSMain", "PlayerWorldLinePixel.hlsl"))
        {
            VertexElements =
            [
                new("POSITION", 0, GraphicsVertexElementFormat.Float4, 0),
                new("COLOR", 0, GraphicsVertexElementFormat.Float4, 16),
                new("TEXCOORD", 0, GraphicsVertexElementFormat.Float2, 32)
            ],
            CullMode = GraphicsCullMode.None,
            AlphaBlendEnabled = true,
            DepthEnabled = depthEnabled
        });
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics,
        bool depthEnabled)
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
                DepthEnabled = depthEnabled
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ScreenLineVertex(Vector4 Position, Vector4 Color, Vector2 Edge);
}
