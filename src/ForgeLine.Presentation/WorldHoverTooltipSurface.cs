using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class WorldHoverTooltipSurface : IGameplayHudSurface
{
    internal const int MaxVertices = 131_072;
    private const int MaxLines = 12;
    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly TooltipVertex[] _vertices = new TooltipVertex[MaxVertices];
    private int _count;
    private int _line;
    private HudRect _bounds;
    private float _scale;
    private int _width, _height;
    private bool _disposed;

    public WorldHoverTooltipSurface(IGraphicsDevice graphics)
    {
        _graphics = graphics;
        _pipeline = SelectionInspectorHudRenderer.CreatePipeline(graphics, alphaBlendEnabled: true);
    }

    public GameplayHudRegion Regions => GameplayHudRegion.GlobalOverlay;
    public int LastRenderedVertexCount { get; private set; }
    internal HudRect LastBounds { get; private set; }

    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0;
        LastBounds = default;
        var view = context.HoverTooltip;
        if (context.Graphics.Width != view.ViewportWidth || context.Graphics.Height != view.ViewportHeight ||
            context.Layout.Scale != view.Scale || context.InformationLayer.IsDragSelecting ||
            context.TacticalTargeting.Mode != TacticalTargetingMode.None) return;
        var content = HoverTooltipResolver.Resolve(context.Snapshot, view, context.ActionPanel);
        if (content is not { } resolved) return;
        _width = context.Graphics.Width;
        _height = context.Graphics.Height;
        _scale = context.Layout.Scale;
        if (_width <= 0 || _height <= 0) return;
        _count = 0;
        _line = 0;
        // Reserve a bounded twelve-line panel; use the measured line count for its tight final bounds.
        int lines = CountLines(resolved);
        _bounds = HoverTooltipPlacement.Resolve(view.PointerPosition, context.Layout,
            330 * _scale, (16 + lines * 14) * _scale);
        if (_bounds.IsEmpty) return;
        Quad(_bounds, GameplayHudVisualStyle.PanelBackground);
        Quad(new HudRect(_bounds.X, _bounds.Y, MathF.Min(2 * _scale, _bounds.Width), _bounds.Height), GameplayHudVisualStyle.TextPrimary);
        Line(resolved.Title, true);
        Line(resolved.Role);
        Line(resolved.Status);
        if (resolved.World is { } world) EmitWorld(world);
        if (resolved.Costs is { } costs) EmitAmounts(costs, "NEED");
        if (resolved.Outputs is { } outputs) EmitAmounts(outputs, "OUT");
        if (resolved.ProductionTicks is { } ticks)
        {
            Span<char> text = stackalloc char[128];
            var builder = new TooltipTextBuilder(text);
            builder.Append("PRODUCTION: "); builder.Number(ticks, "0"); builder.Append(" TICKS");
            Line(builder.Written);
        }
        Line(resolved.Requirement);
        if (resolved.Prerequisites is { } prerequisites)
        {
            Span<char> text = stackalloc char[160];
            for (int i = 0; i < Math.Min(prerequisites.Count, 2); i++)
            {
                var builder = new TooltipTextBuilder(text);
                builder.Append(prerequisites[i].Completed ? "DONE: " : "REQUIRES: ");
                builder.Append(prerequisites[i].DisplayName);
                Line(builder.Written);
            }
        }
        Line(resolved.Hint);
        if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
        {
            buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaxVertices * 24UL, GraphicsBufferMemory.Upload));
            _buffers.Add(context.Graphics.FrameIndex, buffer);
        }
        buffer.SetData<TooltipVertex>(_vertices.AsSpan(0, _count));
        context.Graphics.SetPipeline(_pipeline);
        context.Graphics.SetVertexBuffer(buffer, 24);
        context.Graphics.Draw(_count);
        LastRenderedVertexCount = _count;
        LastBounds = _bounds;
    }

    private static int CountLines(in HoverTooltipContent content)
    {
        int count = 2 + (content.Status.Length > 0 ? 1 : 0) + (content.Hint.Length > 0 ? 1 : 0) +
            (content.Requirement.Length > 0 ? 1 : 0) + (content.ProductionTicks.HasValue ? 1 : 0);
        if (content.World is { } world)
        {
            var details = world.OwnedDetails;
            count += (details.HasHealth ? 1 : 0) + (details.HasSupply ? 2 : 0) +
                (details.HasReadiness ? 1 : 0) + (details.HasPower ? 1 : 0) +
                (details.Work.Kind != PlayerWorkKind.None ? 2 : 0) +
                (world.RemainingQuantity.HasValue ? 1 : 0) + (world.Requirement.Length > 0 ? 1 : 0);
        }
        count += Math.Min(content.Costs?.Count ?? 0, 3) + Math.Min(content.Outputs?.Count ?? 0, 2);
        count += Math.Min(content.Prerequisites?.Count ?? 0, 2);
        return Math.Min(count, MaxLines);
    }

    private void EmitWorld(in PlayerHoverSummary world)
    {
        var details = world.OwnedDetails;
        Span<char> text = stackalloc char[160];
        if (details.HasHealth) Percent("HEALTH", details.HealthFraction);
        if (details.HasSupply)
        {
            Line(SelectionInspectorHudModel.ResolveSupplyLabel(details.SupplyStatus));
            var builder = new TooltipTextBuilder(text);
            builder.Append("FUEL "); builder.Number(details.FuelFraction, "P0");
            builder.Append(" / AMMO "); builder.Number(details.AmmunitionFraction, "P0");
            Line(builder.Written);
        }
        if (details.HasReadiness) Percent("READINESS", details.Readiness);
        if (details.HasPower) Line(SelectionInspectorHudModel.ResolvePowerLabel(details.PowerState));
        if (details.Work.Kind != PlayerWorkKind.None)
        {
            var builder = new TooltipTextBuilder(text);
            builder.Append(details.Work.Activity); builder.Append(" "); builder.Number(details.Work.Progress, "P0");
            Line(builder.Written);
            Line(details.Work.State == PlayerWorkState.Blocked ? details.Work.BlockReason :
                SelectionInspectorHudModel.ResolveWorkStateLabel(details.Work.State));
        }
        if (world.RemainingQuantity is { } remaining)
        {
            var builder = new TooltipTextBuilder(text);
            builder.Append("REMAINING: "); builder.Number(remaining, "N0");
            Line(builder.Written);
        }
        if (world.Requirement.Length > 0)
        {
            var builder = new TooltipTextBuilder(text);
            builder.Append("REQUIRES: "); builder.Append(world.Requirement);
            Line(builder.Written);
        }
    }

    private void Percent(string label, double value)
    {
        Span<char> text = stackalloc char[64];
        var builder = new TooltipTextBuilder(text);
        builder.Append(label); builder.Append(": "); builder.Number(value, "P0");
        Line(builder.Written);
    }

    private void EmitAmounts(IReadOnlyList<PlayerActionResourceAmount> amounts, string label)
    {
        Span<char> text = stackalloc char[160];
        int maximum = label == "OUT" ? 2 : 3;
        for (int i = 0; i < Math.Min(amounts.Count, maximum); i++)
        {
            var amount = amounts[i];
            var builder = new TooltipTextBuilder(text);
            builder.Append(label); builder.Append(" "); builder.Append(amount.DisplayName); builder.Append(": ");
            builder.Number(amount.RequiredQuantity, "N0"); builder.Append(" / "); builder.Number(amount.AvailableQuantity, "N0");
            Line(builder.Written);
        }
    }

    private void Line(ReadOnlySpan<char> text, bool primary = false)
    {
        if (text.IsEmpty || _line >= MaxLines) return;
        float x = _bounds.X + 8 * _scale;
        float y = _bounds.Y + (8 + _line++ * 14) * _scale;
        if (y + 7 * _scale > _bounds.Bottom - 4 * _scale) return;
        int characters = Math.Min(48, Math.Max(0, (int)((_bounds.Width - 16 * _scale) / (6.4f * _scale))));
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
    private readonly record struct TooltipVertex(Vector2 Position, Vector4 Color);

    private ref struct TooltipTextBuilder(Span<char> buffer)
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
