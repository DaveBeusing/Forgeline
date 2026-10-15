using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Logistics;

namespace ForgeLine.Presentation;

internal sealed class PlayerActionDockHudSurface : IGameplayHudSurface
{
    private readonly PlayerActionDockHudRenderer _renderer;

    public PlayerActionDockHudSurface(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _renderer =
            new PlayerActionDockHudRenderer(
                graphics,
                runtimeAssets);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.ActionDock | GameplayHudRegion.PrimaryCommands;

    public UiTextDiagnostics TextDiagnostics => _renderer.TextDiagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _renderer.SkinDiagnostics;
    public int LastRenderedVertexCount => _renderer.LastRenderedVertexCount;

    public void Render(
        in GameplayHudRenderContext context) =>
        _renderer.Render(
            context.Graphics,
            context.Snapshot,
            context.ActionPanel,
            context.TacticalTargeting,
            context.ActiveFormation,
            context.Layout);

    public void Dispose() =>
        _renderer.Dispose();
}

internal sealed class PlayerActionDockHudRenderer : IDisposable
{
    private const int MaxVertices = 262_144;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 1.0f;
    private const float GlyphAdvance = 6.4f;

    private static readonly Vector4 PanelColor =
        GameplayHudVisualStyle.PanelBackground;
    private static readonly Vector4 CardColor =
        GameplayHudVisualStyle.PanelRaised;
    private static readonly Vector4 SelectedColor =
        GameplayHudVisualStyle.PanelSelected;
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

    public PlayerActionDockHudRenderer(
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
        try { _skin = new(graphics, runtimeAssets, icons: true, portraits: true); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }

    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext graphics,
        PresentationSnapshot snapshot,
        in PlayerActionPanelView panel,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation,
        in GameplayHudLayout layout)
    {
        ThrowIfDisposed();
        _text.Begin();
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphics.Width > 0 && graphics.Height > 0) _skin.Begin(graphics);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (graphics.Width <= 0 ||
            graphics.Height <= 0 ||
            layout.ActionDock.IsEmpty ||
            snapshot.PlayerExperience is
                PlayerExperienceSnapshot experience &&
            experience.IsMatchComplete)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _scale =
            layout.Scale;

        EmitPrimaryCommands(snapshot, panel, targeting, layout, graphics.Width, graphics.Height);
        _scale = PlayerActionDockInteractionLayout.Scale(layout);

        EmitModeBar(
            panel.Mode,
            panel.Bindings,
            layout,
            graphics.Width,
            graphics.Height);

        PlayerActionSnapshot? actions =
            panel.SelectionPending ? null : PlayerActionDockHudModel.ResolveActions(snapshot);

        if (panel.IsOpen)
        {
            EmitExpandedDock(
                snapshot,
                actions,
                panel,
                targeting,
                activeFormation,
                layout,
                graphics.Width,
                graphics.Height);
        }
        else
        {
            EmitCollapsedStatus(
                snapshot,
                targeting,
                layout,
                graphics.Width,
                graphics.Height);
        }

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

    private void EmitPrimaryCommands(PresentationSnapshot snapshot, in PlayerActionPanelView panel, in TacticalTargetingView targeting,
        in GameplayHudLayout layout, int width, int height)
    {
        HudRect region = layout.PrimaryCommands;
        if (region.IsEmpty) return;
        float previousScale = _scale;
        _scale = ContextualCommandModel.TextScale(layout);
        if (!_skin.Panel(region, "panel.compact")) EmitQuad(region.X, region.Y, region.Width, region.Height, PanelColor, width, height);
        PrimaryCommandText(ContextualCommandModel.Status(snapshot), region.X + 5 * _scale,
            region.Y + 5 * _scale, region.Right - 5 * _scale, TextColor, 12, true, width, height);
        Span<char> promptBuffer = stackalloc char[128];
        bool localPending = ContextualCommandModel.HasPending(panel, snapshot);
        for (int i = 0; ContextualCommandModel.TryGet(snapshot, i, out var command, localPending, panel.SelectionPending, panel.Bindings); i++)
        {
            HudRect button = ContextualCommandModel.Button(layout, i);
            bool focused = panel.IsOpen && panel.Mode == command.Mode && (command.OpensMode || panel.SelectedIndex == command.ItemIndex);
            bool hovered = panel.ContextualHoveredIndex == i;
            var visual = GameplayHudVisualStyle.ResolveItemState(focused, hovered, hovered && panel.ContextualPressed, command.Availability.CanActivate);
            if (!_skin.Control(button, command.Availability.CanActivate, focused, hovered, hovered && panel.ContextualPressed)) EmitQuad(button.X, button.Y, button.Width, button.Height, visual.Fill, width, height);
            EmitItemStateCue(button, visual, width, height);
            PrimaryCommandText(command.Label, button.X + 5 * _scale, button.Y + (_text.Ready ? 3 : 4) * _scale,
                button.Right - 4 * _scale, TextColor, 12, true, width, height);
            var prompt = new HudTextBuilder(promptBuffer);
            if (command.Availability.CanActivate)
            {
                prompt.Append(hovered && panel.ContextualPressed ? "PRESS " : focused ? "FOCUS " : hovered ? "HOVER " : string.Empty);
                prompt.Append(command.Shortcut);
            }
            else prompt.Append(command.Availability.DisabledReason);
            PrimaryCommandText(prompt.Written, button.X + 5 * _scale, button.Y + (_text.Ready ? 17 : 18) * _scale, button.Right - 4 * _scale,
                MutedTextColor, 10, false, width, height);
        }
        string footer = targeting.IsActive ? "TARGETING - ESC CANCELS" :
            localPending || snapshot.PlayerActions?.PendingCommandCount > 0 ? "REQUEST PENDING - WAIT FOR RESULT" : (panel.Bindings ?? GameplayBindingRegistry.Default).DockModesPrompt;
        PrimaryCommandText(footer, region.X + 5 * _scale, region.Bottom - 14 * _scale,
            region.Right - 5 * _scale, MutedTextColor, 10, false, width, height);
        _scale = previousScale;
    }

    private void PrimaryCommandText(ReadOnlySpan<char> text, float x, float y, float right, Vector4 color,
        float size, bool bold, int width, int height)
    {
        if (_text.Ready) DockText(text, x, y, right, color, size, bold, width, height);
        else EmitClippedText(text, x, y, right, color, width, height);
    }

