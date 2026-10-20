using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal enum HudAlertSeverity : byte
{
    Information = 0,
    Warning = 1,
    Critical = 2
}

internal static class ResourcePowerHudModel
{
    public const int AuthoritativeResourceCount = 7;
    public const ulong CommandFeedbackLifetimeTicks = 80;

    public static bool IsCommandFeedbackVisible(
        in PlayerExperienceSnapshot experience)
    {
        PlayerCommandFeedback feedback =
            experience.Feedback;

        return feedback.Kind !=
                   PlayerCommandFeedbackKind.None &&
               feedback.State !=
                   PlayerCommandFeedbackState.None &&
               experience.Tick.Value >=
                   feedback.ResolvedAtTick.Value &&
               experience.Tick.Value -
                   feedback.ResolvedAtTick.Value <=
                   CommandFeedbackLifetimeTicks;
    }

    public static string ResolvePowerStateLabel(
        in PlayerPowerSummary power) =>
        !double.IsFinite(power.Generation) || !double.IsFinite(power.Demand) ? "UNAVAILABLE" :
        power.IsConstrained
            ? "CONSTRAINED"
            : "STABLE";

    public static int ResolveNotificationCount(
        in PlayerExperienceSnapshot experience) =>
        CountActiveAlerts(
            experience.Alerts) +
        (IsCommandFeedbackVisible(
            experience)
            ? 1
            : 0);

    public static HudAlertSeverity ResolveAlertSeverity(
        PlayerAlertState alert) =>
        alert switch
        {
            PlayerAlertState.CommandCoreDestroyed =>
                HudAlertSeverity.Critical,
            PlayerAlertState.CommandCoreDamaged =>
                HudAlertSeverity.Critical,
            PlayerAlertState.SupplyCritical =>
                HudAlertSeverity.Critical,
            PlayerAlertState.ProductionBlocked =>
                HudAlertSeverity.Warning,
            PlayerAlertState.LowPower =>
                HudAlertSeverity.Warning,
            _ =>
                HudAlertSeverity.Information
        };

    public static int CountActiveAlerts(
        PlayerAlertState alerts)
    {
        int count = 0;

        if (HasAlert(
                alerts,
                PlayerAlertState.LowPower))
        {
            count++;
        }

        if (HasAlert(
                alerts,
                PlayerAlertState.ProductionBlocked))
        {
            count++;
        }

        if (HasAlert(
                alerts,
                PlayerAlertState.SupplyCritical))
        {
            count++;
        }

        if (HasAlert(
                alerts,
                PlayerAlertState.CommandCoreDamaged))
        {
            count++;
        }

        if (HasAlert(
                alerts,
                PlayerAlertState.CommandCoreDestroyed))
        {
            count++;
        }

        return count;
    }

    public static bool HasAlert(
        PlayerAlertState alerts,
        PlayerAlertState alert) =>
        (alerts & alert) !=
        PlayerAlertState.None;

    public static string GetResourceLabel(int index) => index switch
    {
        0 => "Ferrous Ore", 1 => "Volatiles", 2 => "Silicates", 3 => "Steel",
        4 => "Fuel", 5 => "Electronics", 6 => "Ammunition", _ => string.Empty
    };

    public static RtsUiIcon GetResourceIcon(
        int index) =>
        index switch
        {
            0 => RtsUiIcon.ResourceFerrousOre,
            1 => RtsUiIcon.ResourceVolatiles,
            2 => RtsUiIcon.ResourceSilicates,
            3 => RtsUiIcon.ResourceSteel,
            4 => RtsUiIcon.ResourceFuel,
            5 => RtsUiIcon.ResourceElectronics,
            6 => RtsUiIcon.ResourceAmmunition,
            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };

    public static double GetResourceQuantity(
        in PlayerResourceSummary resources,
        int index) =>
        index switch
        {
            0 => resources.FerrousOre,
            1 => resources.Volatiles,
            2 => resources.Silicates,
            3 => resources.Steel,
            4 => resources.Fuel,
            5 => resources.Electronics,
            6 => resources.Ammunition,
            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };

    public static int ResolveVisibleResourceCount(
        float availableWidth,
        float cellWidth)
    {
        if (!float.IsFinite(availableWidth) ||
            !float.IsFinite(cellWidth) ||
            availableWidth <= 0.0f ||
            cellWidth <= 0.0f)
        {
            return 0;
        }

        return Math.Clamp(
            (int)MathF.Floor(
                availableWidth /
                cellWidth),
            0,
            AuthoritativeResourceCount);
    }
}

internal sealed class ResourcePowerHudSurface : IGameplayHudSurface
{
    private readonly ResourcePowerHudRenderer _renderer;

