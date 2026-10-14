using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

public sealed class FrontendOverlayRenderer : IDisposable
{
    private const int MaxVertices = 32_768;
    private static readonly string[] s_menuNumbers = ["01", "02", "03", "04", "05", "06", "07", "08", "09"];
    private const int MaxDetailVertices = 65_536;
    private const int MaxControlsVertices = 131_072;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 3.0f;
    private const float GlyphAdvance = 18.0f;

    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly Dictionary<(string Text, float Width, float Size), string[]> _wrappedText = new();
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private OverlayVertex[] _vertices = new OverlayVertex[MaxVertices];
    private int _vertexLimit = MaxVertices;
    private int _vertexCount;
    private bool _disposed;
    private FrontendMenuBackground? _background;

    public FrontendOverlayRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _graphics = graphics;
        _pipeline = CreatePipeline(graphics);
        _text = new UiTextBatch(graphics);
    }

    public FrontendBackgroundDiagnostics BackgroundDiagnostics => _background?.Diagnostics ?? new("Deferred", 0, 0, null);
    public void PrepareBackground(RuntimeAssetCatalog catalog, RenderQuality quality, bool software = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _background ??= new FrontendMenuBackground(_graphics, catalog);
        _background.Prepare(quality, software);
    }
    public FrontendGeometry LastLayout { get; private set; }
    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        in FrontendSurfaceView view,
        float userScale = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _text.Begin();
        ArgumentNullException.ThrowIfNull(context);

        // Studio playback has its own compositor; the overlay must never add old branding.
        if (view.Kind == FrontendSurfaceKind.StudioSplash)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _vertexLimit = view.Kind switch
        {
            FrontendSurfaceKind.Controls => MaxControlsVertices,
            FrontendSurfaceKind.Detail => MaxDetailVertices,
            _ => MaxVertices
        };
        FrontendGeometry layout = view.Layout ?? FrontendGeometry.Resolve(context.Width, context.Height, userScale,
            controls: view.Kind == FrontendSurfaceKind.Controls);
        if (layout.IsEmpty || layout.Width != context.Width || layout.Height != context.Height)
        {
            LastRenderedVertexCount = 0;
            return; // A resize has not yet published matching input geometry.
        }
        LastLayout = layout;
        float scale = layout.Scale;
        int width = context.Width, height = context.Height;
        if (view.Kind != FrontendSurfaceKind.PauseMenu)
        {
            for (int band = 0; band < 24; band++)
                EmitPanel(new SurfaceRect(0, band * height / 24f, width, height / 24f + 1),
                    Vector4.Lerp(ForgeLineUiPalette.BackgroundDeep, ForgeLineUiPalette.Background, band / 24f), width, height);
        }
        if (view.Kind == FrontendSurfaceKind.PauseMenu)
        {
            Panel(layout.Header with { Width = layout.Navigation.Width }, ForgeLineUiPalette.Surface);
            Panel(layout.Title with { Width = layout.Navigation.Width }, ForgeLineUiPalette.Surface);
            Panel(layout.Footer, ForgeLineUiPalette.Surface);
            if (!string.IsNullOrEmpty(view.Feedback)) Panel(layout.Feedback, ForgeLineUiPalette.Surface);
        }
        else Panel(layout.SafeArea, ForgeLineUiPalette.Surface);
        Text("FORGELINE", layout.Header, ForgeLineUiPalette.TextPrimary, view.Kind == FrontendSurfaceKind.PauseMenu ? 1.46f : 1.8f);
        var tagline = layout.Header with { Y = layout.Header.Y + (view.Kind == FrontendSurfaceKind.PauseMenu ? 54 : 70) * scale };
        Text("BUILD. SUPPLY. CONQUER.", tagline, ForgeLineUiPalette.TextSecondary, .75f);
        Text(view.Title, layout.Title, ForgeLineUiPalette.Accent);
        // Decorations have their own visual-only rectangle and cannot enter a control slot.
        if (layout.Decorative.Width > 180 * scale && view.Kind is not (FrontendSurfaceKind.Controls or FrontendSurfaceKind.PauseMenu) && _background?.Ready != true)
        {
            var art = layout.Decorative;
            Panel(art, ForgeLineUiPalette.BackgroundRaised);
            for (int i = 1; i < 8; i++)
                Panel(art with { X = art.X + art.Width * i / 8, Width = 1 }, ForgeLineUiPalette.BorderSubtle);
            // Retain the industrial schematic inside the reserved art viewport.
            for (int i = 0; i < 4; i++)
            {
                float buildingWidth = art.Width * .15f;
                float buildingHeight = art.Height * (.2f + (i % 3) * .08f);
                Panel(new FrontendRegion(art.X + art.Width * (.12f + i * .2f), art.Y + art.Height * .8f - buildingHeight,
                    buildingWidth, buildingHeight), ForgeLineUiPalette.BackgroundDeep);
            }
            Text("TACTICAL COMMAND", art with { X = art.X + 20 * scale, Y = art.Y + 20 * scale, Width = art.Width - 40 * scale },
                ForgeLineUiPalette.TextSecondary, .8f);
        }
        if (_background?.Ready == true && view.Kind == FrontendSurfaceKind.MainMenu)
            Text("DIRECTORATE / CENTRAL DIVIDE", layout.MenuArtwork with { X = layout.MenuArtwork.X + 20 * scale,
                Y = layout.MenuArtwork.Y + 18 * scale, Width = layout.MenuArtwork.Width - 40 * scale }, ForgeLineUiPalette.TextSecondary, .72f);
        if (view.Kind is FrontendSurfaceKind.MainMenu or FrontendSurfaceKind.PauseMenu)
        {
            for (int i = 0; i < Math.Min(7, view.MenuEntries.Count); i++)
            {
                var item = view.MenuEntries[i]; var row = layout.MenuRow(i);
                bool focused = item.IsEnabled && item.IsFocused;
                Panel(row, focused ? ForgeLineUiPalette.SurfaceActive : item.IsEnabled ? ForgeLineUiPalette.SurfaceRaised : ForgeLineUiPalette.SurfaceDisabled);
                Panel(row with { Width = (focused ? 5 : 2) * scale }, focused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.BorderSubtle);
                Text(s_menuNumbers[i], row with { X = row.X + 22 * scale, Y = row.Y + 15 * scale, Width = 40 * scale }, ForgeLineUiPalette.TextMuted, .76f);
                Text(item.Label, row with { X = row.X + 76 * scale, Y = row.Y + 12 * scale, Width = row.Width - 220 * scale },
                    focused ? ForgeLineUiPalette.TextPrimary : item.IsEnabled ? ForgeLineUiPalette.TextSecondary : ForgeLineUiPalette.TextDisabled);
                if (focused || !item.IsEnabled)
                    Text(focused ? "SELECT" : "N/A", row with { X = row.Right - 128 * scale, Y = row.Y + 15 * scale, Width = 110 * scale },
                        focused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.TextDisabled, .68f);
            }
        }
        else if (view.Kind is FrontendSurfaceKind.Detail or FrontendSurfaceKind.Controls)
        {
            int start = Math.Clamp(view.DetailScroll, 0, Math.Max(0, view.DetailLines.Count - layout.VisibleDetailRows));
            int count = Math.Min(layout.VisibleDetailRows, view.DetailLines.Count - start);
            for (int i = 0; i < count; i++)
            {
                var line = view.DetailLines[start + i]; var row = layout.DetailRow(i);
                Panel(row, line.IsPressed ? ForgeLineUiPalette.SurfacePressed : line.IsFocused ? ForgeLineUiPalette.SurfaceActive :
                    line.IsHovered ? ForgeLineUiPalette.SurfaceHover : ForgeLineUiPalette.SurfaceRaised);
                if (line.IsFocused || line.IsHovered) Panel(row with { Width = 4 * scale }, ForgeLineUiPalette.Accent);
                Text(line.Label, layout.DetailLabel(i), ForgeLineUiPalette.TextSecondary);
                var valueSlot = layout.DetailValue(i, line.CanDecrease || line.CanIncrease);
                Vector4 color = line.IsWarning ? ForgeLineUiPalette.Warning : line.IsFocused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.TextPrimary;
                if (layout.Controls)
                {
                    var wrapped = Wrap(line.Value, valueSlot.Width / scale, 22);
                    for (int lineIndex = 0; lineIndex < Math.Min(3, wrapped.Length); lineIndex++)
                        Text(wrapped[lineIndex], valueSlot with { Y = valueSlot.Y + lineIndex * 27 * scale }, color);
                }
                else Text(line.Value, valueSlot, color);
                if (line.CanDecrease) Button("-", layout.Decrease(i));
                if (line.CanIncrease) Button("+", layout.Increase(i));
            }
            if (view.DetailLines.Count > count)
                Text($"{start + 1}-{start + count} / {view.DetailLines.Count}  UP/DOWN / WHEEL", layout.Title with { X = layout.Title.X + layout.Title.Width * .42f, Width = layout.Title.Width * .58f }, ForgeLineUiPalette.TextMuted, .65f);
            if (!string.IsNullOrEmpty(view.PrimaryAction)) Action(view.PrimaryAction, layout.PrimaryAction, true, view.PrimaryHovered, view.PrimaryPressed);
            if (!string.IsNullOrEmpty(view.SecondaryAction)) Action(view.SecondaryAction, layout.SecondaryAction, false, view.SecondaryHovered, view.SecondaryPressed);
        }
        else if (view.Kind == FrontendSurfaceKind.Loading)
        {
            Text("INITIALIZING COMMAND SYSTEMS", layout.Title, ForgeLineUiPalette.TextSecondary);
            Text(view.Status, layout.Details, ForgeLineUiPalette.Accent);
            if (view.HasProgress)
            {
                var r = layout.PrimaryAction;
                EmitProgress(layout.Details.X, r.Y, layout.Details.Width, 8 * scale, view.Progress, width, height);
                Text($"{Math.Clamp((int)(view.Progress * 100), 0, 100)}%", layout.Feedback, ForgeLineUiPalette.TextPrimary);
            }
        }
        var footerLines = Wrap(view.Footer, layout.Footer.Width / scale, 22 * .78f);
        for (int i = 0; i < Math.Min(2, footerLines.Length); i++)
            Text(footerLines[i], layout.Footer with { Y = layout.Footer.Y + i * 24 * scale }, ForgeLineUiPalette.TextMuted, .78f);
        if (!string.IsNullOrEmpty(view.Feedback)) Text(view.Feedback, layout.Feedback, ForgeLineUiPalette.Warning, .8f);
        if (!string.IsNullOrEmpty(view.ProductVersion))
            Text($"VERSION {view.ProductVersion}", layout.Footer with { Y = height - 40 * scale }, ForgeLineUiPalette.TextMuted, .65f);
        if (_graphics.SupportsUiText && !_text.Ready)
            Text("UI FONT ERROR - REINSTALL FORGELINE", layout.Feedback, ForgeLineUiPalette.Warning, .8f);

        void Panel(FrontendRegion r, Vector4 color) => EmitPanel(new SurfaceRect(r.X, r.Y, r.Width, r.Height), color, width, height);
        void Text(string text, FrontendRegion r, Vector4 color, float factor = 1) =>
            EmitText(text, r.X, r.Y, color, width, height, scale * factor, r.Width / (scale * factor));
        void Button(string label, FrontendRegion r) => EmitControlButton(label, r.X, r.Y, r.Width, r.Height, width, height, scale);
        void Action(string label, FrontendRegion r, bool primary, bool hovered, bool pressed) =>
            EmitActionButton(label, r.X, r.Y, r.Width, r.Height, primary, hovered, pressed, width, height, scale);

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
        var imageRegion = view.Kind == FrontendSurfaceKind.MainMenu ? layout.MenuArtwork : layout.Decorative;
        bool imageDrawn = _background?.Ready == true && imageRegion.Width > 0 && imageRegion.Height > 0 &&
            view.Kind is not (FrontendSurfaceKind.Controls or FrontendSurfaceKind.PauseMenu);
        if (imageDrawn) _background!.Render(context, imageRegion);
        _text.Render(context);
        LastRenderedVertexCount = _vertexCount + _text.VertexCount + (imageDrawn ? 6 : 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (IGraphicsBuffer buffer in _vertexBuffers.Values) buffer.Dispose();
        _vertexBuffers.Clear();
        _background?.Dispose();
        _text.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private string[] Wrap(string text, float width, float size)
    {
        var key = (text, width, size);
        if (_wrappedText.TryGetValue(key, out var lines)) return lines;
        if (_wrappedText.Count >= 64) _wrappedText.Clear();
        lines = UiFontMetrics.Default.Wrap(text, width, size);
        _wrappedText.Add(key, lines);
        return lines;
    }

    private void EmitControlButton(string label, float x, float y, float width, float height, int viewportWidth, int viewportHeight, float scale)
    {
        EmitPanel(new SurfaceRect(x, y, width, height), ForgeLineUiPalette.SurfaceHover, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width, scale), ForgeLineUiPalette.Border, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y + height - scale, width, scale), ForgeLineUiPalette.BorderSubtle, viewportWidth, viewportHeight);
        EmitText(label, x + 27 * scale, y + 10 * scale, ForgeLineUiPalette.TextPrimary, viewportWidth, viewportHeight, scale);
    }

    private void EmitActionButton(string label, float x, float y, float width, float height, bool primary, bool hovered, bool pressed, int viewportWidth, int viewportHeight, float scale)
    {
        Vector4 fill = pressed
            ? ForgeLineUiPalette.SurfacePressed
            : hovered
                ? ForgeLineUiPalette.SurfaceHover
                : primary
                    ? ForgeLineUiPalette.SurfaceActive
                    : ForgeLineUiPalette.SurfaceRaised;
        Vector4 border = primary ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.Border;
        EmitPanel(new SurfaceRect(x, y, width, height), fill, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, 5 * scale, height), border, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width, 1 * scale), border, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y + height - 1 * scale, width, 1 * scale), border, viewportWidth, viewportHeight);
        EmitText(label, x + 24 * scale, y + 18 * scale,
            primary ? ForgeLineUiPalette.TextPrimary : ForgeLineUiPalette.TextSecondary,
            viewportWidth, viewportHeight, scale);
    }

    private void EmitProgress(float x, float y, float width, float height, float progress, int viewportWidth, int viewportHeight)
    {
        EmitPanel(new SurfaceRect(x, y, width, height), ForgeLineUiPalette.SurfaceRaised, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x, y, width * Math.Clamp(progress, 0, 1), height), ForgeLineUiPalette.Accent, viewportWidth, viewportHeight);
    }

    private void EmitPanel(SurfaceRect rect, Vector4 color, int width, int height)
    {
        float x = MathF.Round(rect.X);
        float y = MathF.Round(rect.Y);
        float rectWidth = MathF.Max(1f, MathF.Round(rect.Width));
        float rectHeight = MathF.Max(1f, MathF.Round(rect.Height));
        float left = x / width * 2 - 1;
        float right = (x + rectWidth) / width * 2 - 1;
        float top = 1 - y / height * 2;
        float bottom = 1 - (y + rectHeight) / height * 2;
        EmitTriangle(left, top, right, top, right, bottom, color);
        EmitTriangle(left, top, right, bottom, left, bottom, color);
    }

    private void EmitText(string text, float x, float y, Vector4 color, int width, int height, float scale, float maximumWidth = float.MaxValue)
    {
        if (_text.Add(text, x, y, UiTypography.Style(text == "FORGELINE" ? UiTextRole.CommandTitle : UiTextRole.MenuItem).Size * scale, color, width, height,
            maximumWidth: maximumWidth == float.MaxValue ? maximumWidth : maximumWidth * scale,
            bold: text == "FORGELINE" || color == ForgeLineUiPalette.Warning)) return;

        // Bootstrap bitmap fallback obeys the same bounded text slot.
        int maximumCharacters = maximumWidth == float.MaxValue ? text.Length : Math.Max(0, (int)(maximumWidth / GlyphAdvance));
        if (text.Length > maximumCharacters) text = text[..maximumCharacters];
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
        if (_vertexCount + 3 > _vertexLimit) return;
        if (_vertexCount + 3 > _vertices.Length)
            Array.Resize(ref _vertices, Math.Min(_vertices.Length * 2, _vertexLimit));
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(ax,ay),color);
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(bx,by),color);
        _vertices[_vertexCount++] = new OverlayVertex(new Vector2(cx,cy),color);
    }

    private IGraphicsBuffer GetFrameVertexBuffer(int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(frameIndex, out IGraphicsBuffer? buffer))
        {
            if (buffer.Description.SizeInBytes >= (ulong)(_vertices.Length * VertexStride)) return buffer;
            buffer.Dispose();
        }
        buffer = _graphics.CreateBuffer(new GraphicsBufferDescription((ulong)(_vertices.Length * VertexStride), GraphicsBufferMemory.Upload));
        _vertexBuffers[frameIndex] = buffer;
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
            DepthEnabled=false,
            AlphaBlendEnabled=true
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