    private void EmitModeBar(PlayerActionPanelMode activeMode, GameplayBindingRegistry? bindings,
        in GameplayHudLayout layout, int width, int height)
    {
        var bar = PlayerActionDockInteractionLayout.GetModeBarRect(layout);
        if (!_skin.Panel(bar, "panel.compact")) EmitQuad(bar.X, bar.Y, bar.Width, bar.Height, GameplayHudVisualStyle.PanelHeader, width, height);
        Span<char> buffer = stackalloc char[40];
        for (int i = 0; i < 7; i++)
        {
            var mode = PlayerActionDockInteractionLayout.ModeForButton(i);
            var rect = PlayerActionDockInteractionLayout.GetModeButtonRect(layout, i);
            bool selected = mode == activeMode;
            if (!_skin.Control(rect, selected: selected, role: "control.tab")) EmitQuad(rect.X, rect.Y, rect.Width, rect.Height,
                selected ? GameplayHudVisualStyle.PanelSelected : CardColor, width, height);
            if (selected) EmitQuad(rect.X, rect.Bottom - 3 * _scale, rect.Width, 3 * _scale,
                GameplayHudVisualStyle.Focus, width, height);
            var text = new HudTextBuilder(buffer);
            text.Append(PlayerActionDockHudModel.ResolveModeShortcut(mode, bindings)); text.Append(" ");
            text.Append(PlayerActionDockHudModel.ResolveModeLabel(mode));
            DockText(text.Written, rect.X + 4 * _scale, rect.Y + 8 * _scale,
                rect.Right - 3 * _scale, selected ? TextColor : MutedTextColor, 11, selected, width, height);
        }
    }

    private void EmitExpandedDock(
        PresentationSnapshot snapshot,
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        HudRect bar =
            PlayerActionDockInteractionLayout.GetModeBarRect(
                layout);

        if (!_skin.Panel(new HudRect(layout.ActionDock.X, bar.Bottom, layout.ActionDock.Width, MathF.Max(0, layout.ActionDock.Bottom - bar.Bottom)))) EmitQuad(
            layout.ActionDock.X,
            bar.Bottom,
            layout.ActionDock.Width,
            MathF.Max(
                0.0f,
                layout.ActionDock.Bottom -
                    bar.Bottom),
            PanelColor,
            width,
            height);

        EmitHeader(
            snapshot,
            actions,
            panel,
            targeting,
            activeFormation,
            layout,
            width,
            height);

        var motion = panel.Motion ?? UiMotionView.Settled;
        if (motion.Reveal < 1)
            EmitQuad(layout.ActionDock.X, bar.Bottom, layout.ActionDock.Width * Math.Clamp(motion.Reveal, 0, 1),
                _scale, GameplayHudVisualStyle.Focus, width, height);
        if (motion.Result > 0 && snapshot.PlayerExperience is { } experience && experience.Tick == snapshot.Tick)
        {
            var resultColor = experience.Feedback.State == PlayerCommandFeedbackState.Accepted ? GameplayHudVisualStyle.Focus : GameplayHudVisualStyle.Warning;
            EmitQuad(layout.ActionDock.X, bar.Bottom, 3 * _scale,
                PlayerActionDockInteractionLayout.HeaderHeight(layout) * Math.Clamp(motion.Result, 0, 1), resultColor, width, height);
        }

        EmitCards(
            actions,
            panel,
            layout,
            width,
            height);

        EmitFooter(
            actions,
            panel,
            layout,
            width,
            height);
    }

    private void EmitHeader(PresentationSnapshot snapshot, PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel, in TacticalTargetingView targeting, FormationTemplate formation,
        in GameplayHudLayout layout, int width, int height)
    {
        var header = PlayerActionDockInteractionLayout.GetHeaderRect(layout);
        bool compact = PlayerActionDockInteractionLayout.Compact(layout);
        if (!_skin.Panel(header, "panel.raised")) EmitQuad(header.X, header.Y, header.Width, header.Height, GameplayHudVisualStyle.PanelHeader, width, height);
        Span<char> buffer = stackalloc char[320]; var text = new HudTextBuilder(buffer);
        text.Append(PlayerActionDockHudModel.ResolveModeLabel(panel.Mode));
        if (snapshot.PlayerExperience is { } experience && experience.Tick == snapshot.Tick &&
            experience.Selection.HasSingleEntityDetails && actions is not null)
        { text.Append(" / "); text.Append(experience.Selection.DisplayName); }
        DockText(text.Written, header.X + 7 * _scale, header.Y + 3 * _scale,
            header.Right - 58 * _scale, TextColor, 13, true, width, height);

        bool pending = actions?.PendingCommandCount > 0 || ContextualCommandModel.HasPending(panel, snapshot);
        if (pending || panel.SelectionPending) _skin.State(header, UiSkinState.Pending);
        else if (snapshot.PlayerExperience is { } feedbackExperience && ResourcePowerHudModel.IsCommandFeedbackVisible(feedbackExperience))
            _skin.State(header, feedbackExperience.Feedback.State switch
            {
                PlayerCommandFeedbackState.Accepted => UiSkinState.Accepted,
                PlayerCommandFeedbackState.Rejected => UiSkinState.Error,
                PlayerCommandFeedbackState.Partial => UiSkinState.Warning,
                _ => UiSkinState.Normal
            });
        text = new(buffer);
        if (panel.SelectionPending) text.Append("UPDATING SELECTION");
        else if (pending) text.Append("REQUEST PENDING / AWAIT RESULT");
        else if (snapshot.PlayerExperience is { } e && ResourcePowerHudModel.IsCommandFeedbackVisible(e))
        {
            text.Append("LAST "); text.Append(PlayerActionDockHudModel.ResolveFeedbackStateLabel(e.Feedback.State));
            text.Append(" "); text.Append(PlayerActionDockHudModel.ResolveCommandFeedbackReason(e.Feedback));
        }
        else if (compact) AppendHeaderContext(ref text, actions, panel, targeting, formation);
        else text.Append("CAPTURED ACTIONS / SELECT CARD TO INSPECT");
        DockText(text.Written, header.X + 7 * _scale, header.Y + 19 * _scale, header.Right - 7 * _scale,
            pending ? GameplayHudVisualStyle.Warning : MutedTextColor, compact ? 10 : 11, false, width, height);
        if (!compact)
        {
            text = new(buffer); AppendHeaderContext(ref text, actions, panel, targeting, formation);
            DockText(text.Written, header.X + 7 * _scale, header.Y + 34 * _scale, header.Right - 7 * _scale,
                MutedTextColor, 11, false, width, height);
            int count = PlayerActionDockInteractionLayout.GetItemCount(panel.Mode, actions);
            int first = PlayerActionDockInteractionLayout.FirstVisibleItem(layout, panel.SelectedIndex, count);
            text = new(buffer); text.Append("CARDS "); text.Append(count == 0 ? 0 : first + 1); text.Append("-");
            text.Append(Math.Min(count, first + PlayerActionDockInteractionLayout.MaximumVisibleItems(layout)));
            text.Append(" / "); text.Append(count); text.Append("  ");
            text.Append((panel.Bindings ?? GameplayBindingRegistry.Default).Prompt(GameplayAction.NextItem)); text.Append(" NEXT / WHEEL PAGE");
            DockText(text.Written, header.X + 7 * _scale, header.Y + 49 * _scale, header.Right - 7 * _scale,
                MutedTextColor, 10, false, width, height);
        }
        int items = PlayerActionDockInteractionLayout.GetItemCount(panel.Mode, actions);
        int start = PlayerActionDockInteractionLayout.FirstVisibleItem(layout, panel.SelectedIndex, items);
        int capacity = PlayerActionDockInteractionLayout.MaximumVisibleItems(layout);
        for (int i = 0; i < 2; i++)
        {
            var rect = PlayerActionDockInteractionLayout.GetPageButtonRect(layout, i);
            bool enabled = i == 0 ? start > 0 : start + capacity < items;
            if (!_skin.Control(rect, enabled)) EmitQuad(rect.X, rect.Y, rect.Width, rect.Height, enabled ? CardColor : PanelColor, width, height);
            DockText(i == 0 ? "<" : ">", rect.X + 6 * _scale, rect.Y + 3 * _scale, rect.Right - _scale,
                enabled ? TextColor : MutedTextColor, 12, true, width, height);
        }
    }