    public ResourcePowerHudSurface(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _renderer =
            new ResourcePowerHudRenderer(
                graphics,
                runtimeAssets);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.TopStatusBar |
        GameplayHudRegion.AlertStack;

    public UiTextDiagnostics TextDiagnostics => _renderer.TextDiagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _renderer.SkinDiagnostics;
    public int LastRenderedVertexCount => _renderer.LastRenderedVertexCount;

    public void Render(
        in GameplayHudRenderContext context) =>
        _renderer.Render(
            context.Graphics,
            context.Snapshot,
            context.Layout,
            context.RuntimeMetrics,
            context.ShowRuntimeMetrics,
            dockExpanded: context.ActionPanel.DockExpanded, localFeedback: context.PreAlphaUx.Placement.Visible ? "" : context.PreAlphaUx.InteractionHint,
            presentationTime: context.PresentationTime, animateNotifications: context.AnimateNotifications,
            expansion: context.NotificationExpansion);

    public void RenderWaiting(IGraphicsCommandContext graphics, in GameplayHudLayout layout,
        in RuntimeMetricsView metrics, bool showRuntimeMetrics = false) =>
        _renderer.Render(graphics, null, layout, metrics, showRuntimeMetrics);

    public void Dispose() =>
        _renderer.Dispose();
}

internal sealed class ResourcePowerHudRenderer : IDisposable
{
    private const int MaxVertices = 131_072;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 1.25f;
    private const float GlyphAdvance = 7.5f;

    private static readonly Vector4 PanelColor =
        GameplayHudVisualStyle.PanelBackground;
    private static readonly Vector4 DividerColor =
        GameplayHudVisualStyle.Border;
    private static readonly Vector4 TextColor =
        GameplayHudVisualStyle.TextPrimary;
    private static readonly Vector4 MutedTextColor =
        GameplayHudVisualStyle.TextSecondary;

    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly GameplayHudSkin _skin;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private readonly RuntimeUiIconPalette? _runtimePalette;
    private readonly NotificationSurfaceMotion _notificationMotion = new();

    private int _vertexCount;
    private float _scale = 1.0f;
    private bool _disposed;

    public ResourcePowerHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _graphics =
            graphics ??
            throw new ArgumentNullException(nameof(graphics));

        _pipeline =
            CreatePipeline(
                graphics);
        _runtimePalette =
            runtimeAssets is null
                ? null
                : new RuntimeUiIconPalette(
                    runtimeAssets);
        _text = new UiTextBatch(graphics);
        try { _skin = new(graphics, runtimeAssets, icons: true, portraits: false); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }

    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext graphics,
        PresentationSnapshot? snapshot,
        in GameplayHudLayout layout,
        in RuntimeMetricsView metrics = default,
        bool showRuntimeMetrics = false, bool hideNotifications = false, bool dockExpanded = false, string localFeedback = "",
        TimeSpan presentationTime = default, bool animateNotifications = false,
        NotificationExpansion expansion = default)
    {
        ThrowIfDisposed();
        _text.Begin();
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphics.Width > 0 && graphics.Height > 0) _skin.Begin(graphics);

        if (graphics.Width <= 0 ||
            graphics.Height <= 0 ||
            layout.SafeArea.IsEmpty)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _scale = layout.Scale;

        if (!layout.TopStatusBar.IsEmpty)
        {
            if (snapshot?.PlayerExperience is PlayerExperienceSnapshot experience)
            {
                EmitTopStatusBar(experience, layout.TopStatusBar, graphics.Width, graphics.Height);
                if (!hideNotifications)
                {
                    EmitNotificationStack(snapshot, experience, layout, dockExpanded, graphics.Width, graphics.Height, localFeedback,
                        presentationTime, animateNotifications, expansion);
                }
            }
            else
            {
                HudRect bar = layout.TopStatusBar;
                if (!_skin.Panel(bar, "panel.resource")) EmitQuad(bar.X, bar.Y, bar.Width, bar.Height, PanelColor, graphics.Width, graphics.Height);
                ReadOnlySpan<char> label = snapshot is null ? "WAITING FOR MATCH" : "WAITING FOR PLAYER DATA";
                if (!_text.AddCentered(label, bar, 14 * MathF.Min(_scale, 1.5f), MutedTextColor,
                    graphics.Width, graphics.Height))
                    EmitText(label, bar.X + 8 * _scale, bar.Y + 12 * _scale,
                        MutedTextColor, graphics.Width, graphics.Height);
            }
        }
        _scale = layout.Scale;


        if (_vertexCount == 0 && _skin.Vertices == 0 && _text.VertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _skin.Flush(graphics);
        if (_vertexCount > 0)
        {
            IGraphicsBuffer vertexBuffer =
                GetFrameVertexBuffer(
                    graphics.FrameIndex);
            vertexBuffer.SetData<OverlayVertex>(
                _vertices.AsSpan(
                    0,
                    _vertexCount));

            graphics.SetPipeline(
                _pipeline);
            graphics.SetVertexBuffer(
                vertexBuffer,
                VertexStride);
            graphics.Draw(
                _vertexCount);

        }

        _text.Render(graphics);
        LastRenderedVertexCount = _vertexCount + _text.VertexCount + _skin.Vertices;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IGraphicsBuffer buffer in
                 _vertexBuffers.Values)
        {
            buffer.Dispose();
        }

