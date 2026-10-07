using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class RtsInformationOverlayRenderer : IDisposable
{
    private const int MaxVertices = 262_144;
    private const int VertexStride = 24;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private readonly RuntimeUiIconPalette? _runtimePalette;

    private int _vertexCount;
    private bool _disposed;

    public RtsInformationOverlayRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
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
        IGraphicsCommandContext context,
        RtsCamera camera,
        PresentationSnapshot snapshot,
        in AxisAlignedBounds worldBounds,
        in RtsInformationLayerView view,
        CombatGroupOverviewView combatGroups,
        uint dpi,
        float uiScale = 1.0f,
        PreAlphaUxView preAlphaUx = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(combatGroups);

        if (context.Width <= 0 ||
            context.Height <= 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);
        float scale =
            layout.Scale;

        if (view.MinimapEnabled)
        {
            RtsMinimapModel minimap =
                RtsMinimapModelBuilder.Build(
                    snapshot,
                    worldBounds,
                    snapshot.PlayerExperience?.Player ??
                        new PlayerId(1),
                    view.SelectedEntities,
                    combatGroups.ActiveMembers);

            EmitMinimap(
                minimap,
                camera.Target,
                view,
                snapshot.StrategicOverlay,
                layout,
                context.Width,
                context.Height);
        }

        EmitCombatGroupOverview(
            combatGroups,
            layout.SecondaryView,
            scale,
            context.Width,
            context.Height);
        EmitSystemOverlay(
            snapshot.PlayerExperience,
            preAlphaUx,
            layout,
            context.Width,
            context.Height);

        if (view.IsDragSelecting)
        {
            EmitSelectionRectangle(
                view.DragStart,
                view.DragCurrent,
                ResolveColor(
                    RtsUiIcon.CursorDragSelect),
                scale,
                context.Width,
                context.Height);
        }

        if (view.HasPointer)
        {
            EmitIcon(
                RtsUiIconCatalog.ResolveCursor(
                    view.Cursor),
                view.PointerPosition.X +
                    10.0f * scale,
                view.PointerPosition.Y +
                    10.0f * scale,
                14.0f * scale,
                context.Width,
                context.Height);
        }

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        IGraphicsBuffer vertexBuffer =
            GetFrameVertexBuffer(
                context.FrameIndex);
        vertexBuffer.SetData<OverlayVertex>(
            _vertices.AsSpan(
                0,
                _vertexCount));

        context.SetPipeline(
            _pipeline);
        context.SetVertexBuffer(
            vertexBuffer,
            VertexStride);
        context.Draw(
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

    private void EmitMinimap(
        RtsMinimapModel model,
        Vector3 cameraTarget,
        in RtsInformationLayerView view,
        StrategicOverlaySnapshot? overlay,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        HudRect map =
            RtsMinimapInteractionLayout.GetMapRect(
                layout);

        if (map.IsEmpty)
        {
            return;
        }

        HudRect selector =
            RtsMinimapInteractionLayout.GetSelectorRect(
                layout);
        HudRect legend =
            RtsMinimapInteractionLayout.GetLegendRect(
                layout);

        EmitQuad(
            layout.Minimap.X -
                3.0f,
            layout.Minimap.Y -
                3.0f,
            layout.Minimap.Width +
                6.0f,
            layout.Minimap.Height +
                6.0f,
            GameplayHudVisualStyle.Border,
            width,
            height);

        EmitOverlaySelector(
            view.OverlayMode,
            selector,
            scale,
            width,
            height);
        EmitOverlayLegend(
            view.OverlayMode,
            overlay,
            legend,
            scale,
            width,
            height);

        float left =
            map.X;
        float top =
            map.Y;
        float mapWidth =
            map.Width;
        float mapHeight =
            map.Height;

        EmitQuad(
            left,
            top,
            mapWidth,
            mapHeight,
            new Vector4(
                0.13f,
                0.15f,
                0.14f,
                1.0f),
            width,
            height);

        float cellSize =
            model.IntelligenceCellSizeMeters;

        for (int index = 0;
             index < model.FogCells.Count;
             index++)
        {
            RtsFogCell fog =
                model.FogCells[index];

            if (fog.Presentation.Pattern ==
                RtsFogPattern.Clear)
            {
                continue;
            }

            Vector3 worldMin =
                new(
                    fog.Cell.X *
                        cellSize,
                    0.0f,
                    fog.Cell.Z *
                        cellSize);
            Vector3 worldMax =
                worldMin +
                new Vector3(
                    cellSize,
                    0.0f,
                    cellSize);
            Vector2 first =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    worldMin,
                    model.WorldBounds);
            Vector2 second =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    worldMax,
                    model.WorldBounds);

            float cellLeft =
                left +
                first.X *
                mapWidth;
            float cellRight =
                left +
                second.X *
                mapWidth;
            float cellTop =
                top +
                (1.0f -
                 second.Y) *
                mapHeight;
            float cellBottom =
                top +
                (1.0f -
                 first.Y) *
                mapHeight;

            Vector4 fogColor =
                fog.Presentation.Pattern ==
                    RtsFogPattern.Solid
                    ? new Vector4(
                        0.015f,
                        0.02f,
                        0.02f,
                        1.0f)
                    : new Vector4(
                        0.09f,
                        0.10f,
                        0.10f,
                        1.0f);

            EmitQuad(
                cellLeft,
                cellTop,
                Math.Max(
                    1.0f,
                    cellRight -
                    cellLeft),
                Math.Max(
                    1.0f,
                    cellBottom -
                    cellTop),
                fogColor,
                width,
                height);

            if (fog.Presentation.Pattern ==
                RtsFogPattern.Hatch)
            {
                EmitQuad(
                    cellLeft,
                    (cellTop +
                     cellBottom) *
                    0.5f,
                    Math.Max(
                        1.0f,
                        cellRight -
                        cellLeft),
                    1.0f,
                    new Vector4(
                        0.25f,
                        0.27f,
                        0.25f,
                        1.0f),
                    width,
                    height);
            }
        }

        for (int index = 0;
             index < model.Symbols.Count;
             index++)
        {
            RtsMinimapSymbol symbol =
                model.Symbols[index];
            Vector2 normalized =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    symbol.WorldPosition,
                    model.WorldBounds);
            float x =
                left +
                normalized.X *
                mapWidth;
            float y =
                top +
                (1.0f -
                 normalized.Y) *
                mapHeight;

            float symbolSize =
                symbol.Kind is
                    RtsMinimapSymbolKind.Objective or
                    RtsMinimapSymbolKind.AttackNotification
                    ? 9.0f * scale
                    : 6.0f * scale;

            EmitIcon(
                symbol.Icon,
                x -
                symbolSize *
                0.5f,
                y -
                symbolSize *
                0.5f,
                symbolSize,
                width,
                height);

            if (symbol.Kind ==
                    RtsMinimapSymbolKind.SelectedGroup &&
                symbol.IsActiveGroup)
            {
                float bracketSize =
                    11.0f *
                    scale;
                float half =
                    bracketSize *
                    0.5f;
                float segment =
                    3.0f *
                    scale;
                float thickness =
                    MathF.Max(
                        1.0f,
                        1.0f *
                        scale);
                Vector4 activeColor =
                    ResolveColor(
                        RtsUiIcon.MinimapSelectedGroup);

                EmitQuad(
                    x - half,
                    y - half,
                    segment,
                    thickness,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x - half,
                    y - half,
                    thickness,
                    segment,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x + half - segment,
                    y - half,
                    segment,
                    thickness,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x + half - thickness,
                    y - half,
                    thickness,
                    segment,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x - half,
                    y + half - thickness,
                    segment,
                    thickness,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x - half,
                    y + half - segment,
                    thickness,
                    segment,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x + half - segment,
                    y + half - thickness,
                    segment,
                    thickness,
                    activeColor,
                    width,
                    height);
                EmitQuad(
                    x + half - thickness,
                    y + half - segment,
                    thickness,
                    segment,
                    activeColor,
                    width,
                    height);
            }
        }

        Vector2 cameraPosition =
            RtsMinimapModelBuilder.NormalizeWorldPosition(
                cameraTarget,
                model.WorldBounds);
        float cameraX =
            left +
            cameraPosition.X *
            mapWidth;
        float cameraY =
            top +
            (1.0f -
             cameraPosition.Y) *
            mapHeight;
        Vector4 cameraColor =
            new(
                0.95f,
                0.95f,
                0.90f,
                1.0f);

        EmitQuad(
            cameraX - 5.0f * scale,
            cameraY - 1.0f,
            10.0f * scale,
            2.0f,
            cameraColor,
            width,
            height);
        EmitQuad(
            cameraX - 1.0f,
            cameraY - 5.0f * scale,
            2.0f,
            10.0f * scale,
            cameraColor,
            width,
            height);

        if (view.MinimapPointerCaptured &&
            view.MinimapPointerWorldValid)
        {
            Vector2 target =
                RtsMinimapInteractionLayout.MapWorldToPointer(
                    view.MinimapPointerWorldTarget,
                    layout,
                    model.WorldBounds);
            Vector4 targetColor =
                ResolveColor(
                    RtsUiIconCatalog.ResolveCursor(
                        view.Cursor));
            float targetSize =
                6.0f *
                scale;

            EmitQuad(
                target.X -
                    targetSize,
                target.Y -
                    1.0f,
                targetSize *
                    2.0f,
                2.0f,
                targetColor,
                width,
                height);
            EmitQuad(
                target.X -
                    1.0f,
                target.Y -
                    targetSize,
                2.0f,
                targetSize *
                    2.0f,
                targetColor,
                width,
                height);
        }
    }

    private void EmitOverlaySelector(
        StrategicOverlayMode selectedMode,
        in HudRect selector,
        float scale,
        int width,
        int height)
    {
        EmitQuad(
            selector.X,
            selector.Y,
            selector.Width,
            selector.Height,
            new Vector4(
                0.055f,
                0.065f,
                0.065f,
                0.98f),
            width,
            height);

        for (int index = 0;
             index <
                 RtsStrategicOverlayHudModel.SelectorButtonCount;
             index++)
        {
            StrategicOverlayMode mode =
                RtsStrategicOverlayHudModel.ModeForButton(
                    index);
            HudRect rect;
            float gap =
                2.0f *
                scale;
            float buttonWidth =
                MathF.Max(
                    0.0f,
                    (selector.Width -
                     gap *
                     (RtsStrategicOverlayHudModel.SelectorButtonCount -
                      1)) /
                    RtsStrategicOverlayHudModel.SelectorButtonCount);
            rect =
                new HudRect(
                    selector.X +
                        index *
                        (buttonWidth + gap),
                    selector.Y,
                    buttonWidth,
                    selector.Height);

            bool selected =
                mode ==
                selectedMode;
            EmitQuad(
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                selected
                    ? new Vector4(
                        0.12f,
                        0.18f,
                        0.19f,
                        0.98f)
                    : new Vector4(
                        0.075f,
                        0.09f,
                        0.09f,
                        0.98f),
                width,
                height);

            float iconSize =
                8.0f *
                scale;
            EmitIcon(
                RtsStrategicOverlayHudModel.ResolveIcon(
                    mode),
                rect.X +
                    2.0f *
                    scale,
                rect.Y +
                    3.0f *
                    scale,
                iconSize,
                width,
                height);
            EmitText(
                RtsStrategicOverlayHudModel.ResolveShortLabel(
                    mode),
                rect.X +
                    12.0f *
                    scale,
                rect.Y +
                    5.0f *
                    scale,
                rect.Right -
                    1.0f *
                    scale,
                new Vector4(
                    0.90f,
                    0.94f,
                    0.92f,
                    1.0f),
                scale *
                    0.62f,
                width,
                height);

            if (selected)
            {
                EmitQuad(
                    rect.X,
                    rect.Bottom -
                        2.0f *
                        scale,
                    rect.Width,
                    2.0f *
                        scale,
                    new Vector4(
                        0.90f,
                        0.94f,
                        0.92f,
                        1.0f),
                    width,
                    height);
            }
        }
    }

    private void EmitOverlayLegend(
        StrategicOverlayMode mode,
        StrategicOverlaySnapshot? overlay,
        in HudRect legend,
        float scale,
        int width,
        int height)
    {
        EmitQuad(
            legend.X,
            legend.Y,
            legend.Width,
            legend.Height,
            new Vector4(
                0.055f,
                0.065f,
                0.065f,
                0.98f),
            width,
            height);

        Span<char> buffer =
            stackalloc char[96];
        var text =
            new HudTextBuilder(
                buffer);
        text.Append(
            RtsStrategicOverlayHudModel.ResolveShortLabel(
                mode));

        if (overlay is not null)
        {
            switch (mode)
            {
                case StrategicOverlayMode.Logistics:
                    text.Append(" N");
                    text.Append(
                        overlay.LogisticsNodes.Count);
                    text.Append(" L");
                    text.Append(
                        overlay.LogisticsLinks.Count);
                    break;

                case StrategicOverlayMode.Supply:
                    int providers = 0;
                    int critical = 0;
                    for (int index = 0;
                         index <
                             overlay.Supply.Count;
                         index++)
                    {
                        StrategicSupplyReadModel item =
                            overlay.Supply[index];
                        if (item.IsProvider)
                        {
                            providers++;
                        }

                        if (item.HasUnitState &&
                            item.Status is
                                BattlefieldSupplyStatus.Critical or
                                BattlefieldSupplyStatus.Unsupplied)
                        {
                            critical++;
                        }
                    }

                    text.Append(" P");
                    text.Append(
                        providers);
                    text.Append(" C");
                    text.Append(
                        critical);
                    break;

                case StrategicOverlayMode.Sensors:
                    text.Append(" S");
                    text.Append(
                        overlay.Sensors.Count);
                    break;

                case StrategicOverlayMode.Navigation:
                    text.Append(" S");
                    text.Append(
                        overlay.NavigationSectors.Count);
                    text.Append(" P");
                    text.Append(
                        overlay.NavigationPortals.Count);
                    break;

                case StrategicOverlayMode.Power:
                    int constrained = 0;
                    for (int index = 0;
                         index <
                             overlay.PowerNetworks.Count;
                         index++)
                    {
                        if (overlay.PowerNetworks[
                                index].IsConstrained)
                        {
                            constrained++;
                        }
                    }

                    text.Append(" N");
                    text.Append(
                        overlay.PowerNetworks.Count);
                    text.Append(" C");
                    text.Append(
                        constrained);
                    break;

                case StrategicOverlayMode.All:
                    text.Append(" LOG ");
                    text.Append(
                        overlay.LogisticsNodes.Count);
                    text.Append(" SUP ");
                    text.Append(
                        overlay.Supply.Count);
                    text.Append(" PWR ");
                    text.Append(
                        overlay.PowerNetworks.Count);
                    break;
            }
        }

        EmitText(
            text.Written,
            legend.X +
                4.0f *
                scale,
            legend.Y +
                4.0f *
                scale,
            legend.Right -
                4.0f *
                scale,
            new Vector4(
                0.82f,
                0.87f,
                0.85f,
                1.0f),
            scale *
                0.68f,
            width,
            height);
    }

    private void EmitSystemOverlay(
        PlayerExperienceSnapshot? experience,
        in PreAlphaUxView view,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        if (experience.HasValue &&
            experience.Value.IsMatchComplete)
        {
            EmitTerminalOverlay(
                experience.Value,
                layout,
                width,
                height);
            return;
        }

        switch (view.Mode)
        {
            case PreAlphaUxMode.MatchSetup:
                EmitSystemPanel(
                    "MATCH SETUP",
                    "ENTER START  ESC EXIT",
                    "F12 CONTROLS",
                    layout,
                    width,
                    height);
                break;

            case PreAlphaUxMode.Paused:
                EmitSystemPanel(
                    "PAUSED",
                    "ESC OR SPACE RESUME",
                    "F12 CONTROLS",
                    layout,
                    width,
                    height);
                break;

            case PreAlphaUxMode.Help:
                EmitHelpPanel(
                    layout,
                    width,
                    height);
                break;

            default:
                if (view.ShowOnboarding)
                {
                    EmitOnboardingHint(
                        layout,
                        width,
                        height);
                }

                break;
        }
    }

    private void EmitTerminalOverlay(
        in PlayerExperienceSnapshot experience,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        string result =
            PlayerSystemHudModel.ResolveMatchResultLabel(
                experience.MatchStatus);
        EmitSystemPanel(
            result,
            "R RESTART  ESC RETURN",
            "MATCH COMPLETE",
            layout,
            width,
            height);
    }

    private void EmitSystemPanel(
        string title,
        string primary,
        string secondary,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        float panelWidth =
            MathF.Min(
                480.0f * scale,
                layout.SafeArea.Width);
        float panelHeight =
            MathF.Min(
                112.0f * scale,
                layout.SafeArea.Height);
        float x =
            layout.SafeArea.X +
            MathF.Max(
                0.0f,
                (layout.SafeArea.Width -
                 panelWidth) *
                0.5f);
        float y =
            layout.SafeArea.Y +
            MathF.Max(
                0.0f,
                layout.SafeArea.Height *
                0.24f);

        EmitQuad(
            x,
            y,
            panelWidth,
            panelHeight,
            GameplayHudVisualStyle.PanelBackground,
            width,
            height);
        EmitQuad(
            x,
            y,
            GameplayHudVisualStyle.StateRailThickness *
            scale,
            panelHeight,
            GameplayHudVisualStyle.Focus,
            width,
            height);
        EmitText(
            title,
            x + 18.0f * scale,
            y + 16.0f * scale,
            x + panelWidth - 12.0f * scale,
            GameplayHudVisualStyle.TextPrimary,
            scale,
            width,
            height);
        EmitText(
            primary,
            x + 18.0f * scale,
            y + 46.0f * scale,
            x + panelWidth - 12.0f * scale,
            GameplayHudVisualStyle.TextPrimary,
            scale * 0.78f,
            width,
            height);
        EmitText(
            secondary,
            x + 18.0f * scale,
            y + 68.0f * scale,
            x + panelWidth - 12.0f * scale,
            GameplayHudVisualStyle.TextSecondary,
            scale * 0.72f,
            width,
            height);
    }

    private void EmitHelpPanel(
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        float panelWidth =
            MathF.Min(
                720.0f * scale,
                layout.SafeArea.Width);
        float panelHeight =
            MathF.Min(
                250.0f * scale,
                layout.SafeArea.Height);
        float x =
            layout.SafeArea.X +
            MathF.Max(
                0.0f,
                (layout.SafeArea.Width -
                 panelWidth) *
                0.5f);
        float y =
            layout.SafeArea.Y +
            MathF.Max(
                0.0f,
                (layout.SafeArea.Height -
                 panelHeight) *
                0.18f);

        EmitQuad(
            x,
            y,
            panelWidth,
            panelHeight,
            GameplayHudVisualStyle.PanelBackground,
            width,
            height);
        EmitQuad(
            x,
            y,
            panelWidth,
            GameplayHudVisualStyle.BorderThickness *
            scale,
            GameplayHudVisualStyle.Focus,
            width,
            height);

        EmitText(
            "CONTROLS",
            x + 16.0f * scale,
            y + 14.0f * scale,
            x + panelWidth - 12.0f * scale,
            GameplayHudVisualStyle.TextPrimary,
            scale,
            width,
            height);

        for (int index = 0;
             index < PlayerSystemHudModel.HelpLineCount;
             index++)
        {
            EmitText(
                PlayerSystemHudModel.GetHelpLine(
                    index),
                x + 16.0f * scale,
                y + (44.0f + index * 24.0f) * scale,
                x + panelWidth - 12.0f * scale,
                GameplayHudVisualStyle.TextSecondary,
                scale * 0.72f,
                width,
                height);
        }
    }

    private void EmitOnboardingHint(
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        string hint =
            PlayerSystemHudModel.OnboardingHint;
        float x =
            layout.SafeArea.X +
            GameplayHudVisualStyle.CompactPadding *
            scale;
        float y =
            MathF.Max(
                layout.TopStatusBar.Bottom +
                GameplayHudVisualStyle.CompactGap *
                scale,
                layout.SafeArea.Bottom -
                18.0f * scale);

        EmitText(
            hint,
            x,
            y,
            layout.SafeArea.Right,
            GameplayHudVisualStyle.TextSecondary,
            scale * 0.68f,
            width,
            height);
    }

    private void EmitCombatGroupOverview(
        CombatGroupOverviewView overview,
        in HudRect region,
        float scale,
        int width,
        int height)
    {
        if (region.IsEmpty ||
            overview.Groups.Count == 0)
        {
            return;
        }

        int assigned = 0;
        for (int index = 0;
             index < overview.Groups.Count;
             index++)
        {
            if (overview.Groups[index].IsAssigned)
            {
                assigned++;
            }
        }

        if (assigned == 0)
        {
            return;
        }

        const float BaseRowHeight = 43.0f;
        float padding =
            5.0f *
            scale;
        float headerHeight =
            18.0f *
            scale;
        float rowHeight =
            BaseRowHeight *
            scale;
        int visibleRows =
            Math.Min(
                assigned,
                Math.Max(
                    0,
                    (int)MathF.Floor(
                        MathF.Max(
                            0.0f,
                            region.Height -
                            headerHeight -
                            padding * 2.0f) /
                        rowHeight)));

        if (visibleRows <= 0)
        {
            return;
        }

        float panelHeight =
            headerHeight +
            visibleRows *
                rowHeight +
            padding * 2.0f;

        EmitQuad(
            region.X,
            region.Y,
            region.Width,
            MathF.Min(
                region.Height,
                panelHeight),
            GameplayHudVisualStyle.PanelBackground,
            width,
            height);

        Span<char> headerBuffer =
            stackalloc char[64];
        var header =
            new HudTextBuilder(
                headerBuffer);
        header.Append("COMBAT GROUPS ");
        header.Append(assigned);
        EmitText(
            header.Written,
            region.X +
                padding,
            region.Y +
                5.0f *
                scale,
            region.Right -
                padding,
            new Vector4(
                0.90f,
                0.94f,
                0.92f,
                1.0f),
            scale *
                0.70f,
            width,
            height);

        float y =
            region.Y +
            headerHeight +
            padding;
        int rendered = 0;
        Span<char> primaryBuffer =
            stackalloc char[96];
        Span<char> statusBuffer =
            stackalloc char[128];
        Span<char> contextBuffer =
            stackalloc char[96];

        for (int index = 0;
             index < overview.Groups.Count &&
             rendered < visibleRows;
             index++)
        {
            CombatGroupSummaryReadModel group =
                overview.Groups[index];

            if (!group.IsAssigned)
            {
                continue;
            }

            Vector4 accent =
                group.IsActive
                    ? new Vector4(
                        0.96f,
                        0.76f,
                        0.24f,
                        1.0f)
                    : group.IsSelected
                        ? new Vector4(
                            0.34f,
                            0.82f,
                            0.92f,
                            1.0f)
                        : new Vector4(
                            0.54f,
                            0.59f,
                            0.58f,
                            1.0f);

            EmitQuad(
                region.X +
                    padding,
                y,
                region.Width -
                    padding * 2.0f,
                rowHeight -
                    2.0f *
                    scale,
                GameplayHudVisualStyle.PanelRaised,
                width,
                height);
            EmitQuad(
                region.X +
                    padding,
                y,
                3.0f *
                    scale,
                rowHeight -
                    2.0f *
                    scale,
                accent,
                width,
                height);

            var primary =
                new HudTextBuilder(
                    primaryBuffer);
            primary.Append("G");
            primary.Append(group.Slot);
            primary.Append(" ");
            primary.Append(group.Label);
            primary.Append("  U");
            primary.Append(group.MemberCount);

            if (group.IsActive)
            {
                primary.Append("  ACTIVE");
            }
            else if (group.IsSelected)
            {
                primary.Append("  SELECTED");
            }

            EmitText(
                primary.Written,
                region.X +
                    10.0f *
                    scale,
                y +
                    5.0f *
                    scale,
                region.Right -
                    padding,
                accent,
                scale *
                    0.66f,
                width,
                height);

            var status =
                new HudTextBuilder(
                    statusBuffer);

            if (group.HasHealth)
            {
                status.Append("HP ");
                status.Append(
                    Percent(
                        group.Health));
            }

            if (group.HasStrength)
            {
                status.Append(" STR ");
                status.Append(
                    Percent(
                        group.Strength));
            }

            if (group.HasReadiness)
            {
                status.Append(" RDY ");
                status.Append(
                    Percent(
                        group.Readiness));
            }

            if (group.HasSupply)
            {
                status.Append(" F ");
                status.Append(
                    Percent(
                        group.Fuel));
                status.Append(" A ");
                status.Append(
                    Percent(
                        group.Ammunition));
                status.Append(" ");
                status.Append(
                    ResolveGroupSupplyLabel(
                        group.SupplyStatus));
            }

            EmitText(
                status.Written,
                region.X +
                    10.0f *
                    scale,
                y +
                    17.0f *
                    scale,
                region.Right -
                    padding,
                new Vector4(
                    0.82f,
                    0.87f,
                    0.85f,
                    1.0f),
                scale *
                    0.60f,
                width,
                height);

            var context =
                new HudTextBuilder(
                    contextBuffer);

            if (group.HasFormation)
            {
                context.Append(
                    group.MixedFormation
                        ? "FORM MIXED"
                        : ResolveFormationLabel(
                            group.Formation));
            }

            if (group.HasOrder)
            {
                if (context.Written.Length >
                    0)
                {
                    context.Append("  ");
                }

                context.Append(
                    group.MixedOrder
                        ? "ORDER MIXED"
                        : ResolveOrderLabel(
                            group.Order));
            }

            if (context.Written.Length >
                0)
            {
                EmitText(
                    context.Written,
                    region.X +
                        10.0f *
                        scale,
                    y +
                        29.0f *
                        scale,
                    region.Right -
                        padding,
                    new Vector4(
                        0.62f,
                        0.68f,
                        0.67f,
                        1.0f),
                    scale *
                        0.57f,
                    width,
                    height);
            }

            y +=
                rowHeight;
            rendered++;
        }

        if (assigned >
            visibleRows)
        {
            Span<char> overflowBuffer =
                stackalloc char[32];
            var overflow =
                new HudTextBuilder(
                    overflowBuffer);
            overflow.Append("+");
            overflow.Append(
                assigned -
                visibleRows);
            overflow.Append(" MORE");

            EmitText(
                overflow.Written,
                region.Right -
                    66.0f *
                    scale,
                region.Y +
                    5.0f *
                    scale,
                region.Right -
                    padding,
                new Vector4(
                    0.72f,
                    0.76f,
                    0.75f,
                    1.0f),
                scale *
                    0.60f,
                width,
                height);
        }
    }

    private static int Percent(
        double value) =>
        (int)Math.Round(
            Math.Clamp(
                value,
                0.0,
                1.0) *
            100.0,
            MidpointRounding.AwayFromZero);

    private static string ResolveGroupSupplyLabel(
        BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.Supplied =>
                "SUPPLIED",
            BattlefieldSupplyStatus.LowSupply =>
                "LOW",
            BattlefieldSupplyStatus.Critical =>
                "CRITICAL",
            BattlefieldSupplyStatus.Unsupplied =>
                "EMPTY",
            _ =>
                "SUPPLY"
        };

    private static string ResolveFormationLabel(
        FormationTemplate formation) =>
        formation switch
        {
            FormationTemplate.Line =>
                "FORM LINE",
            FormationTemplate.Column =>
                "FORM COLUMN",
            FormationTemplate.Wedge =>
                "FORM WEDGE",
            FormationTemplate.Compact =>
                "FORM COMPACT",
            _ =>
                "FORM"
        };

    private static string ResolveOrderLabel(
        CombatOrderKind order) =>
        order switch
        {
            CombatOrderKind.Attack =>
                "ORDER ATTACK",
            CombatOrderKind.AttackMove =>
                "ORDER ATTACK MOVE",
            CombatOrderKind.Stop =>
                "ORDER STOP",
            CombatOrderKind.HoldPosition =>
                "ORDER HOLD",
            CombatOrderKind.Retreat =>
                "ORDER RETREAT",
            _ =>
                "ORDER"
        };

    private void EmitSelectionRectangle(
        Vector2 first,
        Vector2 second,
        Vector4 color,
        float scale,
        int width,
        int height)
    {
        Vector2 minimum =
            Vector2.Min(
                first,
                second);
        Vector2 maximum =
            Vector2.Max(
                first,
                second);
        float thickness =
            Math.Max(
                1.0f,
                2.0f * scale);

        EmitQuad(
            minimum.X,
            minimum.Y,
            maximum.X -
                minimum.X,
            thickness,
            color,
            width,
            height);
        EmitQuad(
            minimum.X,
            maximum.Y -
                thickness,
            maximum.X -
                minimum.X,
            thickness,
            color,
            width,
            height);
        EmitQuad(
            minimum.X,
            minimum.Y,
            thickness,
            maximum.Y -
                minimum.Y,
            color,
            width,
            height);
        EmitQuad(
            maximum.X -
                thickness,
            minimum.Y,
            thickness,
            maximum.Y -
                minimum.Y,
            color,
            width,
            height);
    }

    private void EmitSegmentedBar(
        float x,
        float y,
        float widthValue,
        float heightValue,
        double fraction,
        Vector4 color,
        int width,
        int height)
    {
        const int SegmentCount = 5;
        float gap = 2.0f;
        float segmentWidth =
            (widthValue -
             gap *
             (SegmentCount - 1)) /
            SegmentCount;
        int active =
            (int)Math.Ceiling(
                Math.Clamp(
                    fraction,
                    0.0,
                    1.0) *
                SegmentCount);

        for (int index = 0;
             index < SegmentCount;
             index++)
        {
            Vector4 segmentColor =
                index < active
                    ? color
                    : new Vector4(
                        0.18f,
                        0.19f,
                        0.18f,
                        1.0f);

            EmitQuad(
                x +
                index *
                    (segmentWidth + gap),
                y,
                segmentWidth,
                heightValue,
                segmentColor,
                width,
                height);
        }
    }

    private void EmitText(
        string value,
        float x,
        float y,
        float right,
        Vector4 color,
        float glyphScale,
        int width,
        int height) =>
        EmitText(
            value.AsSpan(),
            x,
            y,
            right,
            color,
            glyphScale,
            width,
            height);

    private void EmitText(
        ReadOnlySpan<char> value,
        float x,
        float y,
        float right,
        Vector4 color,
        float glyphScale,
        int width,
        int height)
    {
        const float Advance = 6.0f;
        float step =
            Advance *
            glyphScale;
        int maximumCharacters =
            step >
                0.0f
                ? Math.Max(
                    0,
                    (int)MathF.Floor(
                        MathF.Max(
                            0.0f,
                            right -
                            x) /
                        step))
                : 0;
        int count =
            Math.Min(
                value.Length,
                maximumCharacters);

        for (int index = 0;
             index < count;
             index++)
        {
            EmitTextGlyph(
                char.ToUpperInvariant(
                    value[index]),
                x +
                    index *
                    step,
                y,
                color,
                glyphScale,
                width,
                height);
        }
    }

    private void EmitTextGlyph(
        char character,
        float x,
        float y,
        Vector4 color,
        float glyphScale,
        int width,
        int height)
    {
        string pattern =
            TextGlyphPattern(
                character);

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
                        glyphScale,
                    y +
                        row *
                        glyphScale,
                    glyphScale,
                    glyphScale,
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
            GlyphPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
        float pixel =
            Math.Max(
                1.0f,
                size / 5.0f);

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
                "RtsInformationOverlayVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "RtsInformationOverlayPixel.hlsl");

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
                DepthEnabled = false
            });
    }

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
            '%' => "11001000100010001000100001001100000",
            ' ' => "",
            _ => "11111000010001000100000000010000100"
        };

    private ref struct HudTextBuilder
    {
        private Span<char> _buffer;
        private int _length;

        public HudTextBuilder(
            Span<char> buffer)
        {
            _buffer =
                buffer;
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
                        System.Globalization.CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }
    }

    private static string GlyphPattern(
        RtsUiGlyph glyph) =>
        glyph switch
        {
            RtsUiGlyph.Ore =>
                "01110" + "11111" + "11111" + "01110" + "00100",
            RtsUiGlyph.Drop =>
                "00100" + "01110" + "01110" + "11111" + "01110",
            RtsUiGlyph.Crystal =>
                "00100" + "01110" + "11111" + "01110" + "00100",
            RtsUiGlyph.Star or
            RtsUiGlyph.Objective =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Ingot =>
                "00000" + "11111" + "10001" + "11111" + "00000",
            RtsUiGlyph.Circuit =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Ammunition =>
                "10101" + "10101" + "10101" + "11111" + "01110",
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
            RtsUiGlyph.Patrol =>
                "01110" + "10001" + "10111" + "10000" + "01110",
            RtsUiGlyph.Build =>
                "00100" + "01110" + "11111" + "10001" + "11111",
            RtsUiGlyph.Cancel or
            RtsUiGlyph.Invalid or
            RtsUiGlyph.Empty =>
                "10001" + "01010" + "00100" + "01010" + "10001",
            RtsUiGlyph.Cursor =>
                "10000" + "11000" + "10100" + "10010" + "00001",
            RtsUiGlyph.Select or
            RtsUiGlyph.DragSelect =>
                "11011" + "10001" + "00000" + "10001" + "11011",
            RtsUiGlyph.Pan =>
                "00100" + "10101" + "01110" + "10101" + "00100",
            RtsUiGlyph.Check =>
                "00001" + "00010" + "10100" + "01000" + "00000",
            RtsUiGlyph.LowSupply =>
                "00100" + "00100" + "00100" + "01110" + "00100",
            RtsUiGlyph.Critical or
            RtsUiGlyph.Alert =>
                "00100" + "00100" + "00100" + "00000" + "00100",
            RtsUiGlyph.FriendlyUnit =>
                "01110" + "11111" + "11111" + "11111" + "01110",
            RtsUiGlyph.EnemyUnit =>
                "00100" + "01110" + "11111" + "11111" + "10101",
            RtsUiGlyph.Contact =>
                "00100" + "01010" + "10001" + "01010" + "00100",
            RtsUiGlyph.Health =>
                "01010" + "11111" + "11111" + "01110" + "00100",
            _ =>
                "00100" + "01110" + "11111" + "01110" + "00100"
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


}