    private static void AppendHeaderContext(
        ref HudTextBuilder text,
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation)
    {
        if (actions is null)
        {
            text.Append("NO ACTION SNAPSHOT");
            return;
        }

        switch (panel.Mode)
        {
            case PlayerActionPanelMode.Construction:
                AppendConstructionHeader(
                    ref text,
                    actions,
                    panel.SelectedIndex);
                break;

            case PlayerActionPanelMode.Production:
                AppendProductionHeader(
                    ref text,
                    actions.Production,
                    panel);
                break;

            case PlayerActionPanelMode.UnitProduction:
                AppendUnitProductionHeader(
                    ref text,
                    actions.UnitProduction,
                    panel);
                break;

            case PlayerActionPanelMode.Logistics:
                AppendLogisticsHeader(
                    ref text,
                    actions.Logistics,
                    panel);
                break;

            case PlayerActionPanelMode.Supply:
                AppendSupplyHeader(
                    ref text,
                    actions.Supply,
                    panel);
                break;

            case PlayerActionPanelMode.Tactical:
                AppendTacticalHeader(
                    ref text,
                    actions.Tactical,
                    targeting,
                    activeFormation);
                break;

            case PlayerActionPanelMode.Technology:
                AppendTechnologyHeader(
                    ref text,
                    actions.Technology,
                    panel.SelectedIndex);
                break;
        }
    }

    private static void AppendConstructionHeader(
        ref HudTextBuilder text,
        PlayerActionSnapshot actions,
        int selectedIndex)
    {
        if (selectedIndex < 0 ||
            selectedIndex >=
                actions.Construction.Count)
        {
            text.Append("SELECT BUILDING");
            return;
        }

        PlayerConstructionActionReadModel action =
            actions.Construction[
                selectedIndex];

        text.Append(
            action.HasRequiredResources
                ? "READY  "
                : "MATERIALS  ");
        AppendAmounts(
            ref text,
            action.Costs);

        if (action.RequiresResourceDeposit)
        {
            text.Append("  DEPOSIT");
        }
    }

    private static void AppendProductionHeader(
        ref HudTextBuilder text,
        PlayerProductionFacilityActionReadModel? facility,
        in PlayerActionPanelView panel)
    {
        if (facility is null)
        {
            text.Append("SELECT ONE OWNED PROCESSOR");
            return;
        }

        text.Append(
            PlayerActionDockHudModel.ResolveProductionStatusLabel(
                facility.Status));
        text.Append(" ");
        text.Append(
            facility.Progress *
            100.0,
            "F0");
        text.Append("%");

        string block =
            PlayerActionDockHudModel.ResolveProductionBlockLabel(
                facility.BlockReason);
        if (block.Length > 0)
        {
            text.Append(" ");
            text.Append(block);
        }

        text.Append("  ");
        text.Append(
            PlayerActionDockHudModel.ResolvePriorityLabel(
                panel.Priority));
        text.Append(" ");
        text.Append(
            PlayerActionDockHudModel.ResolveProductionModeLabel(
                panel.ProductionMode));

        if (panel.ProductionMode ==
            ProductionRequestMode.DesiredStock)
        {
            text.Append(" ");
            text.Append(
                panel.DesiredStockQuantity,
                "F0");
        }
    }

    private static void AppendUnitProductionHeader(
        ref HudTextBuilder text,
        PlayerUnitProductionFacilityActionReadModel? facility,
        in PlayerActionPanelView panel)
    {
        if (facility is null)
        {
            text.Append("SELECT ONE OWNED BARRACKS OR FACTORY");
            return;
        }

        text.Append(
            PlayerActionDockHudModel.ResolveUnitProductionStatusLabel(
                facility.Status));
        text.Append(" ");
        text.Append(
            facility.Progress *
            100.0,
            "F0");
        text.Append("%");

        string block =
            PlayerActionDockHudModel.ResolveUnitProductionBlockLabel(
                facility.BlockReason);
        if (block.Length > 0)
        {
            text.Append(" ");
            text.Append(block);
        }

        text.Append("  PRIOR ");
        text.Append(
            PlayerActionDockHudModel.ResolvePriorityLabel(
                panel.Priority));
        text.Append(
            facility.RallyPoint.HasValue
                ? "  RALLY SET"
                : "  RALLY NONE");
    }

    private static void AppendLogisticsHeader(
        ref HudTextBuilder text,
        PlayerLogisticsActionReadModel? logistics,
        in PlayerActionPanelView panel)
    {
        if (logistics is null)
        {
            text.Append("SELECT ONE OWNED LOGISTICS ENTITY");
            return;
        }

        if (logistics.Cargo is
            PlayerCargoStatusReadModel cargo)
        {
            text.Append("CARGO ");
            text.Append(
                cargo.CargoQuantity,
                "F0");
            text.Append("/");
            text.Append(
                cargo.Capacity,
                "F0");
            text.Append(" ");
            text.Append(
                PlayerActionDockHudModel.ResolveCargoStateLabel(
                    cargo.Lifecycle));

            string cargoIssue =
                cargo.FailureReason !=
                    CargoTransportFailureReason.None
                    ? PlayerActionDockHudModel.ResolveCargoFailureLabel(
                        cargo.FailureReason)
                    : PlayerActionDockHudModel.ResolveCargoWaitLabel(
                        cargo.WaitReason);

            if (cargoIssue.Length > 0)
            {
                text.Append(" ");
                text.Append(
                    cargoIssue);
            }

            return;
        }

        text.Append("POLICY ");
        text.Append(
            panel.StockMinimum,
            "F0");
        text.Append("/");
        text.Append(
            panel.StockTarget,
            "F0");
        text.Append("/");
        text.Append(
            panel.StockMaximum,
            "F0");
        text.Append(" ");
        text.Append(
            PlayerActionDockHudModel.ResolveLogisticsPriorityLabel(
                panel.LogisticsPriority));
        text.Append(" ");
        text.Append(
            panel.StockThresholdField switch
            {
                PlayerStockThresholdField.Minimum =>
                    "EDIT MIN",
                PlayerStockThresholdField.Maximum =>
                    "EDIT MAX",
                _ =>
                    "EDIT TARGET"
            });
    }