        _vertexBuffers.Clear();
        _skin.Dispose();
        _text.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitTopStatusBar(
        in PlayerExperienceSnapshot experience, in HudRect bar, int width, int height)
    {
        var density = ResourceStripLayout.Create(GameplayHudLayout.Create(width, height, 96) with
        { Scale = _scale, TopStatusBar = bar });
        float previousScale = _scale;
        _scale = density.Scale;
        if (_scale <= 0) return;
        if (!_skin.Panel(bar, "panel.resource")) EmitQuad(bar.X, bar.Y, bar.Width, bar.Height, PanelColor, width, height);
        EmitQuad(bar.X, bar.Bottom - _scale, bar.Width, _scale,
            GameplayHudVisualStyle.QuietBorder, width, height);
        Span<char> buffer = stackalloc char[32];
        for (int index = 0; index < ResourcePowerHudModel.AuthoritativeResourceCount; index++)
        {
            var cell = density.Resource(index);
            var icon = ResourcePowerHudModel.GetResourceIcon(index);
            string label = ResourcePowerHudModel.GetResourceLabel(index);
            var value = new HudTextBuilder(buffer);
            AppendQuantity(ref value, ResourcePowerHudModel.GetResourceQuantity(experience.Resources, index));
            float labelSize = MathF.Max(11, 12 * _scale), valueSize = 16 * _scale;
            float textWidth = MathF.Min(cell.Width - 23 * _scale, MathF.Max(
                UiFontMetrics.Default.MeasureLineBounds(label, labelSize).Width,
                UiFontMetrics.Default.MeasureLineBounds(value.Written, valueSize, true).Width));
            float groupWidth = textWidth + 17 * _scale;
            float groupX = cell.X + (cell.Width - groupWidth) * .5f;
            _skin.Panel(cell, "panel.resource");
            float iconY = cell.Y + (cell.Height - 12 * _scale) * .5f;
            if (!_skin.Icon(icon, groupX, iconY, 12 * _scale))
                EmitIcon(icon, groupX, iconY, 12 * _scale, width, height);
            float lineHeight = UiFontMetrics.Default.Description.LineHeight /
                UiFontMetrics.Default.Description.PixelSize;
            float blockHeight = (labelSize + valueSize) * lineHeight;
            float top = cell.Y + (cell.Height - blockHeight) * .5f;
            var labelRect = new HudRect(groupX + 17 * _scale, top, textWidth, labelSize * lineHeight);
            var valueRect = new HudRect(labelRect.X, labelRect.Bottom, textWidth, valueSize * lineHeight);
            if (_text.Ready)
            {
                _text.AddCentered(label, labelRect, labelSize, MutedTextColor, width, height);
                _text.AddCentered(value.Written, valueRect, valueSize, TextColor, width, height, bold: true);
            }
            else
            {
                EmitText(label, labelRect.X, labelRect.Y, MutedTextColor, width, height);
                EmitText(value.Written, valueRect.X, valueRect.Y, TextColor, width, height);
            }
        }
        var power = density.Power;
        Vector4 state = experience.Power.IsConstrained ? GameplayHudVisualStyle.Warning : TextColor;
        if (!_skin.Panel(power, "panel.resource")) EmitQuad(power.X, power.Y + 3 * _scale, power.Width, power.Height - 6 * _scale,
            GameplayHudVisualStyle.PanelHeader, width, height);
        EmitQuad(power.X, power.Y + 3 * _scale, 3 * _scale, power.Height - 6 * _scale,
            state, width, height);
        if (experience.Power.IsConstrained) _skin.State(power, UiSkinState.Warning);
        float powerLabelSize = MathF.Max(11, 12 * _scale);
        float labelHeight = powerLabelSize * UiFontMetrics.Default.Description.LineHeight /
            UiFontMetrics.Default.Description.PixelSize;
        float valueHeight = 16 * _scale * UiFontMetrics.Default.Description.LineHeight /
            UiFontMetrics.Default.Description.PixelSize;
        float y = power.Y + (power.Height - labelHeight - valueHeight) * .5f;
        var labelBounds = new HudRect(power.X + 6 * _scale, y, power.Width - 12 * _scale, labelHeight);
        var valueBounds = new HudRect(labelBounds.X, labelBounds.Bottom, labelBounds.Width, valueHeight);
        Span<char> powerBuffer = stackalloc char[80];
        var text = new HudTextBuilder(powerBuffer);
        text.Append("POWER / ");
        text.Append(ResourcePowerHudModel.ResolvePowerStateLabel(experience.Power));
        if (!_text.AddCentered(text.Written, labelBounds, powerLabelSize, state, width, height))
            EmitText(text.Written, labelBounds.X, labelBounds.Y, state, width, height);
        text = new HudTextBuilder(powerBuffer);
        AppendQuantity(ref text, experience.Power.Generation); text.Append(" / ");
        AppendQuantity(ref text, experience.Power.Demand);
        if (!_text.AddCentered(text.Written, valueBounds, 16 * _scale, TextColor, width, height, bold: true))
            EmitText(text.Written, valueBounds.X, valueBounds.Y, TextColor, width, height);
        _scale = previousScale;
    }

