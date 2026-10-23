using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

/// <summary>Passive native-resolution annotations in the existing HUD pass and text batch.</summary>
internal sealed class OperationsWorldOverlaySurface : IGameplayHudSurface
{
    private const int MaximumVertices = 4096;
    private const int MaximumLabels = 12;
    private readonly OperationsWorldOverlay _overlay = new();
    private readonly OverlayVertex[] _vertices = new OverlayVertex[MaximumVertices];
    private readonly HudRect[] _labels = new HudRect[MaximumLabels];
    private readonly HudRect[] _linkLabels = new HudRect[4];
    private readonly Vector2[] _glyphs = new Vector2[OperationsWorldOverlay.MaximumMarkers];
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly UiTextBatch _text;
    private int _count, _width, _height;
    private bool _disposed;

    public OperationsWorldOverlaySurface(IGraphicsDevice graphics)
    {
        _graphics = graphics;
        _text = new UiTextBatch(graphics);
        try { _pipeline = HudOverlayPrimitives.CreatePipeline(graphics, alphaBlendEnabled: true); }
        catch { _text.Dispose(); throw; }
    }

    public GameplayHudRegion Regions => GameplayHudRegion.GlobalOverlay;
    public int LastRenderedVertexCount { get; private set; }
    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;

    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0;
        _count = 0; _text.Begin();
        _width = context.Graphics.Width; _height = context.Graphics.Height;
        _overlay.Build(context);
        if (_overlay.Markers.IsEmpty && _overlay.Links.IsEmpty) return;
        float scale = Math.Clamp(context.Layout.Scale, .7f, 1.75f);
        Span<HudRect> occupied = stackalloc HudRect[8];
        RadialActionLayout.ResolveObstacles(occupied, context.Layout, context.PreAlphaUx.Onboarding,
            context.Operations, context.CombatGroups);
        var ring = context.ActionPanel.Ring.Geometry;
        int linkLabelCount = 0;
        // No hit regions are added. The existing controls are drawn afterwards and keep input ownership.
        foreach (ref readonly var link in _overlay.Links)
        {
            Vector4 color = !link.Enabled ? new(.95f, .38f, .25f, .6f) :
                link.Loaded ? new(.35f, .88f, .7f, .65f) : link.Request ? new(.95f, .74f, .3f, .6f) :
                new(.58f, .72f, .78f, .32f);
            Vector2 delta = link.End - link.Start;
            float length = delta.Length();
            if (!float.IsFinite(length) || length < 8 * scale) continue;
            if (link.Loaded) Stroke(link.Start, link.End, color, scale, context.Layout.SafeArea, occupied, ring);
            else
                for (int dash = 0; dash < 4; dash++)
                    Stroke(link.Start + delta * (dash / 4f), link.Start + delta * ((dash + .55f) / 4f),
                        color, scale, context.Layout.SafeArea, occupied, ring);
            Vector2 direction = delta / length;
            Vector2 side = new(-direction.Y, direction.X);
            Vector2 tail = link.End - direction * 5 * scale;
            Stroke(link.End, tail + side * 3 * scale, color, scale, context.Layout.SafeArea, occupied, ring);
            Stroke(link.End, tail - side * 3 * scale, color, scale, context.Layout.SafeArea, occupied, ring);
            if (linkLabelCount < _linkLabels.Length)
            {
                Vector2 middle = (link.Start + link.End) * .5f;
                var bounds = new HudRect(middle.X, middle.Y, 148 * scale, 18 * scale);
                bool available = Available(bounds, context.Layout.SafeArea, occupied, ring);
                for (int i = 0; i < linkLabelCount; i++)
                    if (bounds.Intersects(_linkLabels[i])) available = false;
                if (available)
                {
                    _linkLabels[linkLabelCount++] = bounds;
                    Quad(bounds, new(.035f, .05f, .055f, .87f));
                    _text.Add(!link.Enabled ? "DISABLED LINK" : link.Loaded ? "LOADED CARGO" :
                        link.Request ? "TRANSPORT REQUEST" : "NETWORK LINK", bounds.X + 3 * scale,
                        bounds.Y + 2 * scale, 11 * scale, color with { W = 1 }, _width, _height, bounds.Width - 6 * scale);
                }
            }
        }
        int labelCount = 0, glyphCount = 0;
        int labelLimit = context.Camera.Distance >= 600 ? 4 : MaximumLabels;
        Span<char> caption = stackalloc char[160];
        foreach (ref readonly var marker in _overlay.Markers)
        {
            bool crowded = false;
            for (int i = 0; i < glyphCount; i++)
                if (Vector2.DistanceSquared(marker.Position, _glyphs[i]) < 100 * scale * scale)
                { crowded = true; break; }
            var p = marker.Position;
            var glyph = new HudRect(p.X - 5 * scale, p.Y - 5 * scale, 10 * scale, 10 * scale);
            if (crowded || !Available(glyph, context.Layout.SafeArea, occupied, ring)) continue;
            _glyphs[glyphCount++] = p;
            bool blocked = marker.Diagnosis is { PrimaryReason: not OperationsDiagnosisReason.None } ||
                marker.Facility is { Cause.Length: > 0 };
            Vector4 color = blocked ? GameplayHudVisualStyle.Warning : new(.55f, .85f, .9f, .85f);
            Quad(new(p.X - 5 * scale, p.Y - 5 * scale, 2 * scale, 10 * scale), color);
            Quad(new(p.X + 3 * scale, p.Y - 5 * scale, 2 * scale, 10 * scale), color);
            Quad(new(p.X - 3 * scale, p.Y - 5 * scale, 6 * scale, 2 * scale), color);
            Quad(new(p.X - 3 * scale, p.Y + 3 * scale, 6 * scale, 2 * scale), color);
            if (labelCount == labelLimit) continue;
            var bounds = new HudRect(p.X + 9 * scale, p.Y - 18 * scale, 224 * scale, 42 * scale);
            if (!Available(bounds, context.Layout.SafeArea, occupied, ring)) continue;
            for (int i = 0; i < labelCount; i++)
                if (_labels[i].Intersects(bounds)) { crowded = true; break; }
            for (int i = 0; i < linkLabelCount; i++)
                if (_linkLabels[i].Intersects(bounds)) { crowded = true; break; }
            if (crowded) continue;
            _labels[labelCount++] = bounds;
            Quad(bounds, new(.035f, .05f, .055f, .87f));
            _text.Add(marker.Facility?.Name ?? "OPERATIONS", bounds.X + 5 * scale, bounds.Y + 3 * scale,
                13 * scale, color, _width, _height, bounds.Width - 10 * scale, bold: true);
            int count = Caption(marker, context.Snapshot.Operations, caption);
            _text.Add(caption[..count], bounds.X + 5 * scale, bounds.Y + 22 * scale,
                12 * scale, GameplayHudVisualStyle.TextPrimary, _width, _height, bounds.Width - 10 * scale);
        }
        if (_count > 0)
        {
            if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
            {
                buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaximumVertices * 24UL, GraphicsBufferMemory.Upload));
                _buffers.Add(context.Graphics.FrameIndex, buffer);
            }
            buffer.SetData<OverlayVertex>(_vertices.AsSpan(0, _count));
            context.Graphics.SetPipeline(_pipeline);
            context.Graphics.SetVertexBuffer(buffer, 24);
            context.Graphics.Draw(_count);
        }
        _text.Render(context.Graphics);
        LastRenderedVertexCount = _count + _text.VertexCount;
    }

    private static int Caption(in OperationsWorldMarker marker, OperationsSnapshot? operations, Span<char> text)
    {
        var caption = new CaptionBuilder(text);
        if (marker.Diagnosis is { } row)
        {
            if (operations is not null)
                for (int i = 0; i < operations.Resources.Count; i++)
                {
                    var resource = operations.Resources[i];
                    if (resource.Resource == row.Resource)
                    { caption.Append(resource.Name); caption.Append(": "); break; }
                }
            caption.Append(row.PrimaryReason != OperationsDiagnosisReason.None
                ? OperationsDiagnosisSnapshot.Label(row.PrimaryReason) : row.Status);
            caption.Number(row.Subject == OperationsDiagnosisSubject.CargoTransport ? " / ON BOARD " : " / LOAD ",
                row.LoadedCargoQuantity);
            caption.Number(" / STOCK ", row.LocalQuantity);
            caption.Number(" / REQUEST ", row.OutstandingRequestQuantity);
            caption.Number(" / CONFIRMED ", row.ConfirmedIncomingQuantity);
            caption.Number(" / QUEUE ", row.DockQueuePosition);
        }
        else if (marker.Facility is { } facility)
        {
            caption.Append(facility.Cause.Length > 0 ? facility.Cause : facility.Status);
            caption.Number(" / STOCK ", facility.InventoryQuantity);
            caption.Number(" / QUEUE ", facility.Service.VehicleQueue?.Position ?? facility.Service.LoadingQueue?.WaitingCount);
        }
        return caption.Length;
    }

    private static bool Available(in HudRect bounds, in HudRect safe, ReadOnlySpan<HudRect> occupied, in RadialActionLayout ring)
    {
        if (!safe.Contains(new(bounds.X, bounds.Y)) || !safe.Contains(new(bounds.Right, bounds.Bottom))) return false;
        foreach (var obstacle in occupied) if (bounds.Intersects(obstacle)) return false;
        if (ring.IsValid)
            for (int i = 0; i < ring.Count; i++) if (bounds.Intersects(ring.SectorBounds(i))) return false;
        return true;
    }

    private void Stroke(Vector2 start, Vector2 end, Vector4 color, float scale, in HudRect safe,
        ReadOnlySpan<HudRect> occupied, in RadialActionLayout ring)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length <= .01f || !float.IsFinite(length) || _count > MaximumVertices - 6) return;
        Vector2 normal = new Vector2(-delta.Y, delta.X) / length * scale * .65f;
        Vector2 min = Vector2.Min(start, end) - new Vector2(scale);
        Vector2 max = Vector2.Max(start, end) + new Vector2(scale);
        if (!Available(new(min.X, min.Y, max.X - min.X, max.Y - min.Y), safe, occupied, ring)) return;
        Triangle(start - normal, start + normal, end + normal, color);
        Triangle(start - normal, end + normal, end - normal, color);
    }

    private void Quad(in HudRect bounds, Vector4 color)
    {
        if (_count > MaximumVertices - 6) return;
        Vector2 a = new(bounds.X, bounds.Y), b = new(bounds.Right, bounds.Bottom);
        Triangle(a, b, new(b.X, a.Y), color);
        Triangle(a, new(a.X, b.Y), b, color);
    }

    private void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        _vertices[_count++] = new(Normalize(a), color);
        _vertices[_count++] = new(Normalize(b), color);
        _vertices[_count++] = new(Normalize(c), color);
    }

    private Vector2 Normalize(Vector2 point) => new(point.X / _width * 2 - 1, 1 - point.Y / _height * 2);

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var buffer in _buffers.Values) buffer.Dispose();
        _text.Dispose(); _pipeline.Dispose(); _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(Vector2 Position, Vector4 Color);

    private ref struct CaptionBuilder(Span<char> text)
    {
        private readonly Span<char> _text = text;
        public int Length { get; private set; }
        public void Append(ReadOnlySpan<char> value)
        {
            int count = Math.Min(value.Length, _text.Length - Length);
            value[..count].CopyTo(_text[Length..]); Length += count;
        }
        public void Number(string label, double? value)
        {
            if (!value.HasValue) return;
            Append(label);
            if (double.IsFinite(value.Value) && value.Value.TryFormat(_text[Length..], out int count, "0.#", CultureInfo.InvariantCulture))
                Length += count;
            else Append("N/A");
        }
    }
}