    private static void AppendSupplyHeader(
        ref HudTextBuilder text,
        PlayerSupplyActionReadModel? supply,
        in PlayerActionPanelView panel)
    {
        if (!supply.HasValue)
        {
            text.Append("SELECT ONE OWNED SUPPLY UNIT");
            return;
        }

        PlayerSupplyActionReadModel value =
            supply.Value;

        text.Append(
            SelectionInspectorHudModel.ResolveSupplyLabel(
                value.Status));
        text.Append("  F ");
        text.Append(
            value.FuelFraction *
            100.0,
            "F0");
        text.Append("% A ");
        text.Append(
            value.AmmunitionFraction *
            100.0,
            "F0");
        text.Append("%  ");
        text.Append(
            PlayerActionDockHudModel.ResolveSupplyPriorityLabel(
                panel.SupplyPriority));
        text.Append("  PROVIDER ");
        text.Append(
            PlayerActionDockHudModel.ResolveSupplyProviderStateLabel(
                value.ProviderState));
    }

    private static void AppendTechnologyHeader(
        ref HudTextBuilder text,
        IReadOnlyList<PlayerTechnologyActionReadModel> technologies,
        int selectedIndex)
    {
        if (selectedIndex < 0 ||
            selectedIndex >= technologies.Count)
        {
            text.Append("SELECT TECHNOLOGY");
            return;
        }

        PlayerTechnologyActionReadModel technology =
            technologies[selectedIndex];

        text.Append(
            PlayerActionDockHudModel.ResolveTechnologyDomainLabel(
                technology.Domain));
        text.Append(" ");
        text.Append(
            PlayerActionDockHudModel.ResolveTechnologyPhaseLabel(
                technology.Phase));
        text.Append("  ");

        if (technology.State ==
            PlayerTechnologyState.Researching)
        {
            text.Append("RESEARCH ");
            text.Append(
                technology.Progress *
                100.0,
                "F0");
            text.Append("%");
        }
        else if (technology.State ==
                 PlayerTechnologyState.Completed)
        {
            text.Append("COMPLETED");
        }
        else if (technology.State ==
                 PlayerTechnologyState.Available)
        {
            text.Append("AVAILABLE");
        }
        else
        {
            text.Append(
                PlayerActionDockHudModel.ResolveTechnologyBlockLabel(
                    technology.BlockReason));
        }

        text.Append("  FAC ");
        text.Append(
            technology.RequiredFacilityName);
        if (!technology.Facility.IsValid)
        {
            text.Append(" MISSING");
        }

        text.Append("  PWR ");
        text.Append(
            technology.CurrentPowerFraction *
            100.0,
            "F0");
        text.Append("/");
        text.Append(
            technology.RequiredPowerFraction *
            100.0,
            "F0");
        text.Append("%  COST ");
        AppendAmounts(
            ref text,
            technology.Costs);

        if (technology.Prerequisites.Count > 0)
        {
            int completed = 0;
            for (int index = 0;
                 index < technology.Prerequisites.Count;
                 index++)
            {
                if (technology.Prerequisites[index].Completed)
                {
                    completed++;
                }
            }

            text.Append("  PRE ");
            text.Append(completed);
            text.Append("/");
            text.Append(
                technology.Prerequisites.Count);
        }
    }

    private static void AppendTacticalHeader(
        ref HudTextBuilder text,
        PlayerTacticalActionReadModel? tactical,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation)
    {
        if (tactical is null)
        {
            text.Append("SELECT OWNED UNITS");
            return;
        }

        text.Append("SEL ");
        text.Append(
            tactical.RequestedSelectionCount);
        text.Append(" COMBAT ");
        text.Append(
            tactical.CombatEligibleCount);
        text.Append(" REJ ");
        text.Append(
            tactical.RejectedSelectionCount);
        text.Append(" TARGETS ");
        text.Append(
            tactical.Targets.Count);
        text.Append(" CRIT ");
        text.Append(
            tactical.CriticalSupplyCount);
        text.Append(" RESUP ");
        text.Append(
            tactical.ResupplyingCount);
        text.Append(" FORM ");
        text.Append(
            activeFormation switch
            {
                FormationTemplate.Line =>
                    "LINE",
                FormationTemplate.Column =>
                    "COLUMN",
                FormationTemplate.Wedge =>
                    "WEDGE",
                _ =>
                    "COMPACT"
            });

        if (targeting.IsActive)
        {
            text.Append(" TARGETING");
        }
        else if (tactical.MixedOrderState)
        {
            text.Append(" ORDER MIXED");
        }
        else if (tactical.HasCommonOrder)
        {
            text.Append(" ORDER ");
            text.Append(
                ResolveCombatOrderLabel(
                    tactical.CurrentOrder));
        }
    }

