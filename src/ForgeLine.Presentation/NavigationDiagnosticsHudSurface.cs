using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class NavigationDiagnosticsHudSurface : IGameplayHudSurface
{
    private readonly IGraphicsDevice _graphics;
    private UiTextBatch? _text;
    private IGraphicsPipeline? _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private bool _disposed, _requiredThisFrame;
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector2 Position, Vector4 Color);
    public NavigationDiagnosticsHudSurface(IGraphicsDevice graphics) => _graphics = graphics;
    public GameplayHudRegion Regions => GameplayHudRegion.GlobalOverlay;
    public int LastRenderedVertexCount { get; private set; }
    // An invisible optional surface requires no atlas. Its neutral diagnostics
    // preserve the renderer's readiness checks for every active text surface.
    public UiTextDiagnostics TextDiagnostics => _requiredThisFrame
        ? _text?.Diagnostics ?? default : new(0, 0, 0, 0, 0, true, null);
    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0; _requiredThisFrame = false;
        if (!context.NavigationDiagnostics.Visible || context.PreAlphaUx.Mode != PreAlphaUxMode.None) return;
        _requiredThisFrame = true;
        _text ??= new(_graphics);
        _text.Begin();
        var lines = NavigationDiagnosticsViewModel.Lines(context.Snapshot, context.NavigationDiagnostics);
        float scale = Math.Clamp(context.Layout.Scale, .7f, 2);
        float x = context.Layout.SafeArea.X + 12 * scale;
        float y = context.Layout.TopStatusBar.Bottom + 12 * scale;
        float availableWidth = context.Layout.Minimap.IsEmpty ? context.Layout.SafeArea.Width - 24 * scale :
            MathF.Max(240 * scale, context.Layout.Minimap.X - x - 12 * scale);
        float width = MathF.Min(640 * scale, MathF.Min(availableWidth, context.Layout.SafeArea.Right - x - 12 * scale));
        int scroll = Math.Clamp(context.NavigationDiagnostics.Scroll, 0, Math.Max(0, lines.Count - 3));
        int rows = Math.Min(lines.Count - scroll, Math.Max(0, (int)((context.Layout.SafeArea.Bottom - y - 16 * scale) / (20 * scale))));
        if (rows <= 0 || width <= 0) return;
        Background(context.Graphics, new(x - 6 * scale, y - 6 * scale, width + 12 * scale, rows * 20 * scale + 12 * scale));
        _text.Clip = new(x, y, width, rows * 20 * scale);
        for (int row = 0; row < rows; row++, y += 20 * scale)
        {
            int i = row < 2 ? row : row + scroll;
            if (i >= lines.Count) break;
            _text.Add(lines[i], x, y, 13 * scale, new(.7f, 1, .85f, 1),
                context.Graphics.Width, context.Graphics.Height, width, bold: true);
        }
        _text.Render(context.Graphics); LastRenderedVertexCount = _text.VertexCount + 6;
    }

    private void Background(IGraphicsCommandContext context, HudRect rect)
    {
        _pipeline ??= HudOverlayPrimitives.CreatePipeline(_graphics, alphaBlendEnabled: true);
        ArgumentOutOfRangeException.ThrowIfNegative(context.FrameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(context.FrameIndex, 3);
        if (!_buffers.TryGetValue(context.FrameIndex, out var buffer))
        {
            buffer = _graphics.CreateBuffer(new(144, GraphicsBufferMemory.Upload));
            _buffers.Add(context.FrameIndex, buffer);
        }
        Vector2 a = new(rect.X / context.Width * 2 - 1, 1 - rect.Y / context.Height * 2);
        Vector2 c = new(rect.Right / context.Width * 2 - 1, 1 - rect.Bottom / context.Height * 2);
        Vector4 color = new(.025f, .04f, .045f, .94f);
        Span<Vertex> vertices = stackalloc Vertex[6] { new(a, color), new(new(c.X, a.Y), color), new(c, color),
            new(a, color), new(c, color), new(new(a.X, c.Y), color) };
        buffer.SetData<Vertex>(vertices);
        context.SetPipeline(_pipeline); context.SetVertexBuffer(buffer, 24); context.Draw(6);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _text?.Dispose(); _pipeline?.Dispose();
        foreach (var buffer in _buffers.Values) buffer.Dispose();
        _disposed = true;
    }
}
