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
            context.ShowRuntimeMetrics);

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
        bool showRuntimeMetrics = true)
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
                if (ActionableAlertSnapshot.Resolve(snapshot) is { } alerts)
                    EmitActionableNotifications(experience, alerts, layout, graphics.Width, graphics.Height);
                else { _scale = HudInformationLayout.AlertScale(layout); EmitNotificationStack(experience, HudInformationLayout.Alerts(layout), graphics.Width, graphics.Height); _scale = layout.Scale; }
            }
            else
            {
                HudRect bar = layout.TopStatusBar;
                if (!_skin.Panel(bar, "panel.resource")) EmitQuad(bar.X, bar.Y, bar.Width, bar.Height, PanelColor, graphics.Width, graphics.Height);
                ReadOnlySpan<char> label = snapshot is null ? "WAITING FOR MATCH" : "WAITING FOR PLAYER DATA";
                int count = Math.Min(label.Length, Math.Max(0, (int)((bar.Width - 16 * _scale) / (GlyphAdvance * _scale))));
                EmitText(label[..count], bar.X + 8 * _scale, bar.Y + 12 * _scale,
                    MutedTextColor, graphics.Width, graphics.Height);
            }
        }
        _scale = layout.Scale;
        if (showRuntimeMetrics) EmitRuntimeMetrics(metrics, layout.RuntimeMetrics, graphics.Width, graphics.Height);

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

    private void EmitRuntimeMetrics(in RuntimeMetricsView metrics, in HudRect region, int width, int height)
    {
        if (region.IsEmpty) return;
        EmitQuad(region.X, region.Y, region.Width, region.Height, PanelColor, width, height);
        float originalScale = _scale;
        _scale = MathF.Min(_scale, MathF.Min(region.Width / 142.0f, region.Height / 36.0f));
        Span<char> text = stackalloc char[32];
        var builder = new HudTextBuilder(text);
        builder.Append("FPS  ");
        if (metrics.FramesPerSecond is double fps)
        {
            if (fps >= 10000) builder.Append(">9999");
            else builder.Append(fps, "F0");
        }
        else builder.Append("—");
        EmitText(builder.Written, region.X + 8 * _scale, region.Y + 6 * _scale, MutedTextColor, width, height);
        builder = new HudTextBuilder(text);
        builder.Append("SIM  ");
        if (metrics.SimulationState == RuntimeSimulationState.Paused) builder.Append("PAUSED");
        else if (metrics.SimulationState == RuntimeSimulationState.Stopped) builder.Append("STOPPED");
        else if (metrics.TicksPerSecond is double tps)
        {
            if (tps >= 10000) builder.Append(">9999");
            else builder.Append(tps, "F1");
            builder.Append(" TPS");
        }
        else builder.Append("— TPS");
        EmitText(builder.Written, region.X + 8 * _scale, region.Y + 20 * _scale, MutedTextColor, width, height);
        _scale = originalScale;
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
            var definition = RtsUiIconCatalog.Get(icon);
            float top = cell.Y + (cell.Height - 30 * _scale) * .5f;
            _skin.Panel(cell, "panel.resource");
            if (!_skin.Icon(icon, cell.X + 2 * _scale, top + 9 * _scale, 12 * _scale))
                EmitIcon(icon, cell.X + 2 * _scale, top + 9 * _scale, 12 * _scale, width, height);
            EmitQuad(cell.Right - _scale, cell.Y + 5 * _scale, _scale,
                cell.Height - 10 * _scale, GameplayHudVisualStyle.QuietBorder, width, height);
            float textX = cell.X + 19 * _scale;
            float textWidth = MathF.Max(0, cell.Right - textX - 5 * _scale);
            var value = new HudTextBuilder(buffer);
            AppendQuantity(ref value, ResourcePowerHudModel.GetResourceQuantity(experience.Resources, index));
            if (_text.Ready)
            {
                _text.Add(definition.ShortLabel, textX, top, GameplayHudVisualStyle.CaptionTextSize * _scale,
                    MutedTextColor, width, height, textWidth);
                _text.Add(value.Written, textX, top + 13 * _scale, GameplayHudVisualStyle.ValueTextSize * _scale,
                    TextColor, width, height, textWidth, bold: true, alignment: UiTextAlignment.Right);
            }
            else
            {
                EmitText(definition.ShortLabel, textX, top, MutedTextColor, width, height);
                EmitText(value.Written, textX, top + 13 * _scale, TextColor, width, height);
            }
        }
        var power = density.Power;
        Vector4 state = experience.Power.IsConstrained ? GameplayHudVisualStyle.Warning : TextColor;
        if (!_skin.Panel(power, "panel.resource")) EmitQuad(power.X, power.Y + 3 * _scale, power.Width, power.Height - 6 * _scale,
            GameplayHudVisualStyle.PanelHeader, width, height);
        EmitQuad(power.X, power.Y + 3 * _scale, 3 * _scale, power.Height - 6 * _scale,
            state, width, height);
        if (experience.Power.IsConstrained) _skin.State(power, UiSkinState.Warning);
        float y = power.Y + (power.Height - 28 * _scale) * .5f;
        Span<char> powerBuffer = stackalloc char[80];
        var text = new HudTextBuilder(powerBuffer);
        text.Append("POWER / ");
        text.Append(ResourcePowerHudModel.ResolvePowerStateLabel(experience.Power));
        if (!_text.Add(text.Written, power.X + 10 * _scale, y, 10 * _scale,
            state, width, height, power.Width - 18 * _scale, bold: true))
            EmitText(text.Written, power.X + 10 * _scale, y, state, width, height);
        text = new HudTextBuilder(powerBuffer);
        AppendQuantity(ref text, experience.Power.Generation); text.Append(" / ");
        AppendQuantity(ref text, experience.Power.Demand);
        if (!_text.Add(text.Written, power.X + 10 * _scale, y + 12 * _scale, 13 * _scale,
            TextColor, width, height, power.Width - 18 * _scale, bold: true))
            EmitText(text.Written, power.X + 10 * _scale, y + 13 * _scale, TextColor, width, height);
        _scale = previousScale;
    }

    private void EmitNotificationStack(
        in PlayerExperienceSnapshot experience,
        in HudRect region,
        int width,
        int height)
    {
        if (region.IsEmpty)
        {
            return;
        }

        float rowHeight =
            21.0f * _scale;
        int maximumRows =
            Math.Max(
                0,
                (int)MathF.Floor(
                    region.Height /
                    rowHeight));

        if (maximumRows == 0)
        {
            return;
        }

        bool feedbackVisible =
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                experience);
        int totalItems =
            ResourcePowerHudModel.ResolveNotificationCount(
                experience);

        if (totalItems == 0)
        {
            return;
        }

        int contentRows =
            totalItems > maximumRows &&
            maximumRows > 1
                ? maximumRows - 1
                : maximumRows;
        int emitted = 0;
        float y =
            region.Y;

        if (ResourcePowerHudModel.HasAlert(
                experience.Alerts,
                PlayerAlertState.CommandCoreDestroyed) &&
            emitted < contentRows)
        {
            EmitAlertCard(
                PlayerAlertState.CommandCoreDestroyed,
                "CORE DESTROYED",
                0,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        if (ResourcePowerHudModel.HasAlert(
                experience.Alerts,
                PlayerAlertState.CommandCoreDamaged) &&
            emitted < contentRows)
        {
            EmitAlertCard(
                PlayerAlertState.CommandCoreDamaged,
                "CORE DAMAGED",
                0,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        if (ResourcePowerHudModel.HasAlert(
                experience.Alerts,
                PlayerAlertState.SupplyCritical) &&
            emitted < contentRows)
        {
            EmitAlertCard(
                PlayerAlertState.SupplyCritical,
                "SUPPLY CRITICAL",
                experience.CriticalSupplyUnits,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        if (feedbackVisible &&
            emitted < contentRows)
        {
            EmitCommandFeedbackCard(
                experience.Feedback,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        if (ResourcePowerHudModel.HasAlert(
                experience.Alerts,
                PlayerAlertState.ProductionBlocked) &&
            emitted < contentRows)
        {
            EmitAlertCard(
                PlayerAlertState.ProductionBlocked,
                "PRODUCTION BLOCKED",
                experience.BlockedProductionFacilities,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        if (ResourcePowerHudModel.HasAlert(
                experience.Alerts,
                PlayerAlertState.LowPower) &&
            emitted < contentRows)
        {
            EmitAlertCard(
                PlayerAlertState.LowPower,
                "POWER CONSTRAINED",
                0,
                region,
                y,
                width,
                height);
            emitted++;
            y += rowHeight;
        }

        int hidden =
            totalItems -
            emitted;

        if (hidden > 0 &&
            emitted < maximumRows)
        {
            Span<char> buffer =
                stackalloc char[32];
            var text =
                new HudTextBuilder(
                    buffer);
            text.Append("MORE ");
            text.Append(hidden);

            EmitNotificationCard(
                RtsUiIcon.StatusAlert,
                HudAlertSeverity.Information,
                text.Written,
                region,
                y,
                width,
                height);
        }
    }

    private void EmitActionableNotifications(in PlayerExperienceSnapshot experience, ActionableAlertSnapshot alerts, in GameplayHudLayout layout, int width, int height)
    {
        _scale = HudInformationLayout.AlertScale(layout);
        int maximum = (int)(layout.AlertStack.Height / (21 * _scale));
        var region = HudInformationLayout.Alerts(layout);
        for (int row = 0; row < maximum; row++)
        {
            var item = ActionableAlertLayout.Item(experience, layout, row);
            var rect = ActionableAlertLayout.Row(layout, row);
            if (item.Feedback) EmitCommandFeedbackCard(experience.Feedback, region, rect.Y, width, height);
            else if (item.Hidden > 0) EmitAlertCard(PlayerAlertState.None, "MORE - OPEN OPS", item.Hidden, region, rect.Y, width, height);
            else if (item.Kind != PlayerAlertState.None)
                for (int i = 0; i < alerts.Alerts.Count; i++)
                {
                    var alert = alerts.Alerts[i];
                    if (alert.Identity.Kind != item.Kind) continue;
                    int count = item.Kind == PlayerAlertState.SupplyCritical ? experience.CriticalSupplyUnits :
                        item.Kind == PlayerAlertState.ProductionBlocked ? experience.BlockedProductionFacilities : 0;
                    EmitAlertCard(item.Kind, alert.Label, count, region, rect.Y, width, height, alert.Target.IsValid ? " FOCUS" : " OPS");
                    break;
                }
        }
    }

    private void EmitAlertCard(
        PlayerAlertState alert,
        string label,
        int count,
        in HudRect region,
        float y,
        int width,
        int height, string suffix = "")
    {
        Span<char> buffer =
            stackalloc char[96];
        var text =
            new HudTextBuilder(
                buffer);
        HudAlertSeverity severity =
            ResourcePowerHudModel.ResolveAlertSeverity(
                alert);

        text.Append(
            SeverityLabel(
                severity));
        text.Append(" ");
        text.Append(label);
        text.Append(suffix);

        if (count > 0)
        {
            text.Append(" ");
            text.Append(count);
        }

        EmitNotificationCard(
            AlertIcon(
                alert),
            severity,
            text.Written,
            region,
            y,
            width,
            height);
    }

    private void EmitCommandFeedbackCard(
        in PlayerCommandFeedback feedback,
        in HudRect region,
        float y,
        int width,
        int height)
    {
        Span<char> buffer =
            stackalloc char[128];
        var text =
            new HudTextBuilder(
                buffer);
        HudAlertSeverity severity =
            feedback.State switch
            {
                PlayerCommandFeedbackState.Accepted =>
                    HudAlertSeverity.Information,
                PlayerCommandFeedbackState.Partial =>
                    HudAlertSeverity.Warning,
                PlayerCommandFeedbackState.Rejected =>
                    HudAlertSeverity.Critical,
                _ =>
                    HudAlertSeverity.Information
            };

        text.Append(
            feedback.State switch
            {
                PlayerCommandFeedbackState.Accepted =>
                    "OK",
                PlayerCommandFeedbackState.Partial =>
                    "PART",
                PlayerCommandFeedbackState.Rejected =>
                    "FAIL",
                _ =>
                    "INFO"
            });
        text.Append(" ");
        text.Append(
            CommandLabel(
                feedback.Kind));

        if (feedback.AcceptedTargets > 0 ||
            feedback.RejectedTargets > 0)
        {
            text.Append(" ");
            text.Append(
                feedback.AcceptedTargets);
            text.Append("/");
            text.Append(
                feedback.RejectedTargets);
        }

        string failure =
            ResolveFailureLabel(
                feedback);

        if (failure.Length > 0)
        {
            text.Append(" ");
            text.Append(
                failure);
        }

        EmitNotificationCard(
            CommandIcon(
                feedback.Kind),
            severity,
            text.Written,
            region,
            y,
            width,
            height);
    }

    private void EmitNotificationCard(
        RtsUiIcon icon,
        HudAlertSeverity severity,
        ReadOnlySpan<char> text,
        in HudRect region,
        float y,
        int width,
        int height)
    {
        float cardHeight =
            18.0f * _scale;
        float markerWidth =
            severity == HudAlertSeverity.Critical
                ? 5.0f * _scale
                : 3.0f * _scale;
        Vector4 severityColor =
            SeverityColor(
                severity);

        EmitQuad(
            region.X,
            y,
            region.Width,
            cardHeight,
            GameplayHudVisualStyle.PanelRaised,
            width,
            height);
        EmitQuad(
            region.X,
            y,
            markerWidth,
            cardHeight,
            severityColor,
            width,
            height);

        if (severity ==
                HudAlertSeverity.Critical &&
            markerWidth >=
                4.0f * _scale)
        {
            EmitQuad(
                region.X +
                    markerWidth +
                    2.0f * _scale,
                y +
                    3.0f * _scale,
                2.0f * _scale,
                cardHeight -
                    6.0f * _scale,
                severityColor,
                width,
                height);
        }

        float iconSize =
            11.0f * _scale;
        float iconX =
            region.X +
            12.0f * _scale;

        EmitIcon(
            icon,
            iconX,
            y +
                (cardHeight - iconSize) *
                0.5f,
            iconSize,
            width,
            height);

        float textX =
            region.X +
            30.0f * _scale;
        float maximumTextWidth =
            MathF.Max(
                0.0f,
                region.Right -
                textX -
                4.0f * _scale);
        if (_text.Add(text, textX, y + 4f * _scale, 12f * _scale, TextColor,
            width, height, maximumTextWidth)) return;
        int maximumCharacters =
            Math.Max(
                0,
                (int)MathF.Floor(
                    maximumTextWidth /
                    (GlyphAdvance * _scale)));
        ReadOnlySpan<char> visible =
            text[
                ..Math.Min(
                    text.Length,
                    maximumCharacters)];

        EmitText(
            visible,
            textX,
            y +
                4.0f * _scale,
            severityColor,
            width,
            height);
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

    private static string CommandLabel(
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

    private static string ResolveFailureLabel(
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
        int height)
    {
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);
        string pattern =
            IconPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
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

    private ref struct HudTextBuilder
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