    private void EmitCards(PlayerActionSnapshot? actions, in PlayerActionPanelView panel,
        in GameplayHudLayout layout, int width, int height)
    {
        int count = PlayerActionDockInteractionLayout.GetItemCount(panel.Mode, actions);
        int first = PlayerActionDockInteractionLayout.FirstVisibleItem(layout, panel.SelectedIndex, count);
        int end = Math.Min(count, first + PlayerActionDockInteractionLayout.MaximumVisibleItems(layout));
        if (end <= first)
        {
            var rect = PlayerActionDockInteractionLayout.GetCardsRect(layout);
            _skin.Panel(rect, "panel.inset");
            DockText(panel.SelectionPending ? "UPDATING SELECTION" : "SELECT OWNED CONTEXT / NO AVAILABLE CARDS",
                rect.X + 6 * _scale, rect.Y + 6 * _scale, rect.Right - 6 * _scale,
                MutedTextColor, 11, false, width, height);
            return;
        }
        Span<char> buffer = stackalloc char[256];
        bool pending = actions?.PendingCommandCount > 0 || panel.ContextualPending &&
            panel.ContextualActivationTick == actions?.Tick && panel.ContextualSessionId == actions?.SessionId;
        bool compact = PlayerActionDockInteractionLayout.Compact(layout);
        for (int index = first; index < end; index++)
        {
            var rect = PlayerActionDockInteractionLayout.GetCardRect(layout, index, first);
            var state = PlayerActionDockHudModel.ResolveItemState(panel.Mode, index, actions, pending);
            bool selected = index == panel.SelectedIndex; bool hovered = index == panel.HoveredIndex;
            var visual = GameplayHudVisualStyle.ResolveItemState(selected, hovered, hovered && panel.PointerPressed, state.CanActivate);
            if (!_skin.Control(rect, state.CanActivate, selected, hovered, hovered && panel.PointerPressed))
                EmitQuad(rect.X, rect.Y, rect.Width, rect.Height, visual.Fill, width, height);
            var work = EconomicCardVisual.Resolve(panel.Mode, index, actions, pending);
            _skin.State(rect, work.State);
            float focus = selected ? .6f + .4f * Math.Clamp((panel.Motion ?? UiMotionView.Settled).Focus, 0, 1) : 1;
            EmitItemStateCue(rect, visual, width, height, focus);
            var identity = EconomicCardVisual.Portrait(panel.Mode, index, actions);
            bool portrait = _skin.Portrait(identity.Kind, identity.Id, new(rect.X + 5 * _scale, rect.Y + 4 * _scale,
                (compact ? 24 : 30) * _scale, (compact ? 24 : 30) * _scale));
            if (!portrait) EmitIcon(PlayerActionDockHudModel.ResolveItemIcon(panel.Mode, index, actions), rect.X + 6 * _scale,
                rect.Y + 5 * _scale, 14 * _scale, width, height);
            float textX = rect.X + (portrait ? compact ? 34 : 40 : 25) * _scale;
            DockText(PlayerActionDockHudModel.ResolveItemTitle(panel.Mode, index, actions),
                textX, rect.Y + 4 * _scale, rect.Right - 5 * _scale,
                TextColor, compact ? 11 : 13, true, width, height);
            var text = new HudTextBuilder(buffer);
            if (_skin.Ready && work.Label.Length > 0) text.Append(work.Label);
            else if (pending) text.Append("REQUEST PENDING");
            else if (!state.CanActivate) text.Append(state.DisabledReason);
            else AppendItemStatus(ref text, panel.Mode, index, actions, panel);
            DockText(text.Written, textX, rect.Y + (compact ? 18 : 22) * _scale,
                rect.Right - 5 * _scale, state.CanActivate ? MutedTextColor : GameplayHudVisualStyle.Warning,
                compact ? 10 : 11, false, width, height);
            text = new(buffer); AppendCardDetail(ref text, panel.Mode, index, actions);
            DockText(text.Written, rect.X + 6 * _scale, rect.Y + (compact ? 30 : 39) * _scale,
                rect.Right - 5 * _scale, MutedTextColor, 10, false, width, height);
            double? progress = _skin.Ready ? work.Progress : CardProgress(panel.Mode, index, actions);
            if (progress.HasValue && (_skin.Ready || !compact))
            {
                if (_skin.Bar(new(rect.X + 6 * _scale, rect.Bottom - 4 * _scale, rect.Width - 12 * _scale, 2 * _scale),
                    progress.Value, work.State == UiSkinState.Warning ? GameplayHudVisualStyle.Warning : GameplayHudVisualStyle.Focus)) continue;
                EmitQuad(rect.X + 6 * _scale, rect.Bottom - 4 * _scale, rect.Width - 12 * _scale,
                    2 * _scale, GameplayHudVisualStyle.QuietBorder, width, height);
                EmitQuad(rect.X + 6 * _scale, rect.Bottom - 4 * _scale,
                    (rect.Width - 12 * _scale) * (float)Math.Clamp(progress.Value, 0, 1),
                    2 * _scale, GameplayHudVisualStyle.Focus, width, height);
            }
        }
    }

    private static double? CardProgress(PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions) => mode switch
    {
        PlayerActionPanelMode.Production when actions?.Production is { } p && index >= p.Recipes.Count &&
            p.Requests[index - p.Recipes.Count].IsActive => p.Progress,
        PlayerActionPanelMode.UnitProduction when actions?.UnitProduction is { } u && index >= u.Units.Count &&
            u.Requests[index - u.Units.Count].IsActive => u.Progress,
        PlayerActionPanelMode.Technology when actions is not null && actions.Technology[index].ActiveRequest.IsValid => actions.Technology[index].Progress,
        _ => null
    };

    private static void AppendCardDetail(ref HudTextBuilder text, PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions)
    {
        if (actions is null) return;
        switch (mode)
        {
            case PlayerActionPanelMode.Construction:
                text.Append("COST "); AppendAmounts(ref text, actions.Construction[index].Costs);
                if (actions.Construction[index].RequiresResourceDeposit) text.Append(" / DEPOSIT");
                break;
            case PlayerActionPanelMode.Production:
                var p = actions.Production!;
                if (index < p.Recipes.Count)
                { text.Append("IN "); AppendAmounts(ref text, p.Recipes[index].Inputs); text.Append(" OUT "); AppendAmounts(ref text, p.Recipes[index].Outputs); }
                else { var job = p.Requests[index - p.Recipes.Count]; text.Append(PlayerActionDockHudModel.ResolveProductionModeLabel(job.Mode)); text.Append(" / "); text.Append(PlayerActionDockHudModel.ResolvePriorityLabel(job.Priority)); }
                break;
            case PlayerActionPanelMode.UnitProduction:
                var u = actions.UnitProduction!;
                if (index < u.Units.Count) { text.Append("COST "); AppendAmounts(ref text, u.Units[index].Costs); }
                else { text.Append("PROGRESS "); text.Append(u.Progress * 100, "F0"); text.Append("% / CANCEL QUEUED JOB"); }
                break;
            case PlayerActionPanelMode.Logistics:
                var stock = actions.Logistics!.Policies[index];
                text.Append("MIN "); text.Append(stock.DesiredMinimum, "F0"); text.Append(" MAX "); text.Append(stock.DesiredMaximum, "F0");
                text.Append(stock.Enabled ? " / ENABLED" : " / DISABLED");
                break;
            case PlayerActionPanelMode.Supply:
                var supply = actions.Supply!.Value;
                text.Append(index == 0 ? "FUEL " : index == 1 ? "AMMO " : "PROVIDER ");
                if (index < 2) { text.Append((index == 0 ? supply.FuelFraction : supply.AmmunitionFraction) * 100, "F0"); text.Append("% NOW / POLICY DRAFT ABOVE"); }
                else { text.Append(PlayerActionDockHudModel.ResolveSupplyProviderStateLabel(supply.ProviderState)); text.Append(" / REQUEST IS NOT TRANSFER"); }
                break;
            case PlayerActionPanelMode.Technology:
                var technology = actions.Technology[index];
                text.Append(technology.RequiredFacilityName); text.Append(" / COST "); AppendAmounts(ref text, technology.Costs);
                break;
            case PlayerActionPanelMode.Tactical:
                text.Append("ELIGIBLE "); text.Append(actions.Tactical!.CombatEligibleCount); text.Append(" / SELECTED ");
                text.Append(actions.Tactical.RequestedSelectionCount); text.Append(" / INTENT ONLY");
                break;
        }
    }

