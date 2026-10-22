using System.Numerics;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

/// <summary>Noninteractive, world-anchored order caption, independent of rings and hover.</summary>
internal sealed class SelectedUnitOrderStatusSurface : IGameplayHudSurface
{
    private readonly UiTextBatch _text;
    private bool _disposed;

    public SelectedUnitOrderStatusSurface(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _text = new UiTextBatch(graphics);
    }

    public GameplayHudRegion Regions => GameplayHudRegion.GlobalOverlay;
    public int LastRenderedVertexCount { get; private set; }
    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;

    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0;
        _text.Begin();
        if (context.PreAlphaUx.Mode != PreAlphaUxMode.None ||
            context.Graphics.Width <= 0 || context.Graphics.Height <= 0 ||
            context.Snapshot.SelectedOrders is not { } orders ||
            !orders.MatchesSelection(context.Snapshot.SessionId, context.InformationLayer.SelectedEntities) ||
            orders.Orders.Length == 0 || string.IsNullOrWhiteSpace(orders.Summary))
            return;

        float scale = context.Layout.Scale;
        float width = MathF.Min(420 * scale, MathF.Max(40, context.Layout.SafeArea.Width));
        var primary = orders.Orders[0];
        var anchor = context.Camera.WorldToScreen(
            primary.Origin + Vector3.UnitY * 5.0f,
            context.Graphics.Width, context.Graphics.Height);
        float x = anchor.IsVisible
            ? anchor.Position.X - width * 0.5f
            : context.Layout.SafeArea.X + 12 * scale;
        float y = anchor.IsVisible
            ? anchor.Position.Y - 24 * scale
            : context.Layout.TopStatusBar.Bottom + 12 * scale;
        x = Math.Clamp(x, context.Layout.SafeArea.X,
            MathF.Max(context.Layout.SafeArea.X, context.Layout.SafeArea.Right - width));
        y = Math.Clamp(y, context.Layout.TopStatusBar.Bottom + 4 * scale,
            MathF.Max(context.Layout.TopStatusBar.Bottom + 4 * scale,
                context.Layout.SafeArea.Bottom - 22 * scale));

        const float textSize = 13;
        float size = textSize * Math.Clamp(scale, 0.7f, 2.0f);
        _text.Add(orders.Summary, x + scale, y + scale, size,
            new Vector4(0, 0, 0, 0.82f),
            context.Graphics.Width, context.Graphics.Height, width, bold: true);
        _text.Add(orders.Summary, x, y, size,
            RtsSelectionVisualStyle.Selected with { W = 0.93f },
            context.Graphics.Width, context.Graphics.Height, width, bold: true);
        _text.Render(context.Graphics);
        LastRenderedVertexCount = _text.VertexCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _text.Dispose();
        _disposed = true;
    }
}
