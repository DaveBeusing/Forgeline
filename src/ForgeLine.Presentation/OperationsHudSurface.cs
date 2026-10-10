using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class OperationsHudSurface : IGameplayHudSurface
{
    internal const int MaxVertices = 262_144;
    private const int MaxLines = 24;
    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly OperationsVertex[] _vertices = new OperationsVertex[MaxVertices];
    private int _count, _line, _width, _height;
    private HudRect _bounds;
    private float _scale;
    private bool _disposed;
    public OperationsHudSurface(IGraphicsDevice graphics)
    { _graphics = graphics; _pipeline = SelectionInspectorHudRenderer.CreatePipeline(graphics, alphaBlendEnabled: true); }
    public GameplayHudRegion Regions => GameplayHudRegion.SecondaryView | GameplayHudRegion.AlertStack;
    public int LastRenderedVertexCount { get; private set; }
    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0;
        _width = context.Graphics.Width; _height = context.Graphics.Height;
        if (_width <= 0 || _height <= 0) return;
        _count = 0; _line = 0;
        _scale = MathF.Min(context.Layout.Scale, OperationsLayout.Entry(context.Layout).Width / 126);
        _bounds = OperationsLayout.Entry(context.Layout);
        Quad(_bounds, GameplayHudVisualStyle.PanelBackground);
        Line("OPERATIONS", true);
        var view = context.Operations;
        if (view.Open && !view.Suppressed && view.Session == context.Snapshot.SessionId)
        {
            _scale = OperationsLayout.Scale(context.Layout);
            _bounds = OperationsLayout.Panel(context.Layout); _line = 0;
            if (_scale > 0)
            {
                Quad(_bounds, GameplayHudVisualStyle.PanelBackground);
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
                        Control(context.Layout, i + 3, b.Written);
                    }
                    var panel = _bounds; _line = 0;
                    _bounds = new(panel.X, panel.Y + 264 * _scale, panel.Width, 52 * _scale);
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
                    Control(context.Layout, 9, selected.HasValue ? "FOCUS FACILITY" : "SELECT FACILITY TO FOCUS");
                    Control(context.Layout, 10, selected.HasValue && selected.Value.Controls != PlayerActionPanelMode.Closed ? "OPEN EXISTING CONTROLS" : "NO FACILITY CONTROLS");
                }
            }
        }
        if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
        { buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaxVertices * 24UL, GraphicsBufferMemory.Upload)); _buffers.Add(context.Graphics.FrameIndex, buffer); }
        buffer.SetData<OperationsVertex>(_vertices.AsSpan(0, _count));
        context.Graphics.SetPipeline(_pipeline); context.Graphics.SetVertexBuffer(buffer, 24); context.Graphics.Draw(_count);
        LastRenderedVertexCount = _count;
    }
    private static void Number(ref OperationsTextBuilder builder, double? value, string format = "0")
    { if (value.HasValue) builder.Number(value.Value, format); else builder.Append("N/A"); }
    private void Control(in GameplayHudLayout layout, int index, ReadOnlySpan<char> label)
    {
        var previous = _bounds; int line = _line;
        _bounds = OperationsLayout.Control(layout, index); _line = 0;
        Quad(_bounds, GameplayHudVisualStyle.PanelBackground); Line(label, true);
        _bounds = previous; _line = line;
    }
    private void Line(ReadOnlySpan<char> text, bool primary = false)
    {
        if (text.IsEmpty || _line >= MaxLines) return;
        float x = _bounds.X + 8 * _scale;
        float y = _bounds.Y + (4 + _line++ * 14) * _scale;
        if (y + 7 * _scale > _bounds.Bottom - 2 * _scale) return;
        int characters = Math.Min(68, Math.Max(0, (int)((_bounds.Width - 16 * _scale) / (6.4f * _scale))));
        var color = primary ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary;
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
            if (value.TryFormat(_buffer[_length..], out int written, format, CultureInfo.InvariantCulture)) _length += written;
        }
    }
}