    private void EmitItemStateCue(
        in HudRect rect,
        in HudStateVisual visual,
        int width,
        int height,
        float focus = 1f)
    {
        float rail =
            GameplayHudVisualStyle.StateRailThickness *
            _scale;
        float border =
            GameplayHudVisualStyle.BorderThickness *
            _scale;

        switch (visual.Pattern)
        {
            case HudStatePattern.Underline:
                EmitQuad(
                    rect.X,
                    rect.Bottom -
                        GameplayHudVisualStyle.StateRailThickness *
                        _scale,
                    rect.Width,
                    GameplayHudVisualStyle.StateRailThickness *
                    _scale,
                    visual.Border,
                    width,
                    height);
                break;

            case HudStatePattern.LeftRail:
                EmitQuad(
                    rect.X,
                    rect.Y,
                    rail,
                    rect.Height * Math.Clamp(focus, 0, 1),
                    visual.Border,
                    width,
                    height);
                break;

            case HudStatePattern.Outline:
                EmitQuad(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    border,
                    visual.Border,
                    width,
                    height);
                EmitQuad(
                    rect.X,
                    rect.Bottom - border,
                    rect.Width,
                    border,
                    visual.Border,
                    width,
                    height);
                EmitQuad(
                    rect.X,
                    rect.Y,
                    border,
                    rect.Height,
                    visual.Border,
                    width,
                    height);
                EmitQuad(
                    rect.Right - border,
                    rect.Y,
                    border,
                    rect.Height,
                    visual.Border,
                    width,
                    height);
                break;

            case HudStatePattern.DoubleRail:
                EmitQuad(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    border,
                    visual.Border,
                    width,
                    height);
                EmitQuad(
                    rect.X,
                    rect.Bottom - border,
                    rect.Width,
                    border,
                    visual.Border,
                    width,
                    height);
                break;
        }
    }

    private static void AppendItemStatus(
        ref HudTextBuilder text,
        PlayerActionPanelMode mode,
        int index,
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel)
    {
        if (actions is null)
        {
            return;
        }

        switch (mode)
        {
            case PlayerActionPanelMode.Construction:
            {
                PlayerConstructionActionReadModel action =
                    actions.Construction[index];
                text.Append(
                    action.RequiresResourceDeposit
                        ? "READY DEPOSIT"
                        : "READY");
                break;
            }

            case PlayerActionPanelMode.Production:
            {
                PlayerProductionFacilityActionReadModel facility =
                    actions.Production!;
                if (index <
                    facility.Recipes.Count)
                {
                    PlayerProductionRecipeActionReadModel recipe =
                        facility.Recipes[index];
                    text.Append(
                        recipe.HasInputs
                            ? "INPUT OK"
                            : "INPUT MISSING");
                }
                else
                {
                    PlayerProductionRequestReadModel request =
                        facility.Requests[
                            index -
                            facility.Recipes.Count];
                    text.Append("QUEUE ");
                    text.Append(
                        request.Paused
                            ? "PAUSED "
                            : request.IsActive
                                ? "ACTIVE "
                                : "WAIT ");
                    text.Append(
                        PlayerActionDockHudModel.ResolvePriorityLabel(
                            request.Priority));
                    text.Append(" ");
                    text.Append(
                        PlayerActionDockHudModel.ResolveProductionModeLabel(
                            request.Mode));
                }

                break;
            }

            case PlayerActionPanelMode.UnitProduction:
            {
                PlayerUnitProductionFacilityActionReadModel facility =
                    actions.UnitProduction!;
                if (index <
                    facility.Units.Count)
                {
                    PlayerUnitProductionActionReadModel unit =
                        facility.Units[index];
                    text.Append(
                        unit.HasInputs
                            ? "INPUT OK "
                            : "INPUT MISSING ");
                    text.Append(
                        unit.ProductionTicks);
                    text.Append("T");
                }
                else
                {
                    PlayerUnitProductionRequestReadModel request =
                        facility.Requests[
                            index -
                            facility.Units.Count];
                    text.Append("QUEUE ");
                    text.Append(
                        request.Paused
                            ? "PAUSED "
                            : request.IsActive
                                ? "ACTIVE "
                                : "WAIT ");
                    text.Append(
                        PlayerActionDockHudModel.ResolvePriorityLabel(
                            request.Priority));
                }

                break;
            }

            case PlayerActionPanelMode.Logistics:
            {
                PlayerStockPolicyActionReadModel policy =
                    actions.Logistics!.Policies[
                        index];
                text.Append("STOCK ");
                text.Append(
                    policy.CurrentQuantity,
                    "F0");

                if (policy.HasPolicy)
                {
                    text.Append(" TARGET ");
                    text.Append(
                        policy.DesiredTarget,
                        "F0");
                }
                else
                {
                    text.Append(" NO POLICY");
                }

                if (policy.DistributionState !=
                    PlayerDistributionActionState.None)
                {
                    text.Append(" ");
                    text.Append(
                        PlayerActionDockHudModel.ResolveDistributionStateLabel(
                            policy.DistributionState));
                }

                string issue =
                    policy.FailureReason !=
                        LogisticsTransportRequestFailureReason.None
                        ? PlayerActionDockHudModel.ResolveTransportFailureLabel(
                            policy.FailureReason)
                        : PlayerActionDockHudModel.ResolveBottleneckLabel(
                            policy.BottleneckReason);
                if (issue.Length > 0)
                {
                    text.Append(" ");
                    text.Append(issue);
                }

                break;
            }

            case PlayerActionPanelMode.Supply:
            {
                PlayerSupplyActionReadModel supply =
                    actions.Supply!.Value;
                if (index == 0)
                {
                    text.Append(
                        panel.AutomaticFuelThreshold *
                        100.0,
                        "F0");
                    text.Append("% ");
                    text.Append(
                        panel.AutomaticResupplyEnabled
                            ? "AUTO"
                            : "MANUAL");
                }
                else if (index == 1)
                {
                    text.Append(
                        panel.AutomaticAmmunitionThreshold *
                        100.0,
                        "F0");
                    text.Append("% ");
                    text.Append(
                        panel.AutomaticResupplyEnabled
                            ? "AUTO"
                            : "MANUAL");
                }
                else
                {
                    text.Append(
                        PlayerActionDockHudModel.ResolveSupplyProviderStateLabel(
                            supply.ProviderState));
                }

                break;
            }

            case PlayerActionPanelMode.Technology:
            {
                PlayerTechnologyActionReadModel technology =
                    actions.Technology[index];
                text.Append(
                    PlayerActionDockHudModel.ResolveTechnologyDomainLabel(
                        technology.Domain));
                text.Append(" ");
                text.Append(
                    PlayerActionDockHudModel.ResolveTechnologyPhaseLabel(
                        technology.Phase));
                text.Append(" ");
                text.Append(
                    technology.ResearchTicks);
                text.Append("T");
                break;
            }

            case PlayerActionPanelMode.Tactical:
            {
                PlayerTacticalActionReadModel tactical =
                    actions.Tactical!;
                if (index == 0)
                {
                    text.Append("TARGETS ");
                    text.Append(
                        tactical.Targets.Count);
                }
                else if (index == 5)
                {
                    text.Append("ARTILLERY ");
                    text.Append(
                        tactical.Artillery.Count);
                }
                else if (index == 6)
                {
                    text.Append("ACTIVE MISSION");
                }
                else if (index == 7)
                {
                    text.Append(
                        tactical.RetreatReason !=
                            RetreatRecoveryReason.None
                            ? "RECOVERY AVAILABLE"
                            : "RECOVERY ORDER");
                }
                else
                {
                    text.Append("READY");
                }

                break;
            }
        }
    }