    private void EmitNotificationStack(PresentationSnapshot? snapshot, in PlayerExperienceSnapshot experience,
        in GameplayHudLayout layout, bool expanded, int width, int height, string localFeedback,
        TimeSpan presentationTime, bool animateNotifications, NotificationExpansion expansion)
    {
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        var stack = ActionableAlertLayout.Resolve(snapshot, layout, expanded, localFeedback, expansion);
        _notificationMotion.Update(stack, snapshot?.SessionId ?? default, layout, presentationTime, animateNotifications);
        Span<char> buffer = stackalloc char[2048];
        Span<char> ellipsis = stackalloc char[2052];
        _scale = stack.FontSize / 12;
        for (int i = 0; i < stack.Count; i++)
        {
            var card = stack.Card(i);
            var item = card.Item;
            float visibility = _notificationMotion.Opacity(i);
            HudRect incoming = _notificationMotion.EnteringBounds(card.Bounds, i);
            var color = NotificationText.Severity(experience, item);
            if (!incoming.IsEmpty)
            {
                if (!_skin.Panel(incoming, "panel.raised", NotificationSurfaceVisuals.BackgroundOpacity * visibility))
                    EmitQuad(incoming.X, incoming.Y, incoming.Width, incoming.Height,
                        GameplayHudVisualStyle.PanelRaised with { W = NotificationSurfaceVisuals.BackgroundOpacity * visibility }, width, height);
                EmitQuad(incoming.X, incoming.Y, MathF.Min(incoming.Width, color == HudAlertSeverity.Critical ? 5 : 3),
                    incoming.Height, SeverityColor(color) with { W = visibility }, width, height);
            }
            // Foreground geometry advances with the row and becomes visible only
            // once its complete clip-safe bounds have entered from the right.
            float shift = incoming.X - card.Bounds.X;
            float iconSize = stack.FontSize;
            float iconX = card.Bounds.X + (5 + 4) * ActionableAlertLayout.Scale(layout) + shift;
            if (iconX + iconSize <= layout.SafeArea.Right)
                EmitIcon(item.Hidden > 0 ? RtsUiIcon.StatusAlert :
                    item.Feedback ? CommandIcon(experience.Feedback.Kind) : item.Local ? RtsUiIcon.StatusAlert : AlertIcon(item.Kind),
                    iconX, card.Bounds.Y + (card.Bounds.Height - iconSize) * .5f,
                    iconSize, width, height, visibility);
            var message = NotificationText.Format(experience, alerts, item, buffer, localFeedback);
            var visible = NotificationText.Ellipsized(message, card.Text.Width, stack.FontSize, ellipsis);
            HudRect movingText = card.Text with { X = card.Text.X + shift };
            if (movingText.Right <= layout.SafeArea.Right)
            {
                if (!_text.AddCentered(visible, movingText, stack.FontSize, TextColor with { W = visibility }, width, height))
                    EmitText(visible, movingText.X, movingText.Y, TextColor with { W = visibility }, width, height);
            }
            if (!card.Action.IsEmpty && item.Hidden == 0)
            {
                bool collapsedMore = card.Truncated && card.Action == card.Details;
                string label = collapsedMore || item.Feedback || item.Local ? "MORE" : "OPS";
                if (!collapsedMore && alerts is not null)
                    for (int a = 0; a < alerts.Alerts.Count; a++)
                        if (alerts.Alerts[a].Identity.Kind == item.Kind && alerts.Alerts[a].Target.IsValid) label = "FOCUS";
                HudRect movingAction = card.Action with { X = card.Action.X + shift };
                if (movingAction.Right <= layout.SafeArea.Right)
                    EmitNotificationAction(label, movingAction, stack.FontSize, width, height, visibility);
            }
            if (card.Truncated && !card.Details.IsEmpty && card.Details != card.Action && item.Hidden == 0)
            {
                HudRect movingDetails = card.Details with { X = card.Details.X + shift };
                if (movingDetails.Right <= layout.SafeArea.Right)
                    EmitNotificationAction("MORE", movingDetails, stack.FontSize, width, height, visibility);
            }
            if (visibility >= .999f && expansion.Matches(card) && !card.ExpansionBounds.IsEmpty)
                EmitExpandedNotification(card.ExpansionBounds, expansion, stack.FontSize, width, height);
        }
        // Retired cards paint only a short-lived, noninteractive background. Their old
        // text and targets never cross a resolved/session boundary.
        for (int i = 0; i < _notificationMotion.RetiredCount; i++)
        {
            var (bounds, alpha) = _notificationMotion.Retired(i);
            bool overlaps = false;
            for (int active = 0; active < stack.Count; active++)
                if (bounds.Intersects(stack.Card(active).Bounds)) { overlaps = true; break; }
            if (!overlaps && alpha > 0)
                EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    GameplayHudVisualStyle.PanelRaised with { W = NotificationSurfaceVisuals.BackgroundOpacity * alpha },
                    width, height);
        }
        _scale = layout.Scale;
    }

    private void EmitExpandedNotification(in HudRect bounds, in NotificationExpansion expansion,
        float fontSize, int width, int height)
    {
        float progress = Math.Clamp(expansion.Progress, 0, 1);
        if (!_skin.Panel(bounds, "panel.raised", NotificationSurfaceVisuals.BackgroundOpacity * progress))
            EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                GameplayHudVisualStyle.PanelRaised with { W = NotificationSurfaceVisuals.BackgroundOpacity * progress }, width, height);
        if (progress < .999f || expansion.Content.Length == 0) return;
        float padding = MathF.Max(5, fontSize * .45f);
        float size = fontSize * .88f;
        float heightPerLine = UiFontMetrics.Default.Description.LineHeight /
            UiFontMetrics.Default.Description.PixelSize * size;
        float textWidth = MathF.Max(1, bounds.Width - padding * 2);
        int perPage = Math.Clamp((int)((bounds.Height - padding * 2) / heightPerLine) - 1, 1, 5);
        ReadOnlySpan<char> content = expansion.Content.AsSpan();
        int totalLines = 0, offset = 0;
        while (offset < content.Length)
        {
            _ = NotificationText.Line(content[offset..], textWidth, size, out int consumed);
            offset += Math.Max(1, consumed);
            totalLines++;
        }
        int pages = Math.Max(1, (totalLines + perPage - 1) / perPage);
        int page = (int)((uint)expansion.Page % (uint)pages);
        int first = page * perPage;
        offset = 0;
        for (int line = 0; line < first + perPage && offset < content.Length; line++)
        {
            ReadOnlySpan<char> value = NotificationText.Line(content[offset..], textWidth, size, out int consumed);
            if (line >= first)
            {
                var rect = new HudRect(bounds.X + padding,
                    bounds.Y + padding + (line - first) * heightPerLine, textWidth, heightPerLine);
                if (!_text.AddCentered(value, rect, size, TextColor, width, height))
                    EmitText(value, rect.X, rect.Y, TextColor, width, height);
            }
            offset += Math.Max(1, consumed);
        }
        Span<char> pageBuffer = stackalloc char[48];
        var label = new HudTextBuilder(pageBuffer);
        label.Append("PAGE "); label.Append(page + 1); label.Append("/"); label.Append(pages); label.Append("  NEXT: CLICK");
        var footer = new HudRect(bounds.X + padding, bounds.Bottom - padding - heightPerLine, textWidth, heightPerLine);
        if (!_text.AddCentered(label.Written, footer, size, MutedTextColor, width, height))
            EmitText(label.Written, footer.X, footer.Y, MutedTextColor, width, height);
    }

    private void EmitNotificationAction(string label, in HudRect bounds, float fontSize, int width, int height, float visibility)
    {
        if (!_skin.Panel(bounds, "control.button_compact", visibility)) EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            GameplayHudVisualStyle.PanelRaised with { W = visibility }, width, height);
        if (!_text.AddCentered(label, bounds, fontSize, TextColor with { W = visibility }, width, height))
        {
            int count = Math.Min(label.Length, Math.Max(0, (int)(bounds.Width / (GlyphAdvance * _scale))));
            EmitText(label.AsSpan(0, count), bounds.X, bounds.Y, TextColor with { W = visibility }, width, height);
        }
    }

    private static Vector4 SeverityColor(
        HudAlertSeverity severity) =>
        severity switch
        {
            HudAlertSeverity.Critical =>
                GameplayHudVisualStyle.Critical,
            HudAlertSeverity.Warning =>
                GameplayHudVisualStyle.Warning,
            _ =>
                GameplayHudVisualStyle.TextPrimary
        };

    private static RtsUiIcon AlertIcon(
        PlayerAlertState alert) =>
        alert switch
        {
            PlayerAlertState.LowPower =>
                RtsUiIcon.StatusPower,
            PlayerAlertState.ProductionBlocked =>
                RtsUiIcon.BuildingFactory,
            PlayerAlertState.SupplyCritical =>
                RtsUiIcon.SupplyCritical,
            PlayerAlertState.CommandCoreDamaged =>
                RtsUiIcon.StatusHealth,
            PlayerAlertState.CommandCoreDestroyed =>
                RtsUiIcon.BuildingCommand,
            _ =>
                RtsUiIcon.StatusAlert
        };

    private static RtsUiIcon CommandIcon(
        PlayerCommandFeedbackKind kind) =>
        kind switch
        {
            PlayerCommandFeedbackKind.Movement =>
                RtsUiIcon.CommandMove,
            PlayerCommandFeedbackKind.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerCommandFeedbackKind.Production =>
                RtsUiIcon.BuildingProcessing,
            PlayerCommandFeedbackKind.UnitProduction =>
                RtsUiIcon.BuildingFactory,
            PlayerCommandFeedbackKind.Logistics or
            PlayerCommandFeedbackKind.Supply =>
                RtsUiIcon.CommandSupply,
            PlayerCommandFeedbackKind.Tactical or
            PlayerCommandFeedbackKind.Artillery =>
                RtsUiIcon.CommandAttack,
            PlayerCommandFeedbackKind.Surrender =>
                RtsUiIcon.StatusAlert,
            _ =>
                RtsUiIcon.StatusAlert
        };

    internal static string CommandLabel(
        PlayerCommandFeedbackKind kind) =>
        kind switch
        {
            PlayerCommandFeedbackKind.Movement =>
                "MOVE",
            PlayerCommandFeedbackKind.Construction =>
                "BUILD",
            PlayerCommandFeedbackKind.Production =>
                "PROCESS",
            PlayerCommandFeedbackKind.UnitProduction =>
                "UNITS",
            PlayerCommandFeedbackKind.Logistics =>
                "LOGISTICS",
            PlayerCommandFeedbackKind.Supply =>
                "SUPPLY",
            PlayerCommandFeedbackKind.Tactical =>
                "TACTICAL",
            PlayerCommandFeedbackKind.Artillery =>
                "ARTILLERY",
            PlayerCommandFeedbackKind.Surrender =>
                "SURRENDER",
            _ =>
                "COMMAND"
        };

    private static string SeverityLabel(
        HudAlertSeverity severity) =>
        severity switch
        {
            HudAlertSeverity.Critical =>
                "CRIT",
            HudAlertSeverity.Warning =>
                "WARN",
            _ =>
                "INFO"
        };

    internal static string ResolveFailureLabel(
        in PlayerCommandFeedback feedback)
    {
        if (feedback.PlacementFailure !=
            BuildingPlacementFailureReason.None)
        {
            return feedback.PlacementFailure switch
            {
                BuildingPlacementFailureReason.UnknownBuilding =>
                    "UNKNOWN BUILDING",
                BuildingPlacementFailureReason.TerrainUnavailable =>
                    "NO TERRAIN",
                BuildingPlacementFailureReason.OutsideWorldBounds =>
                    "OUT OF BOUNDS",
                BuildingPlacementFailureReason.SlopeTooSteep =>
                    "SLOPE",
                BuildingPlacementFailureReason.Obstructed =>
                    "OBSTRUCTED",
                BuildingPlacementFailureReason.OutsideBuildableArea =>
                    "NO BUILD AREA",
                BuildingPlacementFailureReason.ResourceDepositRequired =>
                    "DEPOSIT REQUIRED",
                _ =>
                    "PLACEMENT"
            };
        }

        if (feedback.BuildRejection !=
            BuildCommandRejectionReason.None)
        {
            return feedback.BuildRejection switch
            {
                BuildCommandRejectionReason.UnknownBuilding =>
                    "UNKNOWN BUILDING",
                BuildCommandRejectionReason.InvalidSourceInventory =>
                    "INVALID SOURCE",
                BuildCommandRejectionReason.SourceInventoryOwnershipMismatch =>
                    "SOURCE OWNER",
                BuildCommandRejectionReason.InsufficientResources =>
                    "NO MATERIALS",
                BuildCommandRejectionReason.PlacementInvalid =>
                    "PLACEMENT",
                _ =>
                    "BUILD REJECTED"
            };
        }

        if (feedback.ActionFailure !=
            PlayerLogisticsActionFailureReason.None)
        {
            return feedback.ActionFailure switch
            {
                PlayerLogisticsActionFailureReason.InvalidTarget =>
                    "INVALID TARGET",
                PlayerLogisticsActionFailureReason.ForeignOwnership =>
                    "FOREIGN TARGET",
                PlayerLogisticsActionFailureReason.UnsupportedTarget =>
                    "UNSUPPORTED",
                PlayerLogisticsActionFailureReason.MissingPolicy =>
                    "NO POLICY",
                PlayerLogisticsActionFailureReason.InvalidThresholds =>
                    "THRESHOLDS",
                PlayerLogisticsActionFailureReason.ResupplyUnavailable =>
                    "NO RESUPPLY",
                _ =>
                    "ACTION REJECTED"
            };
        }

        if (feedback.TacticalFailure !=
            PlayerTacticalActionFailureReason.None)
        {
            return feedback.TacticalFailure switch
            {
                PlayerTacticalActionFailureReason.NoEligibleUnits =>
                    "NO UNITS",
                PlayerTacticalActionFailureReason.TargetUnavailable =>
                    "NO TARGET",
                PlayerTacticalActionFailureReason.TargetNotIdentified =>
                    "TARGET UNKNOWN",
                PlayerTacticalActionFailureReason.FriendlyTarget =>
                    "FRIENDLY TARGET",
                PlayerTacticalActionFailureReason.TargetNotTargetable =>
                    "NOT TARGETABLE",
                PlayerTacticalActionFailureReason.UnsupportedTargetClass =>
                    "NO WEAPON",
                PlayerTacticalActionFailureReason.NoEligibleArtillery =>
                    "NO ARTILLERY",
                PlayerTacticalActionFailureReason.ArtilleryTargetUnavailable =>
                    "NO ARTY TARGET",
                PlayerTacticalActionFailureReason.ArtilleryOutOfRange =>
                    "OUT OF RANGE",
                PlayerTacticalActionFailureReason.NoRecoveryProvider =>
                    "NO RECOVERY",
                _ =>
                    "TACTICAL REJECTED"
            };
        }

        return "";
    }

    internal static void AppendQuantity(
        ref HudTextBuilder builder,
        double value)
    {
        if (!double.IsFinite(value)) { builder.Append("N/A"); return; }
        double normalized = Math.Max(0.0, value);

        if (normalized >= 1_000_000_000_000.0)
        {
            builder.Append(normalized, "0.#E0");
            return;
        }
        if (normalized >= 1_000_000_000.0)
        {
            builder.Append(normalized / 1_000_000_000.0, "F1");
            builder.Append("B");
            return;
        }

        if (normalized >=
            1_000_000.0)
        {
            builder.Append(
                normalized /
                1_000_000.0,
                "F1");
            builder.Append("M");
            return;
        }

        if (normalized >=
            10_000.0)
        {
            builder.Append(
                normalized /
                1_000.0,
                "F1");
            builder.Append("K");
            return;
        }

        builder.Append(
            normalized,
            "F0");
    }

    private void EmitText(
        ReadOnlySpan<char> text,
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        if (_text.Add(text, x, y, UiTypography.Style(UiTextRole.Telemetry).Size * _scale, color, width, height)) return;

        for (int index = 0;
             index < text.Length;
             index++)
        {
            EmitGlyph(
                char.ToUpperInvariant(
                    text[index]),
                x +
                    index *
                    GlyphAdvance *
                    _scale,
                y,
                color,
                width,
                height);
        }
    }

    private void EmitGlyph(
        char character,
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        string pattern =
            GlyphPattern(
                character);

        if (pattern.Length == 0)
        {
            return;
        }

        for (int row = 0;
             row < 7;
             row++)
        {
            for (int column = 0;
                 column < 5;
                 column++)
            {
                if (pattern[
                        row * 5 +
                        column] !=
                    '1')
                {
                    continue;
                }

                EmitQuad(
                    x +
                    column *
                        GlyphPixelSize *
                        _scale,
                    y +
                    row *
                        GlyphPixelSize *
                        _scale,
                    GlyphPixelSize *
                        _scale,
                    GlyphPixelSize *
                        _scale,
                    color,
                    width,
                    height);
            }
        }
    }

    private void EmitIcon(
        RtsUiIcon icon,
        float x,
        float y,
        float size,
        int width,
        int height,
        float visibility = 1)
    {
        if (_skin.Icon(icon, x, y, size, new Vector4(1, 1, 1, visibility))) return;
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);
        string pattern =
            IconPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
        color.W *= visibility;
        float pixel =
            MathF.Max(
                1.0f,
                size /
                5.0f);

        for (int row = 0;
             row < 5;
             row++)
        {
            for (int column = 0;
                 column < 5;
                 column++)
            {
                if (pattern[
                        row * 5 +
                        column] !=
                    '1')
                {
                    continue;
                }

                EmitQuad(
                    x +
                    column *
                        pixel,
                    y +
                    row *
                        pixel,
                    pixel,
                    pixel,
                    color,
                    width,
                    height);
            }
        }
    }

    private Vector4 ResolveColor(
        RtsUiIcon icon)
    {
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);

        return _runtimePalette is not null &&
               _runtimePalette.TryResolve(
                   definition.AssetId,
                   out Vector4 runtimeColor)
            ? runtimeColor
            : definition.FallbackTint;
    }

    private void EmitQuad(
        float x,
        float y,
        float quadWidth,
        float quadHeight,
        Vector4 color,
        int width,
        int height)
    {
        if (_vertexCount >
                MaxVertices -
                6 ||
            width <= 0 ||
            height <= 0 ||
            quadWidth <= 0.0f ||
            quadHeight <= 0.0f)
        {
            return;
        }

        float left =
            x /
            width *
            2.0f -
            1.0f;
        float right =
            (x + quadWidth) /
            width *
            2.0f -
            1.0f;
        float top =
            1.0f -
            y /
            height *
            2.0f;
        float bottom =
            1.0f -
            (y + quadHeight) /
            height *
            2.0f;

        Vector2 topLeft =
            new(
                left,
                top);
        Vector2 topRight =
            new(
                right,
                top);
        Vector2 bottomLeft =
            new(
                left,
                bottom);
        Vector2 bottomRight =
            new(
                right,
                bottom);

        _vertices[_vertexCount++] =
            new OverlayVertex(
                topLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomRight,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                topRight,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                topLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomRight,
                color);
    }

    private IGraphicsBuffer GetFrameVertexBuffer(
        int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(
                frameIndex,
                out IGraphicsBuffer? buffer))
        {
            return buffer;
        }

        buffer =
            _graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked(
                        (ulong)_vertices.Length *
                        VertexStride),
                    GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(
            frameIndex,
            buffer);
        return buffer;
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            struct VertexInput
            {
                float2 Position : POSITION;
                float4 Color : COLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position = float4(input.Position, 0.0f, 1.0f);
                output.Color = input.Color;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                return input.Color;
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "ResourcePowerHudVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "ResourcePowerHudPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float2,
                        0),
                    new GraphicsVertexElement(
                        "COLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        8)
                ],
                CullMode = GraphicsCullMode.None,
                AlphaBlendEnabled = true,
                DepthEnabled = false
            });
    }

    private static string IconPattern(
        RtsUiGlyph glyph) =>
        glyph switch
        {
            RtsUiGlyph.Ore =>
                "01110" + "11111" + "11111" + "01110" + "00100",
            RtsUiGlyph.Drop =>
                "00100" + "01110" + "01110" + "11111" + "01110",
            RtsUiGlyph.Crystal =>
                "00100" + "01110" + "11111" + "01110" + "00100",
            RtsUiGlyph.Ingot =>
                "00000" + "11111" + "10001" + "11111" + "00000",
            RtsUiGlyph.Circuit =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Ammunition =>
                "10101" + "10101" + "10101" + "11111" + "01110",
            RtsUiGlyph.Power =>
                "00110" + "01100" + "11110" + "00110" + "01100",
            RtsUiGlyph.Alert or
            RtsUiGlyph.Critical =>
                "00100" + "00100" + "00100" + "00000" + "00100",
            _ =>
                "00100" + "01110" + "11111" + "01110" + "00100"
        };

    private static string GlyphPattern(
        char value) =>
        value switch
        {
            'A' => "01110100011000111111100011000110001",
            'B' => "11110100011000111110100011000111110",
            'C' => "01111100001000010000100001000001111",
            'D' => "11110100011000110001100011000111110",
            'E' => "11111100001000011110100001000011111",
            'F' => "11111100001000011110100001000010000",
            'G' => "01111100001000010111100011000101111",
            'H' => "10001100011000111111100011000110001",
            'I' => "11111001000010000100001000010011111",
            'J' => "00111000100001000010100101001001100",
            'K' => "10001100101010011000101001001010001",
            'L' => "10000100001000010000100001000011111",
            'M' => "10001110111010110101100011000110001",
            'N' => "10001110011010110011100011000110001",
            'O' => "01110100011000110001100011000101110",
            'P' => "11110100011000111110100001000010000",
            'Q' => "01110100011000110001101011001001101",
            'R' => "11110100011000111110101001001010001",
            'S' => "01111100001000001110000010000111110",
            'T' => "11111001000010000100001000010000100",
            'U' => "10001100011000110001100011000101110",
            'V' => "10001100011000110001100010101000100",
            'W' => "10001100011000110101101011101110001",
            'X' => "10001100010101000100010101000110001",
            'Y' => "10001100010101000100001000010000100",
            'Z' => "11111000010001000100010001000011111",
            '0' => "01110100011001110101110011000101110",
            '1' => "00100011000010000100001000010001110",
            '2' => "01110100010000100010001000100011111",
            '3' => "11110000010000101110000010000111110",
            '4' => "00010001100101010010111110001000010",
            '5' => "11111100001000011110000010000111110",
            '6' => "01110100001000011110100011000101110",
            '7' => "11111000010001000100010000100001000",
            '8' => "01110100011000101110100011000101110",
            '9' => "01110100011000101111000010000101110",
            '.' => "00000000000000000000000000011000110",
            '/' => "00001000100010001000100001000000000",
            '-' or '—' => "00000000000000011111000000000000000",
            '>' => "10000010000010000010001000100010000",
            '+' => "00000001000010011111001000010000000",
            ' ' => "",
            _ => "11111000010001000100000000010000100"
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(
        Vector2 Position,
        Vector4 Color);

    internal ref struct HudTextBuilder
    {
        private Span<char> _buffer;
        private int _length;

        public HudTextBuilder(
            Span<char> buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        public readonly ReadOnlySpan<char> Written =>
            _buffer[.._length];

        public void Append(
            string value)
        {
            if (value.AsSpan().TryCopyTo(
                    _buffer[_length..]))
            {
                _length +=
                    value.Length;
            }
        }

        public void Append(
            int value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider:
                        CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }

        public void Append(
            double value,
            string format)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    format,
                    CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }
    }


}

