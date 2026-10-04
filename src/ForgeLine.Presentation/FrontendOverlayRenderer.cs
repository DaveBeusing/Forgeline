using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed class FrontendOverlayRenderer : IDisposable
{
    private const int MaxVertices = 16_384;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 3.0f;
    private const float GlyphAdvance = 18.0f;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private readonly OverlayVertex[] _vertices = new OverlayVertex[MaxVertices];
    private int _vertexCount;
    private bool _disposed;

    public FrontendOverlayRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _graphics = graphics;
        _pipeline = CreatePipeline(graphics);
    }

    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        in FrontendSurfaceView view,
        float userScale = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        _vertexCount = 0;
        float viewportScale = MathF.Min(
            context.Width / 1920f,
            context.Height / 1080f);
        float scale = Math.Clamp(
            viewportScale * userScale,
            0.75f,
            2.0f);

        EmitPanel(
            new SurfaceRect(0, 0, 1920f, 1080f).Scale(scale),
            new Vector4(0.035f, 0.045f, 0.045f, 0.98f),
            context.Width,
            context.Height);

        EmitText("FORGELINE", 92 * scale, 82 * scale, new Vector4(0.82f, 0.84f, 0.78f, 1), context.Width, context.Height, scale);
        EmitText("BUILD. SUPPLY. CONQUER.", 94 * scale, 128 * scale, new Vector4(0.52f, 0.58f, 0.50f, 1), context.Width, context.Height, scale);

        if (view.Kind == FrontendSurfaceKind.Loading)
        {
            EmitText("INITIALIZING COMMAND SYSTEMS", 94 * scale, 850 * scale, new Vector4(0.72f, 0.74f, 0.68f, 1), context.Width, context.Height, scale);
            EmitText(view.Status, 94 * scale, 890 * scale, new Vector4(0.82f, 0.62f, 0.20f, 1), context.Width, context.Height, scale);
            if (view.HasProgress)
            {
                EmitProgress(94 * scale, 936 * scale, 720 * scale, 8 * scale, view.Progress, context.Width, context.Height);
            }
        }
        else if (view.Kind == FrontendSurfaceKind.MainMenu)
        {
            float y = 350 * scale;
            foreach (FrontendMenuEntryView item in view.MenuEntries)
            {
                bool focused = item.IsFocused;
                Vector4 textColor = item.IsEnabled
                    ? focused ? new Vector4(0.95f, 0.72f, 0.22f, 1) : new Vector4(0.76f, 0.78f, 0.72f, 1)
                    : new Vector4(0.32f, 0.34f, 0.32f, 1);
                if (focused)
                {
                    EmitPanel(new SurfaceRect(82 * scale, (y - 12 * scale), 560 * scale, 52 * scale), new Vector4(0.12f, 0.14f, 0.13f, 0.95f), context.Width, context.Height);
                    EmitPanel(new SurfaceRect(82 * scale, (y - 12 * scale), 5 * scale, 52 * scale), new Vector4(0.86f, 0.61f, 0.16f, 1), context.Width, context.Height);
                }
                EmitText(item.Label, 104 * scale, y, textColor, context.Width, context.Height, scale);
                y += 72 * scale;
            }
            EmitText("ENTER  SELECT     UP/DOWN  NAVIGATE", 94 * scale, 930 * scale, new Vector4(0.40f, 0.44f, 0.40f, 1), context.Width, context.Height, scale);
        }

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        IGraphicsBuffer buffer = GetFrameVertexBuffer(context.FrameIndex);
        buffer.SetData<OverlayVertex>(_vertices.AsSpan(0, _vertexCount));
        context.SetPipeline(_pipeline);
        context.SetVertexBuffer(buffer, VertexStride);
        context.Draw(_vertexCount);
        LastRenderedVertexCount = _vertexCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (IGraphicsBuffer buffer in _vertexBuffers.Values) buffer.Dispose();
        _vertexBuffers.Clear();
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitProgress(float x, float y, float width, float height, float progress, int viewportWidth, int viewportHeight)
    {
        EmitPanel(new SurfaceRect(x, y, width, height), new Vector4(0.12f, 0.14f, 0.13f, 1), viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width * Math.Clamp(progress, 0, 1), height), new Vector4(0.82f, 0.58f, 0.14f, 1), viewportWidth, viewportHeight);
    }

    private void EmitPanel(SurfaceRect rect, Vector4 color, int width, int height)
    {
        float left = rect.X / width * 2 - 1;
        float right = (rect.X + rect.Width) / width * 2 - 1;
        float top = 1 - rect.Y / height * 2;
        float bottom = 1 - (rect.Y + rect.Height) / height * 2;
        EmitTriangle(left, top, right, top, right, bottom, color);
        EmitTriangle(left, top, right, bottom, left, bottom, color);
    }

    private void EmitText(string text, float x, float y, Vector4 color, int width, int height, float scale)
    {
        float cursor = x;
        foreach (char character in text.ToUpperInvariant())
        {
            if (character == ' ') { cursor += GlyphAdvance * scale; continue; }
            ReadOnlySpan<byte> rows = GlyphRows(character);
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 5; column++)
                if ((rows[row] & (1 << (4 - column))) != 0)
                    EmitPanel(new SurfaceRect(cursor + column * GlyphPixelSize * scale, y + row * GlyphPixelSize * scale, GlyphPixelSize * scale, GlyphPixelSize * scale), color, width, height);
            cursor += GlyphAdvance * scale;
        }
    }

    private void EmitTriangle(float ax,float ay,float bx,float by,float cx,float cy,Vector4 color)
    {
        if (_vertexCount + 3 > MaxVertices) return;
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(ax,ay),color);
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(bx,by),color);
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(cx,cy),color);
    }

    private IGraphicsBuffer GetFrameVertexBuffer(int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(frameIndex, out IGraphicsBuffer? buffer)) return buffer;
        buffer = _graphics.CreateBuffer(new GraphicsBufferDescription((ulong)(MaxVertices * VertexStride), GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(frameIndex, buffer);
        return buffer;
    }

    private static ReadOnlySpan<byte> GlyphRows(char c) => c switch
    {
        'A' => [14,17,17,31,17,17,17], 'B' => [30,17,17,30,17,17,30], 'C' => [14,17,16,16,16,17,14],
        'D' => [30,17,17,17,17,17,30], 'E' => [31,16,16,30,16,16,31], 'F' => [31,16,16,30,16,16,16],
        'G' => [14,17,16,23,17,17,15], 'H' => [17,17,17,31,17,17,17], 'I' => [31,4,4,4,4,4,31], 'L' => [16,16,16,16,16,16,31],
        'M' => [17,27,21,21,17,17,17], 'N' => [17,25,21,19,17,17,17], 'O' => [14,17,17,17,17,17,14],
        'P' => [30,17,17,30,16,16,16], 'R' => [30,17,17,30,20,18,17], 'S' => [15,16,16,14,1,1,30],
        'T' => [31,4,4,4,4,4,4], 'U' => [17,17,17,17,17,17,14], 'V' => [17,17,17,17,17,10,4],
        'W' => [17,17,17,21,21,21,10], 'X' => [17,17,10,4,10,17,17], 'Y' => [17,17,10,4,4,4,4],
        '0' => [14,17,19,21,25,17,14], '1' => [4,12,4,4,4,4,14], '2' => [14,17,1,2,4,8,31],
        '3' => [30,1,1,14,1,1,30], '4' => [2,6,10,18,31,2,2], '5' => [31,16,16,30,1,1,30],
        '6' => [14,16,16,30,17,17,14], '7' => [31,1,2,4,8,8,8], '8' => [14,17,17,14,17,17,14],
        '9' => [14,17,17,15,1,1,14], '.' => [0,0,0,0,0,12,12], ':' => [0,12,12,0,12,12,0],
        '/' => [1,2,4,8,16,0,0], '-' => [0,0,0,31,0,0,0], _ => [0,0,0,0,0,0,0]
    };

    private static IGraphicsPipeline CreatePipeline(IGraphicsDevice graphics)
    {
        const string vs = "struct V{float2 P:POSITION;float4 C:COLOR0;}; struct O{float4 P:SV_Position;float4 C:COLOR0;}; O VSMain(V v){O o;o.P=float4(v.P,0,1);o.C=v.C;return o;}";
        const string ps = "struct I{float4 P:SV_Position;float4 C:COLOR0;}; float4 PSMain(I i):SV_Target0{return i.C;}";
        var compiler = new DxcShaderCompiler();
        return graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            compiler.Compile(vs,GraphicsShaderStage.Vertex,"VSMain","FrontendVertex.hlsl"),
            compiler.Compile(ps,GraphicsShaderStage.Pixel,"PSMain","FrontendPixel.hlsl"))
        {
            VertexElements=[new GraphicsVertexElement("POSITION",0,GraphicsVertexElementFormat.Float2,0),new GraphicsVertexElement("COLOR",0,GraphicsVertexElementFormat.Float4,8)],
            PrimitiveTopology=GraphicsPrimitiveTopology.TriangleList,
            DepthEnabled=false
        });
    }

    private readonly record struct SurfaceRect(float X, float Y, float Width, float Height)
    {
        public SurfaceRect Scale(float scale) =>
            new(X * scale, Y * scale, Width * scale, Height * scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(Vector2 Position, Vector4 Color);
}