    private void EmitFooter(PlayerActionSnapshot? actions, in PlayerActionPanelView panel,
        in GameplayHudLayout layout, int width, int height)
    {
        Span<char> buffer = stackalloc char[96];
        for (int index = 0; index < 6; index++)
        {
            var control = PlayerActionDockInteractionLayout.FooterControl(index);
            var rect = PlayerActionDockInteractionLayout.GetFooterButtonRect(layout, index);
            string label = control == PlayerActionDockControlKind.Activate
                ? PlayerActionDockHudModel.ResolvePrimaryActionLabel(panel.Mode, panel.SelectedIndex, actions)
                : PlayerActionDockHudModel.ResolveFooterLabel(control, panel.Mode);
            if (label.Length == 0) continue;
            bool enabled = PlayerActionDockHudModel.CanUseControl(control, panel.Mode, panel.SelectedIndex, panel, actions);
            if (!_skin.Control(rect, enabled)) EmitQuad(rect.X, rect.Y, rect.Width, rect.Height, enabled ? CardColor : GameplayHudVisualStyle.PanelDisabled, width, height);
            var text = new HudTextBuilder(buffer);
            text.Append((panel.Bindings ?? GameplayBindingRegistry.Default).Prompt(index switch
            { 0 => GameplayAction.Activate, 1 => GameplayAction.CancelJob, 2 => GameplayAction.PrimarySetting,
              3 => GameplayAction.SecondarySetting, 4 => GameplayAction.Decrease, _ => GameplayAction.Increase }));
            text.Append(" / "); text.Append(label);
            DockText(text.Written, rect.X + 5 * _scale, rect.Y + 4 * _scale, rect.Right - 4 * _scale,
                enabled ? TextColor : MutedTextColor, 11, index == 0, width, height);
        }
    }

    private void DockText(ReadOnlySpan<char> text, float x, float y, float right, Vector4 color,
        float size, bool bold, int width, int height)
    {
        if (_text.Add(text, x, y, size * _scale, color, width, height, MathF.Max(0, right - x), bold)) return;
        float previous = _scale; _scale *= size / 12;
        EmitClippedText(text, x, y, right, color, width, height); _scale = previous;
    }

    private void EmitCollapsedStatus(
        PresentationSnapshot snapshot,
        in TacticalTargetingView targeting,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        HudRect modeBar =
            PlayerActionDockInteractionLayout.GetModeBarRect(
                layout);
        float stripHeight =
            PlayerActionDockInteractionLayout.GetCollapsedRect(layout).Bottom - modeBar.Bottom - 3 * _scale;
        var strip =
            new HudRect(
                modeBar.X,
                modeBar.Bottom +
                    3.0f *
                    _scale,
                modeBar.Width,
                stripHeight);

        Span<char> buffer =
            stackalloc char[192];
        var text =
            new HudTextBuilder(
                buffer);
        RtsUiIcon icon =
            RtsUiIcon.StatusAlert;

        if (targeting.IsActive)
        {
            icon =
                targeting.Mode switch
                {
                    TacticalTargetingMode.Attack =>
                        RtsUiIcon.CommandAttack,
                    TacticalTargetingMode.AttackMove =>
                        RtsUiIcon.CommandAttackMove,
                    TacticalTargetingMode.Retreat =>
                        RtsUiIcon.CommandMove,
                    TacticalTargetingMode.FireMission =>
                        RtsUiIcon.UnitArtillery,
                    _ =>
                        RtsUiIcon.CursorSelect
                };
            text.Append("TARGET ");
            text.Append(
                ResolveTargetingModeLabel(
                    targeting.Mode));
            text.Append("  SEL ");
            text.Append(
                targeting.SelectedEntityCount);

            if (targeting.Mode ==
                TacticalTargetingMode.Attack)
            {
                text.Append("  TARGETS ");
                text.Append(
                    targeting.AvailableAttackTargets);
            }
            else if (targeting.Mode ==
                TacticalTargetingMode.FireMission)
            {
                text.Append("  CONTACTS ");
                text.Append(
                    targeting.KnownContacts);
                text.Append("  ROUNDS ");
                text.Append(
                    targeting.RequestedRounds);
            }
        }
        else if (snapshot.PlacementPreview is
            BuildingPlacementPreviewReadModel placement)
        {
            icon =
                placement.Preview.IsValid
                    ? RtsUiIcon.CommandBuild
                    : RtsUiIcon.CursorInvalid;
            text.Append("PLACE ");
            text.Append(
                placement.Preview.DisplayName);
            text.Append(" ");
            text.Append(
                placement.Preview.IsValid
                    ? "VALID"
                    : PlayerActionDockHudModel.ResolvePlacementFailureLabel(
                        placement.Preview.Failure));
        }
        else
        {
            return;
        }

        if (!_skin.Panel(strip, "panel.compact")) EmitQuad(
            strip.X,
            strip.Y,
            strip.Width,
            strip.Height,
            PanelColor,
            width,
            height);
        EmitIcon(
            icon,
            strip.X +
                5.0f *
                _scale,
            strip.Y +
                4.0f *
                _scale,
            11.0f *
                _scale,
            width,
            height);
        EmitClippedText(
            text.Written,
            strip.X +
                22.0f *
                _scale,
            strip.Y +
                5.0f *
                _scale,
            strip.Right -
                4.0f *
                _scale,
            TextColor,
            width,
            height);
    }

