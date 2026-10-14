using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

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
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private OverlayVertex[] _vertices = new OverlayVertex[MaxVertices];
    private int _vertexLimit = MaxVertices;
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
        _offsetX = 0f;
        _offsetY = 0f;
        float viewportScale = MathF.Min(
            context.Width / 1920f,
            context.Height / 1080f);
        float scale = Math.Min(
            viewportScale * (view.Kind == FrontendSurfaceKind.Controls ? Math.Min(userScale, 1f) : userScale),
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

        RenderBackdrop(context.Width, context.Height, scale, view.Kind);
        RenderHeader(context.Width, context.Height, scale, view.Kind);

        if (view.Kind == FrontendSurfaceKind.Loading)
        {
            EmitText("INITIALIZING COMMAND SYSTEMS", 94 * scale, 850 * scale, ForgeLineUiPalette.TextSecondary, context.Width, context.Height, scale);
            EmitText(view.Status, 94 * scale, 890 * scale, ForgeLineUiPalette.Accent, context.Width, context.Height, scale);
            if (!string.IsNullOrEmpty(view.Feedback))
                EmitText(view.Feedback, 94 * scale, 982 * scale, ForgeLineUiPalette.TextMuted, context.Width, context.Height, scale);
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
                    ForgeLineUiPalette.TextPrimary,
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
                    ForgeLineUiPalette.Accent,
                    context.Width,
                    context.Height,
                    scale);
                EmitPanel(
                    new SurfaceRect(
                        92 * scale,
                        330 * scale,
                        980 * scale,
                        2 * scale),
                    ForgeLineUiPalette.BorderSubtle,
                    context.Width,
                    context.Height);
            }

            float y = 350 * scale;
            int itemIndex = 0;
            foreach (FrontendMenuEntryView item in view.MenuEntries)
            {
                bool focused = item.IsEnabled && item.IsFocused;
                var row = new SurfaceRect(82 * scale, y - 12 * scale, 560 * scale, 52 * scale);
                EmitPanel(
                    row,
                    focused ? ForgeLineUiPalette.SurfaceActive :
                    item.IsEnabled ? ForgeLineUiPalette.SurfaceRaised : ForgeLineUiPalette.SurfaceDisabled,
                    context.Width, context.Height);
                EmitPanel(
                    new SurfaceRect(row.X, row.Y, row.Width, 1 * scale),
                    focused ? ForgeLineUiPalette.AccentDim : ForgeLineUiPalette.BorderSubtle,
                    context.Width, context.Height);
                EmitPanel(
                    new SurfaceRect(row.X, row.Y + row.Height - 1 * scale, row.Width, 1 * scale),
                    focused ? ForgeLineUiPalette.AccentDim : ForgeLineUiPalette.BorderSubtle,
                    context.Width, context.Height);
                EmitPanel(
                    new SurfaceRect(row.X, row.Y, (focused ? 5f : 2f) * scale, row.Height),
                    focused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.BorderSubtle,
                    context.Width, context.Height);

                string number = s_menuNumbers[Math.Min(itemIndex, s_menuNumbers.Length - 1)];
                EmitText(number, 104 * scale, y + 3 * scale,
                    focused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.TextMuted,
                    context.Width, context.Height, 0.76f * scale);
                EmitText(item.Label, 158 * scale, y,
                    focused ? ForgeLineUiPalette.TextPrimary :
                    item.IsEnabled ? ForgeLineUiPalette.TextSecondary : ForgeLineUiPalette.TextDisabled,
                    context.Width, context.Height, scale);
                if (focused)
                {
                    EmitText("SELECT", 514 * scale, y + 3 * scale,
                        ForgeLineUiPalette.Accent, context.Width, context.Height, 0.68f * scale);
                    EmitPanel(
                        new SurfaceRect(625 * scale, (y + 1 * scale), 3 * scale, 17 * scale),
                        ForgeLineUiPalette.Accent, context.Width, context.Height);
                }
                else if (!item.IsEnabled)
                {
                    EmitText("N/A", 554 * scale, y + 3 * scale,
                        ForgeLineUiPalette.TextDisabled, context.Width, context.Height, 0.68f * scale);
                }

                y += 72 * scale;
                itemIndex++;
            }
            if (!string.IsNullOrEmpty(view.Feedback))
            {
                EmitText(view.Feedback, 94 * scale, 873 * scale, ForgeLineUiPalette.Warning, context.Width, context.Height, 0.82f * scale);
            }

            EmitText(view.Footer, 94 * scale, 930 * scale, ForgeLineUiPalette.TextMuted, context.Width, context.Height, 0.78f * scale);

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
                    ForgeLineUiPalette.TextMuted,
                    context.Width,
                    context.Height,
                    0.78f * scale);
            }
        }
        else if (view.Kind is FrontendSurfaceKind.Detail or FrontendSurfaceKind.Controls)
        {
            bool controls = view.Kind == FrontendSurfaceKind.Controls;
            EmitText(view.Title, 94 * scale + contentOffset, 286 * scale, ForgeLineUiPalette.Accent, context.Width, context.Height, scale);
            EmitPanel(new SurfaceRect(92 * scale, 330 * scale, (controls ? 1740 : 980) * scale, 2 * scale), ForgeLineUiPalette.BorderSubtle, context.Width, context.Height);

            float y = 390 * scale;
            foreach (FrontendDetailLineView line in view.DetailLines)
            {
                var detailRow = new SurfaceRect(92 * scale, y - 13 * scale, 980 * scale, (controls ? 43f : 48f) * scale);
                Vector4 rowPanel = line.IsPressed
                    ? ForgeLineUiPalette.SurfacePressed
                    : line.IsFocused
                        ? ForgeLineUiPalette.SurfaceActive
                        : line.IsHovered
                            ? ForgeLineUiPalette.SurfaceHover
                            : ForgeLineUiPalette.SurfaceRaised;
                EmitPanel(detailRow, rowPanel, context.Width, context.Height);
                EmitPanel(
                    new SurfaceRect(detailRow.X, detailRow.Y + detailRow.Height - scale, detailRow.Width, scale),
                    ForgeLineUiPalette.BorderSubtle, context.Width, context.Height);
                if (line.IsFocused || line.IsHovered)
                {
                    EmitPanel(
                        new SurfaceRect(detailRow.X, detailRow.Y, 4 * scale, detailRow.Height),
                        ForgeLineUiPalette.Accent, context.Width, context.Height);
                }

                Vector4 valueColor = line.IsWarning
                    ? ForgeLineUiPalette.Warning
                    : line.IsFocused
                        ? ForgeLineUiPalette.Accent
                        : ForgeLineUiPalette.TextPrimary;
                float valueScale = controls
                    ? MathF.Min(scale, 1370f * scale / MathF.Max(1f, line.Value.Length * GlyphAdvance))
                    : scale;
                EmitText(line.Label, 112 * scale, y, controls
                    ? ForgeLineUiPalette.TextSecondary
                    : ForgeLineUiPalette.TextMuted, context.Width, context.Height, scale);
                EmitText(line.Value, 460 * scale, y, valueColor, context.Width, context.Height, valueScale);

                if (line.CanDecrease)
                {
                    EmitControlButton("-", 830 * scale, (y - 13 * scale), 72 * scale, 42 * scale, context.Width, context.Height, scale);
                }

                if (line.CanIncrease)
                {
                    EmitControlButton("+", 916 * scale, (y - 13 * scale), 72 * scale, 42 * scale, context.Width, context.Height, scale);
                }

                y += (controls ? 44 : 58) * scale;
            }

            if (!string.IsNullOrEmpty(view.PrimaryAction))
            {
                EmitActionButton(view.PrimaryAction, 782 * scale, 838 * scale, 290 * scale, 58 * scale, true, view.PrimaryHovered, view.PrimaryPressed, context.Width, context.Height, scale);
            }

            if (!string.IsNullOrEmpty(view.SecondaryAction))
            {
                EmitActionButton(view.SecondaryAction, 92 * scale, 838 * scale, 220 * scale, 58 * scale, false, view.SecondaryHovered, view.SecondaryPressed, context.Width, context.Height, scale);
            }

            EmitText(view.Footer, 94 * scale, 930 * scale, ForgeLineUiPalette.TextMuted, context.Width, context.Height, scale);
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

    private void RenderBackdrop(int width, int height, float scale, FrontendSurfaceKind kind)
    {
        if (kind == FrontendSurfaceKind.PauseMenu)
        {
            EmitPanel(new SurfaceRect(64 * scale, 54 * scale, 1040 * scale, 972 * scale),
                ForgeLineUiPalette.Surface, width, height);
            EmitPanel(new SurfaceRect(64 * scale, 54 * scale, 1040 * scale, 2 * scale),
                ForgeLineUiPalette.AccentDim, width, height);
            EmitPanel(new SurfaceRect(64 * scale, 54 * scale, 2 * scale, 972 * scale),
                ForgeLineUiPalette.Border, width, height);
            return;
        }

        // A bounded set of wide color bands gives the menu an intentional
        // dark-to-light hierarchy without textures or runtime asset dependencies.
        const int bandCount = 24;
        for (int band = 0; band < bandCount; band++)
        {
            float t = band / (float)(bandCount - 1);
            Vector4 color = Vector4.Lerp(
                ForgeLineUiPalette.BackgroundDeep,
                ForgeLineUiPalette.Background,
                t * 0.95f);
            EmitPanel(new SurfaceRect(0, band * 45 * scale, 1920 * scale, 45 * scale),
                color, width, height);
        }

        EmitPanel(new SurfaceRect(0, 0, 10 * scale, 1080 * scale),
            ForgeLineUiPalette.AccentDim, width, height);
        EmitPanel(new SurfaceRect(80 * scale, 168 * scale, 1760 * scale, scale),
            ForgeLineUiPalette.BorderSubtle, width, height);
        EmitPanel(new SurfaceRect(80 * scale, 1007 * scale, 1760 * scale, scale),
            ForgeLineUiPalette.BorderSubtle, width, height);

        if (kind == FrontendSurfaceKind.MainMenu)
        {
            RenderMenuComposition(width, height, scale);
        }
        else if (kind is FrontendSurfaceKind.Detail or FrontendSurfaceKind.Controls)
        {
            bool controls = kind == FrontendSurfaceKind.Controls;
            EmitPanel(new SurfaceRect(68 * scale, 251 * scale, (controls ? 1770 : 1032) * scale, 700 * scale),
                ForgeLineUiPalette.Surface, width, height);
            EmitPanel(new SurfaceRect(68 * scale, 251 * scale, 5 * scale, 700 * scale),
                ForgeLineUiPalette.Border, width, height);
            if (!controls)
            {
                RenderTechnicalGrid(1200f, 255f, 625f, 605f, width, height, scale);
                EmitText("TACTICAL COMMAND", 1244 * scale, 310 * scale,
                    ForgeLineUiPalette.TextSecondary, width, height, 0.94f * scale);
                EmitText("BUILD / SUPPLY / CONQUER", 1244 * scale, 365 * scale,
                    ForgeLineUiPalette.AccentDim, width, height, 0.76f * scale);
            }
        }
    }

    private void RenderHeader(int width, int height, float scale, FrontendSurfaceKind kind)
    {
        EmitPanel(new SurfaceRect(91 * scale, 77 * scale, 6 * scale, 74 * scale),
            ForgeLineUiPalette.Accent, width, height);
        EmitText("FORGELINE", 119 * scale, 80 * scale,
            ForgeLineUiPalette.TextPrimary, width, height, 1.46f * scale);
        EmitText("BUILD. SUPPLY. CONQUER.", 121 * scale, 134 * scale,
            ForgeLineUiPalette.TextSecondary, width, height, 0.75f * scale);
        if (kind is FrontendSurfaceKind.MainMenu or FrontendSurfaceKind.Loading)
        {
            EmitText("STRATEGIC OPERATIONS", 1518 * scale, 110 * scale,
                ForgeLineUiPalette.TextMuted, width, height, 0.8f * scale);
        }
    }

    private void RenderMenuComposition(int width, int height, float scale)
    {
        EmitPanel(new SurfaceRect(66 * scale, 263 * scale, 610 * scale, 593 * scale),
            ForgeLineUiPalette.Surface, width, height);
        EmitPanel(new SurfaceRect(66 * scale, 263 * scale, 4 * scale, 593 * scale),
            ForgeLineUiPalette.AccentDim, width, height);
        EmitText("OPERATIONS", 91 * scale, 291 * scale,
            ForgeLineUiPalette.Accent, width, height, 0.9f * scale);
        EmitText("SELECT AN ACTION", 91 * scale, 321 * scale,
            ForgeLineUiPalette.TextMuted, width, height, 0.65f * scale);
        RenderTechnicalGrid(750f, 241f, 1090f, 640f, width, height, scale);

        // Industrial skyline rendered as an understated diagram, not a false
        // gameplay preview. Geometry remains behind the functional menu.
        EmitPanel(new SurfaceRect(885 * scale, 682 * scale, 820 * scale, 165 * scale),
            ForgeLineUiPalette.BackgroundRaised, width, height);
        EmitPanel(new SurfaceRect(1010 * scale, 553 * scale, 168 * scale, 129 * scale),
            ForgeLineUiPalette.BackgroundRaised, width, height);
        EmitPanel(new SurfaceRect(1248 * scale, 601 * scale, 206 * scale, 81 * scale),
            ForgeLineUiPalette.BackgroundRaised, width, height);
        EmitPanel(new SurfaceRect(1510 * scale, 536 * scale, 128 * scale, 146 * scale),
            ForgeLineUiPalette.BackgroundRaised, width, height);
        EmitPanel(new SurfaceRect(1045 * scale, 457 * scale, 8 * scale, 96 * scale),
            ForgeLineUiPalette.BorderSubtle, width, height);
        EmitPanel(new SurfaceRect(1548 * scale, 407 * scale, 7 * scale, 129 * scale),
            ForgeLineUiPalette.BorderSubtle, width, height);
        EmitPanel(new SurfaceRect(903 * scale, 680 * scale, 791 * scale, 2 * scale),
            ForgeLineUiPalette.AccentDim, width, height);
        EmitPanel(new SurfaceRect(885 * scale, 846 * scale, 820 * scale, 3 * scale),
            ForgeLineUiPalette.Border, width, height);
        EmitText("INDUSTRIAL WARFARE", 882 * scale, 296 * scale,
            ForgeLineUiPalette.TextPrimary, width, height, 1.10f * scale);
        EmitText("COMMAND / PRODUCTION / LOGISTICS", 884 * scale, 350 * scale,
            ForgeLineUiPalette.TextSecondary, width, height, 0.78f * scale);
        EmitText("EXTRACT. MANUFACTURE. ADVANCE.", 892 * scale, 901 * scale,
            ForgeLineUiPalette.Accent, width, height, 0.75f * scale);
    }

    private void RenderTechnicalGrid(
        float x, float y, float widthPx, float heightPx,
        int viewportWidth, int viewportHeight, float scale)
    {
        EmitPanel(new SurfaceRect(x * scale, y * scale, widthPx * scale, heightPx * scale),
            ForgeLineUiPalette.Background, viewportWidth, viewportHeight);
        const float spacing = 80f;
        for (float column = spacing; column < widthPx; column += spacing)
        {
            EmitPanel(new SurfaceRect((x + column) * scale, y * scale, scale, heightPx * scale),
                ForgeLineUiPalette.BorderSubtle, viewportWidth, viewportHeight);
        }
        for (float row = spacing; row < heightPx; row += spacing)
        {
            EmitPanel(new SurfaceRect(x * scale, (y + row) * scale, widthPx * scale, scale),
                ForgeLineUiPalette.BorderSubtle, viewportWidth, viewportHeight);
        }
        EmitPanel(new SurfaceRect(x * scale, y * scale, widthPx * scale, 2 * scale),
            ForgeLineUiPalette.Border, viewportWidth, viewportHeight);
        EmitPanel(new SurfaceRect(x * scale, (y + heightPx - 2f) * scale, widthPx * scale, 2 * scale),
            ForgeLineUiPalette.Border, viewportWidth, viewportHeight);
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
