using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
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
        GameplayHudRegion.ActionDock;

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
    }

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
        ArgumentNullException.ThrowIfNull(graphics);
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

        EmitModeBar(
            panel.Mode,
            layout,
            graphics.Width,
            graphics.Height);

        PlayerActionSnapshot? actions =
            snapshot.PlayerActions;

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

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

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

        LastRenderedVertexCount =
            _vertexCount;
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
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitModeBar(
        PlayerActionPanelMode activeMode,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        HudRect bar =
            PlayerActionDockInteractionLayout.GetModeBarRect(
                layout);

        EmitQuad(
            bar.X,
            bar.Y,
            bar.Width,
            bar.Height,
            PanelColor,
            width,
            height);

        Span<char> modeTextBuffer =
            stackalloc char[24];

        for (int index = 0;
             index <
                 PlayerActionDockInteractionLayout.ModeButtonCount;
             index++)
        {
            PlayerActionPanelMode mode =
                PlayerActionDockInteractionLayout.ModeForButton(
                    index);
            HudRect rect =
                PlayerActionDockInteractionLayout.GetModeButtonRect(
                    layout,
                    index);
            bool selected =
                mode ==
                activeMode;

            EmitQuad(
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                selected
                    ? SelectedColor
                    : CardColor,
                width,
                height);

            if (selected)
            {
                EmitQuad(
                    rect.X,
                    rect.Bottom -
                        3.0f *
                        _scale,
                    rect.Width,
                    3.0f *
                        _scale,
                    ResolveColor(
                        PlayerActionDockHudModel.ResolveModeIcon(
                            mode)),
                    width,
                    height);
            }

            float iconSize =
                GameplayHudVisualStyle.SmallIconSize *
                _scale;
            EmitIcon(
                PlayerActionDockHudModel.ResolveModeIcon(
                    mode),
                rect.X +
                    5.0f *
                    _scale,
                rect.Y +
                    5.0f *
                    _scale,
                iconSize,
                width,
                height);

            var text =
                new HudTextBuilder(
                    modeTextBuffer);
            text.Append(
                PlayerActionDockHudModel.ResolveModeShortcut(
                    mode));
            text.Append(" ");
            text.Append(
                PlayerActionDockHudModel.ResolveModeLabel(
                    mode));

            EmitClippedText(
                text.Written,
                rect.X +
                    21.0f *
                    _scale,
                rect.Y +
                    7.0f *
                    _scale,
                rect.Right -
                    3.0f *
                    _scale,
                selected
                    ? TextColor
                    : MutedTextColor,
                width,
                height);
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

        EmitQuad(
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

    private void EmitHeader(
        PresentationSnapshot snapshot,
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        HudRect header =
            PlayerActionDockInteractionLayout.GetHeaderRect(
                layout);
        float x =
            header.X +
            GameplayHudVisualStyle.PanelPadding *
            _scale;
        float y =
            header.Y +
            4.0f *
            _scale;

        Span<char> titleBuffer =
            stackalloc char[96];
        var title =
            new HudTextBuilder(
                titleBuffer);
        title.Append(
            PlayerActionDockHudModel.ResolveModeLabel(
                panel.Mode));

        if (actions is not null)
        {
            title.Append("  PENDING ");
            title.Append(
                actions.PendingCommandCount);
        }

        EmitClippedText(
            title.Written,
            x,
            y,
            header.Right -
                6.0f *
                _scale,
            TextColor,
            width,
            height);

        Span<char> contextBuffer =
            stackalloc char[256];
        var context =
            new HudTextBuilder(
                contextBuffer);
        AppendHeaderContext(
            ref context,
            actions,
            panel,
            targeting,
            activeFormation);

        if (context.Written.Length > 0)
        {
            EmitClippedText(
                context.Written,
                x,
                y +
                    12.0f *
                    _scale,
                header.Right -
                    6.0f *
                    _scale,
                MutedTextColor,
                width,
                height);
        }

        if (snapshot.PlayerExperience is
                PlayerExperienceSnapshot experience &&
            ResourcePowerHudModel.IsCommandFeedbackVisible(
                experience))
        {
            Span<char> feedbackBuffer =
                stackalloc char[160];
            var feedback =
                new HudTextBuilder(
                    feedbackBuffer);
            feedback.Append("LAST ");
            feedback.Append(
                PlayerActionDockHudModel.ResolveFeedbackStateLabel(
                    experience.Feedback.State));

            if (experience.Feedback.AcceptedTargets > 0 ||
                experience.Feedback.RejectedTargets > 0)
            {
                feedback.Append(" ");
                feedback.Append(
                    experience.Feedback.AcceptedTargets);
                feedback.Append("/");
                feedback.Append(
                    experience.Feedback.RejectedTargets);
            }

            string reason =
                PlayerActionDockHudModel.ResolveCommandFeedbackReason(
                    experience.Feedback);

            if (reason.Length > 0)
            {
                feedback.Append(" ");
                feedback.Append(
                    reason);
            }

            EmitClippedText(
                feedback.Written,
                x,
                y +
                    24.0f *
                    _scale,
                header.Right -
                    6.0f *
                    _scale,
                experience.Feedback.State ==
                    PlayerCommandFeedbackState.Rejected
                    ? ResolveColor(
                        RtsUiIcon.StatusAlert)
                    : TextColor,
                width,
                height);
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

    private void EmitCards(
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        int itemCount =
            PlayerActionDockInteractionLayout.GetItemCount(
                panel.Mode,
                actions);
        int visible =
            Math.Min(
                itemCount,
                PlayerActionDockInteractionLayout.MaximumVisibleItems(
                    layout));

        if (visible == 0)
        {
            HudRect cards =
                PlayerActionDockInteractionLayout.GetCardsRect(
                    layout);
            EmitClippedText(
                "NO CONTEXTUAL ACTIONS",
                cards.X +
                    6.0f *
                    _scale,
                cards.Y +
                    8.0f *
                    _scale,
                cards.Right -
                    6.0f *
                    _scale,
                MutedTextColor,
                width,
                height);
            return;
        }

        Span<char> statusBuffer =
            stackalloc char[160];

        for (int index = 0;
             index < visible;
             index++)
        {
            HudRect rect =
                PlayerActionDockInteractionLayout.GetCardRect(
                    layout,
                    index);
            PlayerActionDockItemState state =
                PlayerActionDockHudModel.ResolveItemState(
                    panel.Mode,
                    index,
                    actions);
            bool selected =
                index ==
                panel.SelectedIndex;

            bool hovered =
                panel.HoveredIndex ==
                index;
            bool pressed =
                hovered &&
                panel.PointerPressed;
            HudStateVisual visual =
                GameplayHudVisualStyle.ResolveItemState(
                    selected,
                    hovered,
                    pressed,
                    state.CanActivate);

            EmitQuad(
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                visual.Fill,
                width,
                height);
            EmitItemStateCue(
                rect,
                visual,
                width,
                height);

            float iconSize =
                GameplayHudVisualStyle.ActionIconSize *
                _scale;
            EmitIcon(
                PlayerActionDockHudModel.ResolveItemIcon(
                    panel.Mode,
                    index,
                    actions),
                rect.X +
                    6.0f *
                    _scale,
                rect.Y +
                    6.0f *
                    _scale,
                iconSize,
                width,
                height);

            EmitClippedText(
                PlayerActionDockHudModel.ResolveItemTitle(
                    panel.Mode,
                    index,
                    actions),
                rect.X +
                    24.0f *
                    _scale,
                rect.Y +
                    5.0f *
                    _scale,
                rect.Right -
                    4.0f *
                    _scale,
                state.CanActivate
                    ? TextColor
                    : MutedTextColor,
                width,
                height);

            var status =
                new HudTextBuilder(
                    statusBuffer);

            if (!state.CanActivate &&
                state.DisabledReason.Length > 0)
            {
                status.Append(
                    state.DisabledReason);
            }
            else
            {
                AppendItemStatus(
                    ref status,
                    panel.Mode,
                    index,
                    actions,
                    panel);
            }

            EmitClippedText(
                status.Written,
                rect.X +
                    24.0f *
                    _scale,
                rect.Y +
                    20.0f *
                    _scale,
                rect.Right -
                    4.0f *
                    _scale,
                !state.CanActivate
                    ? ResolveColor(
                        RtsUiIcon.StatusAlert)
                    : MutedTextColor,
                width,
                height);
        }

        if (itemCount > visible)
        {
            HudRect cards =
                PlayerActionDockInteractionLayout.GetCardsRect(
                    layout);
            Span<char> overflowBuffer =
                stackalloc char[24];
            var overflow =
                new HudTextBuilder(
                    overflowBuffer);
            overflow.Append("+");
            overflow.Append(
                itemCount -
                visible);
            overflow.Append(" MORE");

            EmitClippedText(
                overflow.Written,
                cards.Right -
                    62.0f *
                    _scale,
                cards.Bottom -
                    9.0f *
                    _scale,
                cards.Right,
                MutedTextColor,
                width,
                height);
        }
    }

    private void EmitItemStateCue(
        in HudRect rect,
        in HudStateVisual visual,
        int width,
        int height)
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
                    rect.Height,
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

    private void EmitFooter(
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        for (int index = 0;
             index <
                 PlayerActionDockInteractionLayout.FooterButtonCount;
             index++)
        {
            PlayerActionDockControlKind control =
                PlayerActionDockInteractionLayout.FooterControl(
                    index);
            HudRect rect =
                PlayerActionDockInteractionLayout.GetFooterButtonRect(
                    layout,
                    index);
            string label =
                PlayerActionDockHudModel.ResolveFooterLabel(
                    control,
                    panel.Mode);

            if (label.Length == 0)
            {
                continue;
            }

            bool enabled =
                PlayerActionDockHudModel.CanUseControl(
                    control,
                    panel.Mode,
                    panel.SelectedIndex,
                    panel,
                    actions);

            EmitQuad(
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                enabled
                    ? CardColor
                    : GameplayHudVisualStyle.PanelDisabled,
                width,
                height);

            EmitCenteredText(
                label,
                rect,
                enabled
                    ? TextColor
                    : MutedTextColor,
                width,
                height);
        }
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
            20.0f *
            _scale;
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

        EmitQuad(
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
        float textWidth =
            text.Length *
            GlyphAdvance *
            _scale;
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