    private static void AppendAmounts(
        ref HudTextBuilder text,
        IReadOnlyList<PlayerActionResourceAmount> amounts)
    {
        for (int index = 0;
             index < amounts.Count;
             index++)
        {
            if (index > 0)
            {
                text.Append("  ");
            }

            PlayerActionResourceAmount amount =
                amounts[index];
            text.Append(
                amount.DisplayName);
            text.Append(" ");
            text.Append(
                amount.AvailableQuantity,
                "F0");
            text.Append("/");
            text.Append(
                amount.RequiredQuantity,
                "F0");
        }
    }

    private static string ResolveCombatOrderLabel(
        CombatOrderKind order) =>
        order switch
        {
            CombatOrderKind.Attack =>
                "ATTACK",
            CombatOrderKind.AttackMove =>
                "ATTACK MOVE",
            CombatOrderKind.Stop =>
                "STOP",
            CombatOrderKind.HoldPosition =>
                "HOLD",
            CombatOrderKind.Retreat =>
                "RETREAT",
            _ =>
                "NONE"
        };

    private static string ResolveTargetingModeLabel(
        TacticalTargetingMode mode) =>
        mode switch
        {
            TacticalTargetingMode.Attack =>
                "ATTACK",
            TacticalTargetingMode.AttackMove =>
                "ATTACK MOVE",
            TacticalTargetingMode.Retreat =>
                "RETREAT",
            TacticalTargetingMode.FireMission =>
                "FIRE MISSION",
            _ =>
                "NONE"
        };

    private void EmitCenteredText(
        string text,
        in HudRect rect,
        Vector4 color,
        int width,
        int height)
    {
        float textWidth = _text.Ready ? UiFontMetrics.Default.Measure(text, UiTypography.Style(UiTextRole.Telemetry).Size * _scale)
            : text.Length * GlyphAdvance * _scale;
        float x =
            MathF.Max(
                rect.X +
                    2.0f *
                    _scale,
                rect.X +
                    (rect.Width -
                     textWidth) *
                    0.5f);
        float y =
            rect.Y +
            MathF.Max(
                2.0f *
                _scale,
                (rect.Height -
                 7.0f *
                 GlyphPixelSize *
                 _scale) *
                0.5f);

        EmitClippedText(
            text,
            x,
            y,
            rect.Right -
                2.0f *
                _scale,
            color,
            width,
            height);
    }

    private void EmitClippedText(
        string text,
        float x,
        float y,
        float right,
        Vector4 color,
        int width,
        int height) =>
        EmitClippedText(
            text.AsSpan(),
            x,
            y,
            right,
            color,
            width,
            height);

    private void EmitClippedText(
        ReadOnlySpan<char> text,
        float x,
        float y,
        float right,
        Vector4 color,
        int width,
        int height)
    {
        if (_text.Add(text, x, y, UiTypography.Style(UiTextRole.Telemetry).Size * _scale, color, width, height, right - x)) return;

        float available =
            MathF.Max(
                0.0f,
                right -
                x);
        int maximumCharacters =
            Math.Max(
                0,
                (int)MathF.Floor(
                    available /
                    (GlyphAdvance *
                     _scale)));
        ReadOnlySpan<char> visible =
            text[
                ..Math.Min(
                    text.Length,
                    maximumCharacters)];

        for (int index = 0;
             index < visible.Length;
             index++)
        {
            EmitGlyph(
                char.ToUpperInvariant(
                    visible[index]),
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
            TextGlyphPattern(
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
                        row *
                        5 +
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
        if (_skin.Icon(icon, x, y, size)) return;
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);
        string pattern =
            IconGlyphPattern(
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
                        row *
                        5 +
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
                "PlayerActionDockVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "PlayerActionDockPixel.hlsl");

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

    private static string IconGlyphPattern(
        RtsUiGlyph glyph) =>
        glyph switch
        {
            RtsUiGlyph.Infantry =>
                "00100" + "01110" + "00100" + "01110" + "10101",
            RtsUiGlyph.Reconnaissance =>
                "00000" + "01110" + "10101" + "01110" + "00000",
            RtsUiGlyph.Armor =>
                "01110" + "11111" + "11111" + "10101" + "11111",
            RtsUiGlyph.Artillery =>
                "00010" + "00110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Truck =>
                "00000" + "11110" + "10111" + "11111" + "01010",
            RtsUiGlyph.Repair =>
                "10001" + "01010" + "00100" + "01010" + "10001",
            RtsUiGlyph.Command =>
                "00100" + "01110" + "11111" + "00100" + "00100",
            RtsUiGlyph.Extraction =>
                "00100" + "00100" + "10101" + "01110" + "00100",
            RtsUiGlyph.Processing =>
                "01010" + "11111" + "01110" + "11111" + "01010",
            RtsUiGlyph.Factory =>
                "10101" + "11111" + "11111" + "10001" + "11111",
            RtsUiGlyph.Storage or
            RtsUiGlyph.Building =>
                "11111" + "10001" + "10101" + "10001" + "11111",
            RtsUiGlyph.Supply =>
                "00100" + "00100" + "11111" + "00100" + "00100",
            RtsUiGlyph.Power =>
                "00110" + "01100" + "11110" + "00110" + "01100",
            RtsUiGlyph.Move =>
                "00100" + "01100" + "11111" + "01100" + "00100",
            RtsUiGlyph.Attack =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.AttackMove =>
                "00100" + "01100" + "11111" + "01110" + "10101",
            RtsUiGlyph.Stop =>
                "11111" + "10001" + "10001" + "10001" + "11111",
            RtsUiGlyph.Hold =>
                "10001" + "10001" + "10001" + "10001" + "10001",
            RtsUiGlyph.Build =>
                "00100" + "01110" + "11111" + "10001" + "11111",
            RtsUiGlyph.Cancel or
            RtsUiGlyph.Invalid or
            RtsUiGlyph.Empty =>
                "10001" + "01010" + "00100" + "01010" + "10001",
            RtsUiGlyph.Select or
            RtsUiGlyph.DragSelect =>
                "11011" + "10001" + "00000" + "10001" + "11011",
            RtsUiGlyph.Drop =>
                "00100" + "01110" + "01110" + "11111" + "01110",
            RtsUiGlyph.Ammunition =>
                "10101" + "10101" + "10101" + "11111" + "01110",
            RtsUiGlyph.LowSupply =>
                "00100" + "00100" + "00100" + "01110" + "00100",
            RtsUiGlyph.Critical or
            RtsUiGlyph.Alert =>
                "00100" + "00100" + "00100" + "00000" + "00100",
            _ =>
                "00100" + "01110" + "11111" + "01110" + "00100"
        };

    private static string TextGlyphPattern(
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
            ':' => "00000001100011000000001100011000000",
            '/' => "00001000100010001000100001000000000",
            '-' => "00000000000000011111000000000000000",
            '+' => "00000001000010011111001000010000000",
            '%' => "11001000100010001000100001001100000",
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
            uint value)
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
