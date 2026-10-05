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
    private float _offsetX;
    private float _offsetY;
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
        _offsetX = 0f;
        _offsetY = 0f;
        float viewportScale = MathF.Min(
            context.Width / 1920f,
            context.Height / 1080f);
        float scale = Math.Min(
            viewportScale * userScale,
            2.0f);
        float offsetX =
            MathF.Max(
                0f,
                (context.Width - 1920f * scale) * 0.5f);
        float offsetY =
            MathF.Max(
                0f,
                (context.Height - 1080f * scale) * 0.5f);

        _offsetX = offsetX;
        _offsetY = offsetY;

        float transition = Math.Clamp(view.Transition, 0f, 1f);
        float contentOffset = (1f - transition) * 18f * scale;

        SurfaceRect backdrop =
            view.Kind == FrontendSurfaceKind.PauseMenu
                ? new SurfaceRect(
                    64f,
                    54f,
                    1040f,
                    972f)
                : new SurfaceRect(
                    0f,
                    0f,
                    1920f,
                    1080f);

        EmitPanel(
            backdrop.Scale(scale),
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
                int percent =
                    Math.Clamp(
                        (int)MathF.Round(
                            view.Progress * 100f),
                        0,
                        100);
                EmitText(
                    $"{percent}%",
                    828 * scale,
                    926 * scale,
                    new Vector4(0.82f, 0.84f, 0.78f, 1),
                    context.Width,
                    context.Height,
                    scale);
                EmitProgress(
                    94 * scale,
                    936 * scale,
                    720 * scale,
                    8 * scale,
                    view.Progress,
                    context.Width,
                    context.Height);
            }
        }
        else if (view.Kind is
                     FrontendSurfaceKind.MainMenu or
                     FrontendSurfaceKind.PauseMenu)
        {
            if (view.Kind == FrontendSurfaceKind.PauseMenu)
            {
                EmitText(
                    view.Title,
                    94 * scale + contentOffset,
                    286 * scale,
                    new Vector4(0.95f, 0.72f, 0.22f, 1),
                    context.Width,
                    context.Height,
                    scale);
                EmitPanel(
                    new SurfaceRect(
                        92 * scale,
                        330 * scale,
                        980 * scale,
                        2 * scale),
                    new Vector4(0.28f, 0.32f, 0.29f, 1),
                    context.Width,
                    context.Height);
            }

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
            if (!string.IsNullOrEmpty(view.Feedback))
            {
                EmitText(view.Feedback, 94 * scale, 790 * scale, new Vector4(0.82f, 0.42f, 0.20f, 1), context.Width, context.Height, scale);
            }

            EmitText(view.Footer, 94 * scale, 930 * scale, new Vector4(0.40f, 0.44f, 0.40f, 1), context.Width, context.Height, scale);

            if (view.Kind == FrontendSurfaceKind.MainMenu &&
                !string.IsNullOrWhiteSpace(view.ProductVersion))
            {
                string versionLabel =
                    $"VERSION {view.ProductVersion}";
                float versionX =
                    (1920f - 48f -
                     versionLabel.Length * GlyphAdvance) * scale;

                EmitText(
                    versionLabel,
                    versionX,
                    1024f * scale,
                    new Vector4(0.40f, 0.44f, 0.40f, 1),
                    context.Width,
                    context.Height,
                    scale);
            }
        }
        else if (view.Kind == FrontendSurfaceKind.Detail)
        {
            EmitText(view.Title, 94 * scale + contentOffset, 286 * scale, new Vector4(0.95f, 0.72f, 0.22f, 1), context.Width, context.Height, scale);
            EmitPanel(new SurfaceRect(92 * scale, 330 * scale, 980 * scale, 2 * scale), new Vector4(0.28f, 0.32f, 0.29f, 1), context.Width, context.Height);

            float y = 390 * scale;
            foreach (FrontendDetailLineView line in view.DetailLines)
            {
                if (line.IsFocused || line.IsHovered)
                {
                    Vector4 rowPanel = line.IsPressed
                        ? new Vector4(0.20f, 0.17f, 0.10f, 0.98f)
                        : line.IsHovered
                            ? new Vector4(0.14f, 0.16f, 0.14f, 0.97f)
                            : new Vector4(0.10f, 0.12f, 0.11f, 0.96f);
                    EmitPanel(new SurfaceRect(92 * scale, (y - 13 * scale), 980 * scale, 48 * scale), rowPanel, context.Width, context.Height);
                    EmitPanel(new SurfaceRect(92 * scale, (y - 13 * scale), 4 * scale, 48 * scale), new Vector4(0.86f, 0.61f, 0.16f, 1), context.Width, context.Height);
                }

                Vector4 valueColor = line.IsWarning
                    ? new Vector4(0.82f, 0.42f, 0.20f, 1)
                    : line.IsFocused
                        ? new Vector4(0.95f, 0.72f, 0.22f, 1)
                        : new Vector4(0.78f, 0.80f, 0.74f, 1);
                EmitText(line.Label, 112 * scale, y, new Vector4(0.45f, 0.50f, 0.45f, 1), context.Width, context.Height, scale);
                EmitText(line.Value, 460 * scale, y, valueColor, context.Width, context.Height, scale);

                if (line.CanDecrease)
                {
                    EmitControlButton("-", 830 * scale, (y - 13 * scale), 72 * scale, 42 * scale, context.Width, context.Height, scale);
                }

                if (line.CanIncrease)
                {
                    EmitControlButton("+", 916 * scale, (y - 13 * scale), 72 * scale, 42 * scale, context.Width, context.Height, scale);
                }

                y += 58 * scale;
            }

            if (!string.IsNullOrEmpty(view.PrimaryAction))
            {
                EmitActionButton(view.PrimaryAction, 782 * scale, 838 * scale, 290 * scale, 58 * scale, true, view.PrimaryHovered, view.PrimaryPressed, context.Width, context.Height, scale);
            }

            if (!string.IsNullOrEmpty(view.SecondaryAction))
            {
                EmitActionButton(view.SecondaryAction, 92 * scale, 838 * scale, 220 * scale, 58 * scale, false, view.SecondaryHovered, view.SecondaryPressed, context.Width, context.Height, scale);
            }

            EmitText(view.Footer, 94 * scale, 930 * scale, new Vector4(0.40f, 0.44f, 0.40f, 1), context.Width, context.Height, scale);
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

    private void EmitControlButton(string label, float x, float y, float width, float height, int viewportWidth, int viewportHeight, float scale)
    {
        EmitPanel(new SurfaceRect(x, y, width, height), new Vector4(0.15f, 0.17f, 0.16f, 1), viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width, 2 * scale), new Vector4(0.36f, 0.40f, 0.36f, 1), viewportWidth, viewportHeight);
        EmitText(label, x + 27 * scale, y + 10 * scale, new Vector4(0.82f, 0.84f, 0.78f, 1), viewportWidth, viewportHeight, scale);
    }

    private void EmitActionButton(string label, float x, float y, float width, float height, bool primary, bool hovered, bool pressed, int viewportWidth, int viewportHeight, float scale)
    {
        Vector4 panel = pressed
            ? new Vector4(0.30f, 0.24f, 0.10f, 1)
            : hovered
                ? new Vector4(0.20f, 0.19f, 0.13f, 1)
                : primary
                    ? new Vector4(0.24f, 0.20f, 0.10f, 1)
                    : new Vector4(0.12f, 0.14f, 0.13f, 1);
        Vector4 accent = primary
            ? new Vector4(0.86f, 0.61f, 0.16f, 1)
            : new Vector4(0.34f, 0.38f, 0.34f, 1);
        EmitPanel(new SurfaceRect(x, y, width, height), panel, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, 5 * scale, height), accent, viewportWidth, viewportHeight);
        EmitText(label, x + 24 * scale, y + 18 * scale, new Vector4(0.90f, 0.88f, 0.76f, 1), viewportWidth, viewportHeight, scale);
    }

    private void EmitProgress(float x, float y, float width, float height, float progress, int viewportWidth, int viewportHeight)
    {
        EmitPanel(new SurfaceRect(x, y, width, height), new Vector4(0.12f, 0.14f, 0.13f, 1), viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width * Math.Clamp(progress, 0, 1), height), new Vector4(0.82f, 0.58f, 0.14f, 1), viewportWidth, viewportHeight);
    }

    private void EmitPanel(SurfaceRect rect, Vector4 color, int width, int height)
    {
        float x = MathF.Round(rect.X + _offsetX);
        float y = MathF.Round(rect.Y + _offsetY);
        float rectWidth = MathF.Max(1f, MathF.Round(rect.Width));
        float rectHeight = MathF.Max(1f, MathF.Round(rect.Height));
        float left = x / width * 2 - 1;
        float right = (x + rectWidth) / width * 2 - 1;
        float top = 1 - y / height * 2;
        float bottom = 1 - (y + rectHeight) / height * 2;
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
        'G' => [14,17,16,23,17,17,15], 'H' => [17,17,17,31,17,17,17], 'I' => [31,4,4,4,4,4,31],
        'J' => [7,2,2,2,2,18,12], 'K' => [17,18,20,24,20,18,17], 'L' => [16,16,16,16,16,16,31],
        'M' => [17,27,21,21,17,17,17], 'N' => [17,25,21,19,17,17,17], 'O' => [14,17,17,17,17,17,14],
        'P' => [30,17,17,30,16,16,16], 'Q' => [14,17,17,17,21,18,13], 'R' => [30,17,17,30,20,18,17], 'S' => [15,16,16,14,1,1,30],
        'T' => [31,4,4,4,4,4,4], 'U' => [17,17,17,17,17,17,14], 'V' => [17,17,17,17,17,10,4],
        'W' => [17,17,17,21,21,21,10], 'X' => [17,17,10,4,10,17,17], 'Y' => [17,17,10,4,4,4,4],
        'Z' => [31,1,2,4,8,16,31],
        '0' => [14,17,19,21,25,17,14], '1' => [4,12,4,4,4,4,14], '2' => [14,17,1,2,4,8,31],
        '3' => [30,1,1,14,1,1,30], '4' => [2,6,10,18,31,2,2], '5' => [31,16,16,30,1,1,30],
        '6' => [14,16,16,30,17,17,14], '7' => [31,1,2,4,8,8,8], '8' => [14,17,17,14,17,17,14],
        '9' => [14,17,17,15,1,1,14], '.' => [0,0,0,0,0,12,12], ':' => [0,12,12,0,12,12,0],
        '/' => [1,2,4,8,16,0,0], '-' => [0,0,0,31,0,0,0], '+' => [0,4,4,31,4,4,0],
        '%' => [17,2,4,8,16,0,17], _ => [0,0,0,0,0,0,0]
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
