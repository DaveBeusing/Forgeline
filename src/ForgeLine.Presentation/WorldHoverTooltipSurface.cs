using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Game;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class WorldHoverTooltipSurface : IGameplayHudSurface
{
    internal const int MaxVertices = 131_072;
    private const int MaxLines = 12;
    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly GameplayHudSkin _skin;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly TooltipVertex[] _vertices = new TooltipVertex[MaxVertices];
    private int _count;
    private int _line;
    private bool _wrapDetails, _measuring;
    private HudRect _bounds;
    private float _scale;
    private int _width, _height;
    private bool _disposed;

    public WorldHoverTooltipSurface(IGraphicsDevice graphics, RuntimeAssetCatalog? runtimeAssets = null)
    {
        _graphics = graphics;
        _pipeline = HudOverlayPrimitives.CreatePipeline(graphics, alphaBlendEnabled: true);
        _text = new UiTextBatch(graphics);
        try { _skin = new(graphics, runtimeAssets); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }

    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public GameplayHudRegion Regions => GameplayHudRegion.GlobalOverlay;
    public int LastRenderedVertexCount { get; private set; }
    internal HudRect LastBounds { get; private set; }
    internal int LastLayoutLineCount { get; private set; }

    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _text.Begin();
        if (context.Graphics.Width > 0 && context.Graphics.Height > 0) _skin.Begin(context.Graphics);
        LastRenderedVertexCount = 0;
        LastBounds = default;
        LastLayoutLineCount = 0;
        var view = context.HoverTooltip;
        if (context.Graphics.Width != view.ViewportWidth || context.Graphics.Height != view.ViewportHeight ||
            context.Layout.Scale != view.Scale || context.InformationLayer.IsDragSelecting ||
            context.TacticalTargeting.Mode != TacticalTargetingMode.None) return;
        var content = HoverTooltipResolver.Resolve(context.Snapshot, view, context.ActionPanel,
            context.CombatGroups.Selection, context.ActiveFormation);
        if (content is not { } resolved) return;
        _width = context.Graphics.Width;
        _height = context.Graphics.Height;
        _scale = view.SelectionStatus ? Math.Clamp(context.Layout.Scale, 1, 1.25f) : context.Layout.Scale;
        if (_width <= 0 || _height <= 0) return;
        _count = 0;
        _line = 0;
        // Logistics details use the same bounded word layout for measurement and drawing.
        _wrapDetails = resolved.WrapDetails;
        _measuring = true;
        EmitContent(resolved);
        int lines = _line;
        LastLayoutLineCount = lines;
        _line = 0;
        _measuring = false;
        _scale = MathF.Min(_scale, MathF.Min(context.Layout.SafeArea.Width / 350,
            context.Layout.SafeArea.Height / (16 + lines * 14)));
        if (_wrapDetails && !view.SelectionStatus)
        {
            var safe = context.Layout.SafeArea;
            float gap = 18 * context.Layout.Scale;
            float besidePointer = MathF.Max(MathF.Max(0, safe.Bottom - view.PointerPosition.Y - gap),
                MathF.Max(0, view.PointerPosition.Y - gap - safe.Y));
            _scale = MathF.Min(_scale, besidePointer / (16 + lines * 14));
            if (_scale <= 0) return;
        }
        if(view.SelectionStatus)
        {
            Span<HudRect> occupied=stackalloc HudRect[8];
            var notifications=ActionableAlertLayout.Resolve(context.Snapshot,context.Layout,
                localFeedback:context.PreAlphaUx.Placement.Visible ? "" : context.PreAlphaUx.InteractionHint);
            RadialActionLayout.ResolveObstacles(occupied,context.Layout,context.PreAlphaUx.Onboarding,context.Operations,
                context.CombatGroups,notifications);
            float preferred=_scale;
            for(int attempt=0;attempt<5;attempt++) {
                _scale=MathF.Max(MathF.Min(.75f,preferred),preferred-attempt*.125f);
                _bounds=HoverTooltipPlacement.BesideRing(context.ActionPanel.Ring,context.Layout,occupied,
                    330*_scale,(16+lines*14)*_scale);
                if(!_bounds.IsEmpty) break;
            }
        }
        else _bounds=HoverTooltipPlacement.Resolve(view.PointerPosition,context.Layout,330*_scale,(16+lines*14)*_scale);
        if (_bounds.IsEmpty) return;
        if (!_skin.Panel(_bounds, "panel.tooltip")) Quad(_bounds, GameplayHudVisualStyle.PanelBackground);
        _skin.State(_bounds, resolved.VisualState);
        if (!_skin.Ready) Quad(new HudRect(_bounds.X, _bounds.Y, _bounds.Width, 25 * _scale), GameplayHudVisualStyle.PanelHeader);
        Quad(new HudRect(_bounds.X, _bounds.Bottom - _scale, _bounds.Width, _scale), GameplayHudVisualStyle.QuietBorder);
        Quad(new HudRect(_bounds.X, _bounds.Y, MathF.Min(2 * _scale, _bounds.Width), _bounds.Height), GameplayHudVisualStyle.TextPrimary);
        EmitContent(resolved);
        if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
        {
            buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaxVertices * 24UL, GraphicsBufferMemory.Upload));
            _buffers.Add(context.Graphics.FrameIndex, buffer);
        }
        _skin.Flush(context.Graphics);
        buffer.SetData<TooltipVertex>(_vertices.AsSpan(0, _count));
        context.Graphics.SetPipeline(_pipeline);
        context.Graphics.SetVertexBuffer(buffer, 24);
        context.Graphics.Draw(_count);
        _text.Render(context.Graphics);
        LastRenderedVertexCount = _count + _text.VertexCount + _skin.Vertices;
        LastBounds = _bounds;
    }

    private void EmitContent(in HoverTooltipContent resolved)
    {
        Line(resolved.Title, true);
        Line(resolved.Role);
        Line(resolved.Status);
        Line(resolved.WorkStatus);
        Line(resolved.Explanation);
        if (resolved.Selection is { } selection) EmitSelectionStatus(selection, resolved.Group, resolved.Formation);
        if (resolved.Feedback is { State: not PlayerCommandFeedbackState.None } feedback) EmitCommandFeedback(feedback);
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
    }

    private void EmitSelectionStatus(in PlayerSelectionSummary selection, SelectedCombatGroup? group, FormationTemplate formation)
    {
        Span<char> buffer = stackalloc char[256];
        var text = new TooltipTextBuilder(buffer);
        text.Append("SELECTED "); text.Number(selection.Count, "0");
        if (group is not null)
        {
            text.Append(" / LIVE "); text.Number(group.LiveCount, "0");
            text.Append(" / COMBAT "); text.Number(group.CombatCount, "0");
            Line(text.Written);
            Covered("AVG HEALTH", group.Health, group.HealthCount);
            Covered("AVG READINESS", group.Readiness, group.ReadinessCount);
            Covered("AVG FUEL", group.Fuel, group.SupplyCount);
            Covered("AVG AMMUNITION", group.Ammunition, group.SupplyCount);
            for (int row = 0; row < 2; row++)
            {
                text = new(buffer);
                for (int index = row * 4; index < row * 4 + 4; index++)
                {
                    if (index % 4 != 0) text.Append(" / ");
                    text.Append(SelectedCombatGroup.TypeLabel(index)); text.Append(" "); text.Number(group.Composition(index), "0");
                }
                Line(text.Written);
            }
            text = new(buffer); text.Append("DAMAGED "); text.Number(group.DamagedCount, "0");
            text.Append(" / UNSUPPLIED "); text.Number(group.UnsuppliedCount, "0"); Line(text.Written);
            Line(formation switch { FormationTemplate.Line => "FORMATION LINE", FormationTemplate.Column => "FORMATION COLUMN",
                FormationTemplate.Wedge => "FORMATION WEDGE", _ => "FORMATION COMPACT" });
            return;
        }
        Line(text.Written);
        if (!selection.HasSingleEntityDetails) return;
        var details = new PlayerHoverSummary(selection.PrimaryEntity, default, default, default,
            selection.DisplayName, default, selection);
        EmitWorld(details, includeWorkState: true);
        if (selection.HasInventory)
        {
            text = new(buffer); text.Append("LOCAL INVENTORY "); text.Number(selection.InventoryQuantity, "N0"); Line(text.Written);
        }
    }

    private void Covered(string label, double value, int count)
    {
        Span<char> buffer = stackalloc char[128]; var text = new TooltipTextBuilder(buffer);
        text.Append(label); text.Append(" ");
        if (count == 0) text.Append("UNAVAILABLE"); else text.Number(value, "P0");
        text.Append(" / KNOWN "); text.Number(count, "0"); Line(text.Written);
    }

    private void EmitCommandFeedback(in PlayerCommandFeedback feedback)
    {
        Span<char> buffer = stackalloc char[192]; var text = new TooltipTextBuilder(buffer);
        text.Append("LAST "); text.Append(PlayerActionDockHudModel.ResolveFeedbackStateLabel(feedback.State));
        text.Append(" / OK "); text.Number(feedback.AcceptedTargets, "0");
        text.Append(" / REJECTED "); text.Number(feedback.RejectedTargets, "0"); Line(text.Written);
        Line(PlayerActionDockHudModel.ResolveCommandFeedbackReason(feedback));
    }

    private void EmitWorld(in PlayerHoverSummary world, bool includeWorkState = false)
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
            if(includeWorkState)
            {
                Line(SelectionInspectorHudModel.ResolveWorkStateLabel(details.Work.State));
                if(!string.IsNullOrWhiteSpace(details.Work.BlockReason)) Line(details.Work.BlockReason);
            }
            else Line(details.Work.State == PlayerWorkState.Blocked ? details.Work.BlockReason :
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
        int maximum = _wrapDetails ? 7 : label == "OUT" ? 2 : 3;
        for (int i = 0; i < Math.Min(amounts.Count, maximum); i++)
        {
            var amount = amounts[i];
            var builder = new TooltipTextBuilder(text);
            builder.Append(label); builder.Append(" "); builder.Append(amount.DisplayName); builder.Append(": ");
            builder.Number(amount.RequiredQuantity, "N0"); builder.Append(" / "); builder.Number(amount.AvailableQuantity, "N0");
            Line(builder.Written);
        }
    }

    internal static int WrappedPrefix(ReadOnlySpan<char> text, bool primary, bool nativeText)
    {
        int fit = nativeText ? UiFontMetrics.Default.FitPrefix(text, 312, UiTypography.Style(UiTextRole.Tooltip).Size, primary) : Math.Min(48, text.Length);
        if (fit >= text.Length) return text.Length;
        int space = text[..Math.Max(0, fit)].LastIndexOf(' ');
        return Math.Max(1, space > 0 ? space : fit);
    }

    private void Line(ReadOnlySpan<char> text, bool primary = false)
    {
        if (!_wrapDetails) { SingleLine(text, primary); return; }
        while (!text.IsEmpty && _line < 48)
        {
            int length = WrappedPrefix(text, primary, _text.Ready);
            SingleLine(text[..length], primary);
            text = text[length..].TrimStart();
        }
    }

    private void SingleLine(ReadOnlySpan<char> text, bool primary)
    {
        if (text.IsEmpty || _line >= (_wrapDetails ? 48 : MaxLines)) return;
        if (_measuring) { _line++; return; }
        float x = _bounds.X + 8 * _scale;
        float y = _bounds.Y + (8 + _line++ * 14) * _scale;
        if (y + 7 * _scale > _bounds.Bottom - 4 * _scale) return;
        int characters = Math.Min(48, Math.Max(0, (int)((_bounds.Width - 16 * _scale) / (6.4f * _scale))));
        var color = primary ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary;
        if (_text.Add(text, x, y, UiTypography.Style(UiTextRole.Tooltip).Size * _scale, color, _width, _height,
            _bounds.Right - 8 * _scale - x, primary)) return;
        for (int i = 0; i < Math.Min(text.Length, characters); i++)
        {
            string pattern = HudOverlayPrimitives.TextGlyphPattern(char.ToUpperInvariant(text[i]));
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
        _skin.Dispose();
        _text.Dispose();
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
