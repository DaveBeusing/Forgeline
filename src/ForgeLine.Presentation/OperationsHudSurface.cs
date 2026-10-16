using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class OperationsHudSurface : IGameplayHudSurface
{
    internal const int MaxVertices = 262_144;
    private const int MaxLines = 24;
    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly IGraphicsPipeline _pipeline;
    private readonly GameplayHudSkin _skin;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly OperationsVertex[] _vertices = new OperationsVertex[MaxVertices];
    private int _count, _line, _width, _height, _underlay;
    private HudRect _bounds;
    private float _scale;
    private bool _disposed;
    public OperationsHudSurface(IGraphicsDevice graphics, RuntimeAssetCatalog? runtimeAssets = null)
    {
        _graphics = graphics; _text = new(graphics); _pipeline = SelectionInspectorHudRenderer.CreatePipeline(graphics, alphaBlendEnabled: true);
        try { _skin = new(graphics, runtimeAssets, icons: true); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }
    public GameplayHudRegion Regions => GameplayHudRegion.SecondaryView | GameplayHudRegion.AlertStack;
    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }
    internal float LastPanelTextSize { get; private set; }
    private float _minimumTextScale;
    private bool _panelText;
    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0; LastPanelTextSize = 0; _panelText = false; _text.Begin();
        _minimumTextScale = MathF.Min(1, context.Layout.Scale);
        _width = context.Graphics.Width; _height = context.Graphics.Height;
        if (_width <= 0 || _height <= 0) return;
        _skin.Begin(context.Graphics);
        _count = 0; _line = 0; _underlay = 0;
        _scale = MathF.Min(context.Layout.Scale, OperationsLayout.Entry(context.Layout).Width / 126);
        _bounds = OperationsLayout.Entry(context.Layout);
        bool panelVisible = context.Operations.Open && !context.Operations.Suppressed && context.Operations.Session == context.Snapshot.SessionId;
        if (!_bounds.IsEmpty && !panelVisible)
        {
            if (!_skin.Control(_bounds)) Quad(_bounds, GameplayHudVisualStyle.PanelBackground);
            Line("OPERATIONS", true);
        }
        var view = context.Operations;
        if (view.Open && !view.Suppressed && view.Session == context.Snapshot.SessionId)
        {
            _scale = OperationsLayout.Scale(context.Layout);
            _panelText = true;
            LastPanelTextSize = MathF.Max(14 * _minimumTextScale, 14 * _scale);
            _bounds = OperationsLayout.Panel(context.Layout); _line = 0;
            if (_scale > 0)
            {
                if (_skin.Ready) { Quad(_bounds, GameplayHudVisualStyle.PanelBackground with { W = 1 }); _underlay = _count; }
                if (!_skin.Panel(_bounds)) Quad(_bounds, GameplayHudVisualStyle.PanelBackground with { W = 1 });
                var header = new HudRect(_bounds.X, _bounds.Y, _bounds.Width, 24 * _scale);
                if (!_skin.Panel(header, "panel.raised")) Quad(header, GameplayHudVisualStyle.PanelHeader);
                Quad(new HudRect(_bounds.X, _bounds.Y, 3 * _scale, _bounds.Height), GameplayHudVisualStyle.Focus);
                Line("OPERATIONS", true);
                Control(context.Layout, 0, "CLOSE");
                var data = OperationsSnapshot.Resolve(context.Snapshot);
                if (data is null) Line("WAITING FOR CURRENT OWNED DATA");
                else
                {
                    Span<char> text = stackalloc char[192];
                    var b = new OperationsTextBuilder(text);
                    b.Append("FACILITIES "); b.Number(data.FacilityCount, "0"); b.Append(" LISTED "); b.Number(data.Facilities.Count, "0");
                    b.Append(" ROUTES "); b.Number(data.RouteCount, "0"); Line(b.Written);
                    b = new(text); b.Append("TICK "); b.Number(data.Tick.Value, "0"); b.Append(" / NET FLOW N/A: NO FLOW COUNTERS"); Line(b.Written);
                    for (int i = 0; i < Math.Min(6, data.Resources.Count); i += 2)
                    {
                        b = new(text);
                        for (int j = i; j < Math.Min(i + 2, data.Resources.Count); j++)
                        { b.Append(data.Resources[j].Name); b.Append(" "); b.Number(data.Resources[j].Quantity, "0"); b.Append("   "); }
                        Line(b.Written);
                    }
                    if (data.Resources.Count > 6)
                    { b = new(text); b.Append(data.Resources[6].Name); b.Append(" "); b.Number(data.Resources[6].Quantity, "0"); Line(b.Written); }
                    _line = 0;
                    Control(context.Layout, 1, view.Filter switch
                    {
                        OperationsCategory.Production => "PRODUCTION",
                        OperationsCategory.Logistics => "LOGISTICS",
                        OperationsCategory.Supply => "SUPPLY",
                        OperationsCategory.Power => "POWER",
                        OperationsCategory.Blocked => "BLOCKED",
                        _ => "ALL FACILITIES"
                    });
                    Control(context.Layout, 2, "NEXT PAGE");
                    for (int i = 0; i < OperationsLayout.PageSize; i++)
                    {
                        b = new(text);
                        if (OperationsLayout.Row(data, view, i) is { } row)
                        { b.Append(row.Entity == view.Selected ? "> " : "  "); b.Append(row.Name); b.Append(" / "); b.Append(row.Cause.Length > 0 ? row.Cause : row.Status); b.Append(" / Q "); b.Number(row.QueueCount, "0"); }
                        else b.Append(i == 0 ? "NO MATCHING FACILITIES" : "");
                        var facility = OperationsLayout.Row(data, view, i);
                        Control(context.Layout, i + 3, b.Written,
                            facility is { } active && active.Entity == view.Selected,
                            facility is { } blocked && blocked.Cause.Length > 0,
                            facility.HasValue, facility is { } f ? CategoryIcon(f.Category) : null);
                    }
                    var panel = _bounds; _line = 0;
                    _bounds = new(panel.X, panel.Y + 284 * _scale, panel.Width, 56 * _scale);
                    _skin.Panel(_bounds, "panel.inset");
                    OperationsFacility? selected = null;
                    for (int rowIndex = 0; rowIndex < data.Facilities.Count; rowIndex++)
                        if (data.Facilities[rowIndex].Entity == view.Selected) { selected = data.Facilities[rowIndex]; break; }
                    if (selected is { } detail)
                    {
                        Line(detail.Explanation);
                        b = new(text); b.Append("STOCK "); Number(ref b, detail.InventoryQuantity); b.Append(" / "); Number(ref b, detail.InventoryCapacity);
                        b.Append(" POWER "); Number(ref b, detail.AllocatedPower); b.Append(" / "); Number(ref b, detail.PowerDemand);
                        b.Append(" GENMAX "); Number(ref b, detail.GenerationCapacity);
                        b.Append(" LOAD "); Number(ref b, detail.Utilization, "P0"); Line(b.Written);
                        int routes = 0, unavailable = 0;
                        for (int routeIndex = 0; routeIndex < data.Routes.Count; routeIndex++)
                        {
                            var route = data.Routes[routeIndex];
                            if (route.Source == detail.Entity || route.Destination == detail.Entity)
                            { routes++; if (!route.Enabled) unavailable++; }
                        }
                        b = new(text); b.Append("LISTED LINKS "); b.Number(routes, "0"); b.Append(" DISABLED "); b.Number(unavailable, "0");
                        b.Append(" NODE CAP/S "); Number(ref b, detail.TransportCapacity); b.Append(" / FOCUS NEXT"); Line(b.Written);
                    }
                    else Line("SELECT A FACILITY FOR CAUSE AND CONTROLS");
                    _bounds = panel;
                    Control(context.Layout, 9, selected.HasValue ? "FOCUS FACILITY" : "SELECT FACILITY TO FOCUS", enabled: selected.HasValue);
                    bool controls = selected.HasValue && selected.Value.Controls != PlayerActionPanelMode.Closed;
                    Control(context.Layout, 10, controls ? "OPEN EXISTING CONTROLS" : "NO FACILITY CONTROLS", enabled: controls);
                }
            }
        }
        if (_count == 0 && _skin.Vertices == 0 && _text.VertexCount == 0) return;
        IGraphicsBuffer? frameBuffer = null;
        if (_count > 0)
        {
            if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
            { buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaxVertices * 24UL, GraphicsBufferMemory.Upload)); _buffers.Add(context.Graphics.FrameIndex, buffer); }
            buffer.SetData<OperationsVertex>(_vertices.AsSpan(0, _count));
            frameBuffer = buffer;
            if (_underlay > 0)
            { context.Graphics.SetPipeline(_pipeline); context.Graphics.SetVertexBuffer(buffer, 24); context.Graphics.Draw(_underlay); }
        }
        _skin.Flush(context.Graphics);
        if (frameBuffer is not null && _count > _underlay)
        { context.Graphics.SetPipeline(_pipeline); context.Graphics.SetVertexBuffer(frameBuffer, 24); context.Graphics.Draw(_count - _underlay, _underlay); }
        _text.Render(context.Graphics);
        LastRenderedVertexCount = _count + _text.VertexCount + _skin.Vertices;
    }
    private static void Number(ref OperationsTextBuilder builder, double? value, string format = "0")
    { if (value.HasValue && double.IsFinite(value.Value)) builder.Number(value.Value, format); else builder.Append("N/A"); }
    internal static RtsUiIcon CategoryIcon(OperationsCategory category) => category switch
    {
        OperationsCategory.Production => RtsUiIcon.BuildingFactory,
        OperationsCategory.Logistics => RtsUiIcon.UnitLogistics,
        OperationsCategory.Supply => RtsUiIcon.BuildingSupply,
        OperationsCategory.Power => RtsUiIcon.BuildingPower,
        _ => RtsUiIcon.StatusAlert
    };
    private void Control(in GameplayHudLayout layout, int index, ReadOnlySpan<char> label, bool selected = false, bool blocked = false,
        bool enabled = true, RtsUiIcon? icon = null)
    {
        var previous = _bounds; int line = _line;
        _bounds = OperationsLayout.Control(layout, index); _line = 0;
        if (_skin.Ready && label.IsEmpty) { _bounds = previous; _line = line; return; }
        if (!_skin.Control(_bounds, enabled, selected)) Quad(_bounds, selected ? GameplayHudVisualStyle.PanelSelected : GameplayHudVisualStyle.PanelRaised);
        if (blocked) _skin.State(_bounds, UiSkinState.Warning);
        if (selected || blocked)
        {
            var rail = new HudRect(_bounds.X, _bounds.Y, 2 * _scale, _bounds.Height);
            Quad(rail, blocked ? GameplayHudVisualStyle.Warning : GameplayHudVisualStyle.Focus);
            if (blocked) Quad(rail with { X = rail.X + 3 * _scale }, GameplayHudVisualStyle.Warning);
        }
        if (icon.HasValue && _skin.Icon(icon.Value, _bounds.X + 5 * _scale, _bounds.Y + 3 * _scale, 14 * _scale))
            _bounds = _bounds with { X = _bounds.X + 16 * _scale, Width = _bounds.Width - 16 * _scale };
        Line(label, true);
        _bounds = previous; _line = line;
    }
    private void Line(ReadOnlySpan<char> text, bool primary = false)
    {
        if (text.IsEmpty || _line >= MaxLines) return;
        float x = _bounds.X + 8 * _scale;
        float size = _panelText ? LastPanelTextSize : 12 * _scale;
        float leading = _panelText ? MathF.Max(15 * _minimumTextScale, 16 * _scale) : 14 * _scale;
        float y = _bounds.Y + (_panelText ? 4 : 2) * _scale + _line++ * leading;
        if (y + size > _bounds.Bottom - (_panelText ? 2 * _scale : 0)) return;
        int characters = Math.Min(68, Math.Max(0, (int)((_bounds.Width - 16 * _scale) / (6.4f * _scale))));
        var color = primary ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary;
        if (_text.Add(text, x, y, size, color, _width, _height,
            MathF.Max(0, _bounds.Right - 8f * _scale - x), bold: primary)) return;
        for (int i = 0; i < Math.Min(text.Length, characters); i++)
        {
            string pattern = SelectionInspectorHudRenderer.TextGlyphPattern(char.ToUpperInvariant(text[i]));
            if (pattern.Length != 35) continue;
            for (int pixel = 0; pixel < 35; pixel++)
                if (pattern[pixel] == '1')
                    Quad(new HudRect(x + (i * 6.4f + pixel % 5) * _scale,
                        y + pixel / 5 * _scale, _scale, _scale), color);
        }
    }

    private void Quad(HudRect rect, Vector4 color)
    {
        if (_count > MaxVertices - 6) return;
        var a = new Vector2(rect.X / _width * 2 - 1, 1 - rect.Y / _height * 2);
        var b = new Vector2(rect.Right / _width * 2 - 1, 1 - rect.Bottom / _height * 2);
        _vertices[_count++] = new(a, color);
        _vertices[_count++] = new(b, color);
        _vertices[_count++] = new(new Vector2(b.X, a.Y), color);
        _vertices[_count++] = new(a, color);
        _vertices[_count++] = new(new Vector2(a.X, b.Y), color);
        _vertices[_count++] = new(b, color);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var buffer in _buffers.Values) buffer.Dispose();
        _buffers.Clear();
        _text.Dispose();
        _skin.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OperationsVertex(Vector2 Position, Vector4 Color);

    private ref struct OperationsTextBuilder(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;
        private int _length;
        public readonly ReadOnlySpan<char> Written => _buffer[.._length];
        public void Append(string value)
        {
            int length = Math.Min(value.Length, _buffer.Length - _length);
            value.AsSpan(0, length).CopyTo(_buffer[_length..]);
            _length += length;
        }
        public void Number(double value, string format)
        {
            if (!double.IsFinite(value)) { Append("N/A"); return; }
            if (value.TryFormat(_buffer[_length..], out int written, format, CultureInfo.InvariantCulture)) _length += written;
        }
    }
}
