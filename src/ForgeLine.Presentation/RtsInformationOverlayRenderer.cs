using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class RtsInformationOverlayRenderer : IDisposable
{
    private const int MaxVertices = 262_144;
    private const int VertexStride = 24;

    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly GameplayHudSkin _skin;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private readonly RuntimeUiIconPalette? _runtimePalette;
    private readonly RtsMinimapScratch _minimapScratch = new();

    private GameplayBindingRegistry _bindings = GameplayBindingRegistry.Default;
    private int _vertexCount;
    private int _vertexLimit = MaxVertices;
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
        _text = new UiTextBatch(graphics);
        try { _skin = new(graphics, runtimeAssets, icons: true, portraits: false, maximumIcons: 4096); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }

    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }
    private HudRect? _guidanceClip;
    private readonly NotificationSurfaceMotion _placementMotion = new();
    internal HudRect LastGuidanceBounds { get; private set; }
    internal float LastGuidanceTextSize { get; private set; }
    public int LastGuidanceVertexCount { get; private set; }
    internal int LastWorldCorners { get; private set; }
    internal int LastWorldMeters { get; private set; }
    private readonly HudRect[] _worldLabelBounds = new HudRect[64];
    internal int LastWorldLabels { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera,
        PresentationSnapshot snapshot,
        in AxisAlignedBounds worldBounds,
        in RtsInformationLayerView view,
        CombatGroupOverviewView combatGroups,
        uint dpi,
        float uiScale = 1.0f,
        PreAlphaUxView preAlphaUx = default,
        TacticalTargetingView targeting = default,
        TimeSpan presentationTime = default,
        bool animateNotifications = false,
        DebugDraw? gameplayOverlay = null)
    {
        ThrowIfDisposed();
        _text.Begin();
        _bindings = preAlphaUx.Bindings ?? GameplayBindingRegistry.Default;
        ArgumentNullException.ThrowIfNull(context);
        if (context.Width > 0 && context.Height > 0) _skin.Begin(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(combatGroups);
        LastGuidanceBounds = default;
        LastGuidanceTextSize = 0;
        LastGuidanceVertexCount = 0;
        LastWorldCorners = LastWorldMeters = 0;
        LastWorldLabels = 0;

        if (context.Width <= 0 ||
            context.Height <= 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _vertexLimit = MaxVertices - 2048;
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);
        if (view.MinimapEnabled && !view.MinimapBounds.IsEmpty &&
            view.MinimapBounds.X >= layout.SafeArea.X &&
            view.MinimapBounds.Y >= layout.SafeArea.Y &&
            view.MinimapBounds.Right <= layout.SafeArea.Right &&
            view.MinimapBounds.Bottom <= layout.SafeArea.Bottom)
            layout = layout with { Minimap = view.MinimapBounds };
        float scale =
            layout.Scale;

        if (preAlphaUx.Mode == PreAlphaUxMode.None)
        {
            EmitWorldArtwork(camera, snapshot, view, targeting, layout, context.Width, context.Height);
            EmitGameplayLabels(camera, gameplayOverlay, layout, context.Width, context.Height);
        }
        if (view.MinimapEnabled)
        {
            RtsMinimapFrame minimap =
                _minimapScratch.Update(
                    snapshot,
                    worldBounds,
                    snapshot.PlayerExperience?.Player ??
                        new PlayerId(1),
                    view.SelectedEntities,
                    combatGroups.ActiveMembers);

            EmitMinimap(
                minimap,
                camera,
                view,
                RtsStrategicOverlayHudModel.IsCurrent(snapshot, view.OverlayMode) ? snapshot.StrategicOverlay : null,
                layout,
                context.Width,
                context.Height);
        }

        bool placementVisible = preAlphaUx.Placement.Visible && preAlphaUx.Placement.SessionId == snapshot.SessionId && preAlphaUx.Placement.Tick == snapshot.Tick;
        bool contextVisible = preAlphaUx.Mode == PreAlphaUxMode.None && !view.IsDragSelecting && snapshot.PlayerExperience is { IsMatchComplete: false } &&
            ((preAlphaUx.Guidance.Visible && preAlphaUx.Guidance.SessionId == snapshot.SessionId) ||
                placementVisible);
        if (contextVisible)
        {
            if (placementVisible)
                EmitPlacementHintBar(preAlphaUx.Placement, layout, context.Width, context.Height,
                    presentationTime, animateNotifications);
            else EmitContextGuidance(preAlphaUx, false, layout, context.Width, context.Height);
        }
        if (!placementVisible)
        {
            _placementMotion.Update(default, snapshot.SessionId, layout, presentationTime, animateNotifications);
            if (!contextVisible)
                for (int i = 0; i < _placementMotion.RetiredCount; i++)
                {
                    var (bounds, opacity) = _placementMotion.Retired(i);
                    if (!_skin.Panel(bounds, "panel.compact", NotificationSurfaceVisuals.BackgroundOpacity * opacity))
                        EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                            GameplayHudVisualStyle.PanelBackground with
                            { W = NotificationSurfaceVisuals.BackgroundOpacity * opacity }, context.Width, context.Height);
                }
        }
        EmitCombatGroupOverview(
            combatGroups,
            layout.GroupOverviewBounds(combatGroups),
            layout.GroupOverviewScale,
            context.Width,
            context.Height);
        EmitSystemOverlay(
            snapshot.PlayerExperience,
            preAlphaUx,
            layout,
            context.Width,
            context.Height);

        _vertexLimit = MaxVertices;
        if (view.IsDragSelecting)
        {
            EmitSelectionRectangle(
                view.DragStart,
                view.DragCurrent,
                context.Width,
                context.Height);
        }

        if (_vertexCount == 0 && _skin.Vertices == 0 && _text.VertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _skin.FlushPanels(context);
        if (_vertexCount > 0)
        {
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

        }

        // Terrain/fog and live lines precede clipped contact artwork; text remains last.
        _skin.FlushArtwork(context);
        _text.Render(context);
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

    private void EmitWorldArtwork(RtsCamera camera, PresentationSnapshot snapshot, in RtsInformationLayerView view,
        in TacticalTargetingView targeting, in GameplayHudLayout layout, int width, int height)
    {
        float scale = layout.Scale;
        if (!_skin.Ready || snapshot.PlayerExperience is not { IsMatchComplete: false } experience || experience.Tick != snapshot.Tick) return;
        HudRect worldClip = new(0, layout.TopStatusBar.Bottom, width,
            MathF.Max(0, layout.Minimap.Y - layout.TopStatusBar.Bottom));
        _skin.IconClip(worldClip);
        _skin.PanelClip(worldClip);
        var selection = experience.Selection;
        var hover = snapshot.Hover is { } candidate && candidate.SessionId == snapshot.SessionId && candidate.Tick == snapshot.Tick
            ? candidate : (PlayerHoverSummary?)null;
        // Only primary selection and current permitted hover receive corner art; the whole group retains bounded world outlines.
        foreach (ref readonly var instance in snapshot.Instances)
        {
            bool primary = selection.Count > 0 && selection.PrimaryEntity == instance.Entity && instance.Selectable.Owner == experience.Player && view.SelectedEntities is { } selected && selected.Contains(instance.Entity);
            bool hovered = hover is { } h && h.Entity == instance.Entity;
            if ((!primary && !hovered) || (instance.Visibility & RenderVisibilityMask.World) == 0 || !instance.Mesh.IsValid || !instance.Material.IsValid) continue;
            Vector3 extents = PresentationBounds.ResolveLocalHalfExtents(instance);
            float patchX = MathF.Min(extents.X * .6f, 3), patchZ = MathF.Min(extents.Z * .6f, 3);
            for (int corner = 0; corner < 4; corner++)
            {
                var a = camera.WorldToScreen(TacticalFootprint.Corner(instance, corner), width, height);
                var b = camera.WorldToScreen(TacticalFootprint.Corner(instance, corner, patchX), width, height);
                var c = camera.WorldToScreen(TacticalFootprint.Corner(instance, corner, patchX, patchZ), width, height);
                var d = camera.WorldToScreen(TacticalFootprint.Corner(instance, corner, 0, patchZ), width, height);
                if (a.IsVisible && b.IsVisible && c.IsVisible && d.IsVisible)
                {
                    LastWorldCorners++;
                    _skin.Projected("ui.artwork.directorate.world.selection_corner.p32", a.Position, b.Position, c.Position, d.Position,
                        primary ? RtsSelectionVisualStyle.Selected :
                            instance.Selectable.Owner.IsSpecified && instance.Selectable.Owner != experience.Player
                                ? RtsSelectionVisualStyle.ForeignHover : RtsSelectionVisualStyle.Hovered);
                }
            }
            // Aggregate selection values must never be painted onto one member of a group.
            PlayerSelectionSummary facts = primary && selection.Count == 1 ? selection : hovered ? hover!.Value.OwnedDetails : default;
            if (facts.Count != 1 || facts.PrimaryEntity != instance.Entity || instance.Selectable.Owner != experience.Player) continue;
            var anchor = camera.WorldToScreen(instance.Transform.Position + Vector3.UnitY * extents.Y, width, height);
            if (!anchor.IsVisible) continue;
            float x = anchor.Position.X - 24 * scale, y = anchor.Position.Y - 12 * scale;
            if (facts.HasHealth && double.IsFinite(facts.HealthFraction))
            {
                LastWorldMeters++;
                Vector4 healthColor = facts.HealthFraction <= .25 ? ForgeLineUiPalette.Critical :
                    facts.HealthFraction <= .5 ? ForgeLineUiPalette.Warning : RtsSelectionVisualStyle.Selected;
                _skin.Bar(new(x,y,48*scale,4*scale), facts.HealthFraction, healthColor);
            }
            if (facts.HasSupply && double.IsFinite(facts.FuelFraction) && double.IsFinite(facts.AmmunitionFraction))
            {
                LastWorldMeters += 2;
                _skin.Bar(new(x,y+5*scale,23*scale,3*scale), facts.FuelFraction, new(.95f,.7f,.2f,1));
                _skin.Bar(new(x+25*scale,y+5*scale,23*scale,3*scale), facts.AmmunitionFraction, new(.7f,.85f,1,1));
            }
        }
        if (targeting.HasPointerTarget)
        {
            var point = camera.WorldToScreen(targeting.PointerWorldTarget, width, height);
            if (point.IsVisible && targeting.PointerTargetValid) _skin.Target(new(point.Position.X-12*scale,point.Position.Y-12*scale,24*scale,24*scale));
        }
        EmitOrderArtwork(camera, view.MovementFeedback, scale, width, height);
        EmitOrderArtwork(camera, view.TacticalFeedback, scale, width, height);
        _skin.IconClip(new(0,0,width,height));
        _skin.PanelClip(new(0,0,width,height));
    }

    private void EmitOrderArtwork(RtsCamera camera, in RtsCommandMarkerView marker, float scale, int width, int height)
    {
        if (!marker.Visible) return;
        var point = camera.WorldToScreen(marker.Position,width,height);
        if (!point.IsVisible) return;
        _skin.Order(new(point.Position.X-12*scale,point.Position.Y-12*scale,24*scale,24*scale),marker);
        string label = marker.State switch
        {
            RtsCommandMarkerState.Pending => "PENDING",
            RtsCommandMarkerState.Partial => "PARTIAL",
            RtsCommandMarkerState.Rejected => "REJECTED",
            _ => "ACCEPTED"
        };
        _text.AddCentered(label, new(point.Position.X-60*scale, point.Position.Y+14*scale,120*scale,18*scale),
            12*scale, Vector4.One, width, height);
    }

    private void EmitMinimap(
        in RtsMinimapFrame model,
        RtsCamera camera,
        in RtsInformationLayerView view,
        StrategicOverlaySnapshot? overlay,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            HudInformationLayout.MinimapScale(layout);
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

        if (!_skin.Panel(layout.Minimap, "panel.minimap")) EmitQuad(
            layout.Minimap.X,
            layout.Minimap.Y,
            layout.Minimap.Width,
            layout.Minimap.Height,
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

        _skin.IconClip(map);
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
                    ? (_skin.Ready ? 14.0f : 9.0f) * scale
                    : (_skin.Ready ? 11.0f : 6.0f) * scale;

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
                height,
                symbol.IsCurrent ? 1 : .5f);

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

        _skin.IconClip(new HudRect(0, 0, width, height));
        Vector2 cameraPosition =
            RtsMinimapModelBuilder.NormalizeWorldPosition(
                camera.Target,
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

        if (_skin.Ready)
        {
            Span<Vector2> footprint = stackalloc Vector2[4];
            if (RtsMinimapCameraFootprint.TryProject(camera, map, model.WorldBounds, width, height, footprint))
                for (int edge = 0; edge < 4; edge++) EmitPixelLine(footprint[edge], footprint[(edge + 1) % 4], MathF.Max(1, scale), cameraColor, width, height);
        }

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
        if (!_skin.Ready) EmitQuad(
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
            if (!_skin.Control(rect, selected: selected, role: "control.tab")) EmitQuad(
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
                (_skin.Ready ? 10.0f : 8.0f) *
                scale;
            EmitIcon(
                RtsStrategicOverlayHudModel.ResolveIcon(
                    mode),
                _skin.Ready ? rect.X + (rect.Width - iconSize) * .5f : rect.X + 2 * scale,
                rect.Y + (_skin.Ready ? 1 : 3) * scale,
                iconSize,
                width,
                height);
            EmitText(
                RtsStrategicOverlayHudModel.ResolveShortLabel(
                    mode),
                rect.X + (_skin.Ready ? 2 : 12) * scale,
                rect.Y + (_skin.Ready ? 11 : 5) * scale,
                rect.Right -
                    1.0f *
                    scale,
                new Vector4(
                    0.90f,
                    0.94f,
                    0.92f,
                    1.0f),
                scale *
                    (_skin.Ready ? .8f : .62f),
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
        if (!_skin.Panel(legend, "panel.inset")) EmitQuad(
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

        Span<char> buffer = stackalloc char[96];
        var text = new HudTextBuilder(buffer);
        text.Append(RtsStrategicOverlayHudModel.Label(mode));
        if (mode != StrategicOverlayMode.None)
        {
            if (overlay is null) text.Append(" N/A - UPDATING");
            else
            {
                int count = mode switch
                {
                    StrategicOverlayMode.Logistics => overlay.LogisticsLinks.Count,
                    StrategicOverlayMode.Supply => overlay.Supply.Count,
                    StrategicOverlayMode.Sensors => overlay.Sensors.Count,
                    StrategicOverlayMode.Navigation => overlay.NavigationSectors.Count,
                    StrategicOverlayMode.Power => overlay.PowerEntities.Count,
                    _ => overlay.LogisticsNodes.Count + overlay.Supply.Count + overlay.PowerEntities.Count
                };
                text.Append(" LOCAL "); text.Append(count);
            }
        }
        float fittedScale = MathF.Min(scale * .8f, legend.Height / 30);
        EmitText(text.Written, legend.X + 3 * scale, legend.Y + 2 * scale, legend.Right - 3 * scale,
            GameplayHudVisualStyle.TextPrimary, fittedScale, width, height);
        EmitText(_bindings.Text(RtsStrategicOverlayHudModel.Legend(mode)), legend.X + 3 * scale, legend.Y + 15 * scale, legend.Right - 3 * scale,
            GameplayHudVisualStyle.TextSecondary, fittedScale, width, height);
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
                    "ESC CONTROLS",
                    layout,
                    width,
                    height);
                break;

            case PreAlphaUxMode.Paused:
                EmitSystemPanel(
                    "PAUSED",
                    _bindings.Text("ESC OR SPACE RESUME"),
                    "ESC CONTROLS",
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
                if (view.Placement.Visible)
                {
                    EmitOnboardingHint(
                        view.InteractionHint,
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
                _bindings.Text(PlayerSystemHudModel.GetHelpLine(
                    index)),
                x + 16.0f * scale,
                y + (44.0f + index * 24.0f) * scale,
                x + panelWidth - 12.0f * scale,
                GameplayHudVisualStyle.TextSecondary,
                scale * 0.72f,
                width,
                height);
        }
    }

    private void EmitGameplayLabels(RtsCamera camera, DebugDraw? draw, in GameplayHudLayout layout, int width, int height)
    {
        if (draw is not { Enabled: true }) return;
        float font = 14 * layout.Scale;
        float line = UiFontMetrics.Default.Description.LineHeight / UiFontMetrics.Default.Description.PixelSize * font;
        if (layout.SafeArea.Width < 16 * layout.Scale || layout.SafeArea.Height < line) return;
        Span<char> text = stackalloc char[256];
        foreach (var label in draw.Labels)
        {
            if (LastWorldLabels == _worldLabelBounds.Length) break;
            var point = camera.WorldToScreen(label.Position, width, height);
            if (!point.IsVisible) continue;
            float maximumWidth = MathF.Min(400 * layout.Scale, layout.SafeArea.Width);
            var visible = NotificationText.Ellipsized(label.Text, maximumWidth - 8 * layout.Scale, font, text);
            float labelWidth = UiFontMetrics.Default.Measure(visible, font) + 8 * layout.Scale;
            var bounds = new HudRect(Math.Clamp(point.Position.X - labelWidth * .5f, layout.SafeArea.X,
                layout.SafeArea.Right - labelWidth), point.Position.Y - line, labelWidth, line + 4 * layout.Scale);
            bool overlaps = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                overlaps = false;
                for (int i = 0; i < LastWorldLabels; i++) overlaps |= bounds.Intersects(_worldLabelBounds[i]);
                if (!overlaps) break;
                bounds = bounds with { Y = bounds.Y - bounds.Height };
            }
            if (overlaps || bounds.Y < layout.SafeArea.Y || bounds.Bottom > layout.SafeArea.Bottom ||
                bounds.Intersects(layout.SecondaryView) || bounds.Intersects(layout.TopStatusBar) ||
                bounds.Intersects(layout.Minimap) || bounds.Intersects(layout.OnboardingBounds)) continue;
            _worldLabelBounds[LastWorldLabels++] = bounds;
            EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height, new(.025f, .035f, .04f, .82f), width, height);
            var content = bounds with { X = bounds.X + 4 * layout.Scale, Width = bounds.Width - 8 * layout.Scale };
            if (!_text.AddCentered(visible, content, font, label.Color, width, height))
                EmitText(visible, content.X, content.Y, content.Right, label.Color, layout.Scale, width, height);
        }
    }

    private void EmitPlacementHintBar(in PlacementContextFeedbackView placement, in GameplayHudLayout layout,
        int width, int height, TimeSpan now, bool animate)
    {
        var state = OnboardingPresentation.Resolve(layout, 0);
        if (!state.Visible) return;
        bool conveyor = placement.Title == "CONVEYOR";
        bool ports = !string.IsNullOrEmpty(placement.PortInformation);
        int rows = ports ? 4 : conveyor ? 3 : 1;
        float lineRatio = UiFontMetrics.Default.Description.LineHeight / UiFontMetrics.Default.Description.PixelSize;
        float scale = MathF.Min(layout.Scale, state.Panel.Height / (lineRatio * 14 * rows + 12));
        if (scale <= 0) return;
        float font = 14 * scale, padding = 6 * scale;
        float line = lineRatio * font;
        var bounds = state.Panel with { Height = MathF.Min(state.Panel.Height, line * rows + 2 * padding) };
        if (bounds.IsEmpty) return;
        ulong revision = unchecked((uint)HashCode.Combine(placement.State, placement.Title, placement.Reason, placement.Hint, placement.PortInformation));
        var visual = new NotificationStackLayout(bounds, font, 1,
            new(new(default, Local: true), bounds, default, default, default, false, revision));
        _placementMotion.Update(visual, placement.SessionId, layout, now, animate && !conveyor && !ports);
        float opacity = _placementMotion.Opacity(0);
        _guidanceClip = bounds; _text.Clip = bounds; _skin.PanelClip(bounds);
        int vertexStart = _vertexCount;
        LastGuidanceBounds = bounds; LastGuidanceTextSize = font;
        if (!_skin.Panel(bounds, "panel.compact", NotificationSurfaceVisuals.BackgroundOpacity * opacity))
            EmitQuad(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                GameplayHudVisualStyle.PanelBackground with { W = NotificationSurfaceVisuals.BackgroundOpacity * opacity },
                width, height);
        Vector4 rail = placement.State switch
        {
            PlacementContextState.Invalid => GameplayHudVisualStyle.Critical,
            PlacementContextState.MissingMaterials or PlacementContextState.Stale => GameplayHudVisualStyle.Warning,
            PlacementContextState.Valid => GameplayHudVisualStyle.Focus,
            _ => GameplayHudVisualStyle.Border
        };
        rail.W *= opacity;
        EmitQuad(bounds.X, bounds.Y, 3 * scale, bounds.Height, rail, width, height);
        float iconSize = font;
        EmitIcon(placement.State is PlacementContextState.Invalid or PlacementContextState.MissingMaterials
            ? RtsUiIcon.StatusAlert : RtsUiIcon.CommandBuild, bounds.X + padding,
            bounds.Y + (bounds.Height - iconSize) * .5f, iconSize, width, height, opacity);
        Span<char> buffer = stackalloc char[512];
        var text = new HudTextBuilder(buffer);
        text.Append(placement.Title ?? "");
        text.Append(" / ");
        text.Append(placement.Reason ?? "");
        if (!conveyor && !ports)
        {
            text.Append(" / ");
            text.Append(placement.Hint ?? "");
        }
        var content = new HudRect(bounds.X + padding * 2 + iconSize,
            rows > 1 ? bounds.Y + padding : bounds.Y + (bounds.Height - line) * .5f,
            MathF.Max(1, bounds.Width - padding * 3 - iconSize), line);
        Span<char> shortText = stackalloc char[512];
        ReadOnlySpan<char> remaining = text.Written;
        for (int row = 0; row < rows; row++)
        {
            if (ports && row == 1) remaining = placement.PortInformation.AsSpan();
            if (rows > 1 && row == rows - 1) remaining = placement.Hint.AsSpan();
            scoped ReadOnlySpan<char> visible;
            if ((!ports && conveyor && row == 0) || (ports && row == 1))
            {
                visible = NotificationText.Line(remaining, content.Width, font, out int consumed);
                remaining = remaining[consumed..];
            }
            else visible = NotificationText.Ellipsized(remaining, content.Width, font, shortText);
            if (!_text.AddCentered(visible, content, font,
            GameplayHudVisualStyle.TextPrimary with { W = opacity }, width, height))
                EmitText(visible, content.X, content.Y, content.Right,
                GameplayHudVisualStyle.TextPrimary with { W = opacity }, scale, width, height);
            content = content with { Y = content.Y + line };
        }
        LastGuidanceVertexCount = _vertexCount - vertexStart;
        _guidanceClip = null; _text.Clip = null;
        _skin.PanelClip(new HudRect(0, 0, width, height));
    }

    private void EmitContextGuidance(in PreAlphaUxView view, bool placement, in GameplayHudLayout layout, int width, int height)
    {
        if (!placement && !string.IsNullOrEmpty(view.Guidance.ContentKey))
        { EmitDetailedGuidance(view, layout, width, height); return; }
        var state = placement ? OnboardingPresentation.Resolve(layout, 0) : view.Onboarding ?? OnboardingPresentation.Resolve(layout, 0);
        if (!state.Visible) return;
        var region = state.Panel;
        _guidanceClip = state.Clip;
        _text.Clip = state.Clip;
        _skin.PanelClip(state.Clip);
        // Account for wrapped text before choosing a compact type size, so later
        // context lines do not disappear after a long objective uses two rows.
        float scale = MathF.Min(layout.Scale, region.Height / 83);
        if (ContextHeight(view, placement, region, scale) > region.Height)
        {
            float low = 0, high = scale;
            for (int i = 0; i < 10; i++)
            {
                float candidate = (low + high) * .5f;
                if (ContextHeight(view, placement, region, candidate) <= region.Height) low = candidate;
                else high = candidate;
            }
            scale = low;
        }
        scale = MathF.Max(.875f, scale);
        LastGuidanceTextSize = 16 * scale;
        int start = _vertexCount;
        LastGuidanceBounds = state.VisiblePanel.IsEmpty ? state.Tab : state.VisiblePanel;
        float alpha = placement ? 1 : OnboardingPresentation.BackgroundAlpha;
        if (!_skin.Panel(region, "panel.compact", alpha)) EmitQuad(region.X, region.Y, region.Width, region.Height,
            GameplayHudVisualStyle.PanelBackground with { W = alpha }, width, height);
        EmitQuad(region.X, region.Y, 3 * scale, region.Height, GameplayHudVisualStyle.Focus, width, height);
        if (!placement && !state.Tab.IsEmpty)
        {
            var contentClip = state.Clip with { X = state.Tab.Right, Width = MathF.Max(0, state.Clip.Right - state.Tab.Right) };
            _text.Clip = contentClip; _guidanceClip = contentClip;
        }
        float y = region.Y + 8 * scale;
        if (!placement)
            ContextBlock(state.Compact ? "< HINT / SHIFT+F12" : "< MINIMIZE  /  SHIFT+F12", ref y, region, scale, false, 1, width, height);
        ContextBlock(placement ? view.Placement.Title : view.Guidance.Objective, ref y, region, scale, true, !placement && state.Compact ? 1 : 2, width, height);
        ContextBlock(placement ? view.Placement.Reason : view.Guidance.Binding, ref y, region, scale, false, !placement && state.Compact ? 1 : 2, width, height);
        if (placement || !state.Compact)
            ContextBlock(placement ? view.Placement.Hint : view.Guidance.Context, ref y, region, scale, false, 2, width, height);
        if (!placement && !state.Compact && view.Guidance.Next.Length > 0)
            ContextBlock(view.Guidance.Next, ref y, region, scale, false, 1, width, height);
        if (placement && view.Placement.Costs is { } costs)
        {
            Span<char> buffer = stackalloc char[128];
            for (int i = 0; i < costs.Count; i++)
            {
                var text = new HudTextBuilder(buffer);
                text.Append(costs[i].DisplayName); text.Append(" CORE ");
                text.Append(costs[i].AvailableQuantity); text.Append(" / NEED ");
                text.Append(costs[i].RequiredQuantity);
                ContextBlock(text.Written, ref y, region, scale, false, 1, width, height);
            }
        }
        _text.Clip = state.Clip; _guidanceClip = state.Clip;
        if (!placement && !state.Tab.IsEmpty)
        {
            var tab = state.Tab;
            if (!_skin.Panel(tab, "control.button_compact", OnboardingPresentation.BackgroundAlpha))
                EmitQuad(tab.X, tab.Y, tab.Width, tab.Height, GameplayHudVisualStyle.PanelRaised with { W = OnboardingPresentation.BackgroundAlpha }, width, height);
            float tabSize = state.TabTextSize;
            var label = state.TabLabelBounds;
            if (!_text.AddCentered("HINT", label, tabSize, GameplayHudVisualStyle.TextPrimary, width, height))
                EmitText("HINT", label.X, label.Y, label.Right, GameplayHudVisualStyle.TextPrimary,
                    tabSize / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
            _text.Add(">", tab.X + 10, tab.Y + tab.Height / 2, tabSize, GameplayHudVisualStyle.TextPrimary, width, height, tab.Width - 12);
        }
        if (!placement && state.Focused)
        {
            var focus = state.Tab.IsEmpty ? state.VisiblePanel : state.Tab;
            EmitQuad(focus.X, focus.Y, focus.Width, 3, GameplayHudVisualStyle.Focus, width, height);
        }
        _guidanceClip = null;
        _text.Clip = null;
        _skin.PanelClip(new(0, 0, width, height));
        LastGuidanceVertexCount = _vertexCount - start;
    }

    private void EmitDetailedGuidance(in PreAlphaUxView view, in GameplayHudLayout layout, int width, int height)
    {
        var state = view.Onboarding ?? OnboardingPresentation.Resolve(layout, 0, detailed: true);
        if (!state.Visible) return;
        if (!state.Detailed) state = OnboardingPresentation.Resolve(layout, state.Collapse, true, state.Focused, true);
        var panel = state.Panel;
        int start = _vertexCount;
        _guidanceClip = state.Clip; _text.Clip = state.Clip; _skin.PanelClip(state.Clip);
        LastGuidanceBounds = state.VisiblePanel.IsEmpty ? state.Tab : state.VisiblePanel;
        LastGuidanceTextSize = GuidanceCardLayout.TextSize(layout);
        if (!_skin.Panel(panel, "panel.compact", OnboardingPresentation.BackgroundAlpha))
            EmitQuad(panel.X, panel.Y, panel.Width, panel.Height, GameplayHudVisualStyle.PanelBackground with { W = OnboardingPresentation.BackgroundAlpha }, width, height);
        if (!state.Tab.IsEmpty)
        {
            var content = state.Clip with { X = state.Tab.Right, Width = MathF.Max(0, state.Clip.Right - state.Tab.Right) };
            _text.Clip = content; _guidanceClip = content;
        }
        float y = panel.Y + 8;
        ContextBlock("MINIMIZE / SHIFT+F12", ref y, panel, .875f, false, 1, width, height);
        y = panel.Y + 28;
        ContextBlock(view.Guidance.Objective, ref y, panel, .9f, true, 2, width, height);
        y = panel.Y + 68;
        ContextBlock(view.Guidance.Next, ref y, panel, .875f, false, 2, width, height);
        y = panel.Y + GuidanceCardLayout.Header;
        foreach (var line in view.Guidance.Context.AsSpan().EnumerateLines())
        {
            if (!_text.Add(line, panel.X + 10, y, LastGuidanceTextSize, GameplayHudVisualStyle.TextPrimary,
                width, height, MathF.Max(1, panel.Width - 20)))
                EmitText(line, panel.X + 10, y, panel.Right - 10, GameplayHudVisualStyle.TextPrimary,
                    LastGuidanceTextSize / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
            y += LastGuidanceTextSize * 1.4f;
        }
        for (int i = 0; i < 6; i++)
        {
            var control = GuidanceCardLayout.Control(state, i);
            if (control.IsEmpty) continue;
            if (!_skin.Panel(control, "control.button_compact", OnboardingPresentation.BackgroundAlpha))
                EmitQuad(control.X, control.Y, control.Width, control.Height, GameplayHudVisualStyle.PanelRaised with { W = OnboardingPresentation.BackgroundAlpha }, width, height);
            if (!_text.AddCentered(GuidanceCardLayout.Label(i), control, 14, GameplayHudVisualStyle.TextPrimary, width, height))
                EmitText(GuidanceCardLayout.Label(i), control.X + 4, control.Y + 6, control.Right - 4,
                    GameplayHudVisualStyle.TextPrimary, 14 / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
            if (state.Focused && state.FocusControl == i) EmitQuad(control.X, control.Y, control.Width, 2, GameplayHudVisualStyle.Focus, width, height);
        }
        Span<char> buffer = stackalloc char[96];
        var label = new HudTextBuilder(buffer);
        label.Append("SECTION "); label.Append(view.Guidance.Section + 1); label.Append(" / "); label.Append(EarlyGameGuidanceCatalog.Steps.Length);
        label.Append("   PAGE "); label.Append(view.Guidance.Page + 1); label.Append(" / "); label.Append(view.Guidance.PageCount);
        if (!_text.Add(label.Written, panel.X + 10, panel.Bottom - 15, 14, GameplayHudVisualStyle.TextPrimary, width, height, MathF.Max(1, panel.Width - 20)))
            EmitText(label.Written, panel.X + 10, panel.Bottom - 15, panel.Right - 10,
                GameplayHudVisualStyle.TextPrimary, 14 / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
        _text.Clip = state.Clip; _guidanceClip = state.Clip;
        if (!state.Tab.IsEmpty)
        {
            if (!_skin.Panel(state.Tab, "control.button_compact", OnboardingPresentation.BackgroundAlpha))
                EmitQuad(state.Tab.X, state.Tab.Y, state.Tab.Width, state.Tab.Height,
                    GameplayHudVisualStyle.PanelRaised with { W = OnboardingPresentation.BackgroundAlpha }, width, height);
            if (!_text.AddCentered("HINT", state.TabLabelBounds, state.TabTextSize, GameplayHudVisualStyle.TextPrimary, width, height))
                EmitText("HINT", state.TabLabelBounds.X, state.TabLabelBounds.Y, state.TabLabelBounds.Right,
                    GameplayHudVisualStyle.TextPrimary, state.TabTextSize / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
            if (!_text.Add(">", state.Tab.X + 10, state.Tab.Y + state.Tab.Height / 2, state.TabTextSize,
                GameplayHudVisualStyle.TextPrimary, width, height, state.Tab.Width - 12))
                EmitText(">", state.Tab.X + 10, state.Tab.Y + state.Tab.Height / 2, state.Tab.Right,
                    GameplayHudVisualStyle.TextPrimary, state.TabTextSize / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
        }
        if (state.Focused && (state.FocusControl == 6 || !state.Tab.IsEmpty))
        {
            var focus = state.Tab.IsEmpty ? state.VisiblePanel : state.Tab;
            EmitQuad(focus.X, focus.Y, focus.Width, 3, GameplayHudVisualStyle.Focus, width, height);
        }
        _guidanceClip = null; _text.Clip = null; _skin.PanelClip(new(0, 0, width, height));
        LastGuidanceVertexCount = _vertexCount - start;
    }

    private static float ContextHeight(in PreAlphaUxView view, bool placement, in HudRect region, float scale)
    {
        float textWidth = MathF.Max(0, region.Width - 20 * scale);
        int lines = ContextLines(placement ? view.Placement.Title : view.Guidance.Objective, textWidth, 18 * scale, true, 2) +
            ContextLines(placement ? view.Placement.Reason : view.Guidance.Binding, textWidth, 16 * scale, false, 2) +
            ContextLines(placement ? view.Placement.Hint : view.Guidance.Context, textWidth, 16 * scale, false, 2);
        if (!placement && view.Guidance.Next.Length > 0) lines++;
        if (placement && view.Placement.Costs is { } amounts) lines += amounts.Count;
        return (18 + 23 * Math.Max(1, lines + (placement ? 0 : 1))) * scale;
    }

    private static int ContextLines(ReadOnlySpan<char> text, float width, float size, bool bold, int maximum)
    {
        int lines = 0;
        while (lines < maximum && !text.IsEmpty)
        {
            int count = UiFontMetrics.Default.FitPrefix(text, width, size, bold);
            if (count <= 0) break;
            if (count < text.Length && lines + 1 < maximum)
            {
                int space = text[..count].LastIndexOf(' ');
                if (space > 0) count = space;
            }
            lines++;
            text = text[count..].TrimStart();
        }
        return lines;
    }

    private void ContextBlock(ReadOnlySpan<char> text, ref float y, in HudRect region,
        float scale, bool heading, int maximumLines, int width, int height)
    {
        float size = (heading ? 18 : 16) * scale;
        float lineHeight = 23 * scale;
        float availableWidth = MathF.Max(0, region.Width - 20 * scale);
        for (int line = 0; line < maximumLines && !text.IsEmpty && y + lineHeight <= region.Bottom - 6 * scale; line++)
        {
            int count = UiFontMetrics.Default.FitPrefix(text, availableWidth, size, heading);
            if (count <= 0) return;
            if (count < text.Length && line + 1 < maximumLines)
            {
                int space = text[..count].LastIndexOf(' ');
                if (space > 0) count = space;
            }
            ReadOnlySpan<char> value = line + 1 == maximumLines ? text : text[..count];
            if (!_text.Add(value, region.X + 10 * scale, y, size, GameplayHudVisualStyle.TextPrimary,
                    width, height, availableWidth, heading))
                EmitText(value, region.X + 10 * scale, y, region.Right - 10 * scale,
                    GameplayHudVisualStyle.TextPrimary, size / UiTypography.Style(UiTextRole.Telemetry).Size, width, height);
            y += lineHeight;
            text = text[count..].TrimStart();
        }
    }

    private void EmitOnboardingHint(
        string? interactionHint,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        string hint =
            !string.IsNullOrEmpty(interactionHint) ? interactionHint : _bindings.Text(PlayerSystemHudModel.OnboardingHint);
        float gap =
            GameplayHudVisualStyle.CompactGap *
            scale;
        float x =
            layout.SecondaryView.Right +
            gap;
        float right =
            layout.Minimap.X -
            gap;

        if (right <= x)
        {
            return;
        }

        float y =
            MathF.Max(
                layout.TopStatusBar.Bottom +
                gap,
                layout.SafeArea.Bottom -
                18.0f * scale);

        EmitText(
            hint,
            x,
            y,
            right,
            GameplayHudVisualStyle.TextSecondary,
            scale,
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

        if (!_skin.Panel(new HudRect(region.X, region.Y, region.Width, MathF.Min(region.Height, panelHeight)), "panel.compact")) EmitQuad(
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

            var card = new HudRect(region.X + padding, y, region.Width - padding * 2, rowHeight - 2 * scale);
            if (!_skin.Control(card, selected: group.IsActive || group.IsSelected)) EmitQuad(
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
        // Physical client pixels: independent of UI zoom and monitor DPI.
        float thickness = RtsSelectionVisualStyle.MarqueeBorderPixels;

        minimum = Vector2.Clamp(minimum, Vector2.Zero, new Vector2(width, height));
        maximum = Vector2.Clamp(maximum, Vector2.Zero, new Vector2(width, height));
        thickness = MathF.Min(thickness, MathF.Min(maximum.X - minimum.X, maximum.Y - minimum.Y));
        if (thickness <= 0.0f)
        {
            return;
        }

        EmitQuad(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y,
            RtsSelectionVisualStyle.MarqueeFill, width, height);
        EmitRectangleOutline(minimum, maximum, thickness,
            RtsSelectionVisualStyle.MarqueeBorder, width, height);
    }

    private void EmitRectangleOutline(
        Vector2 minimum, Vector2 maximum, float thickness, Vector4 color, int width, int height)
    {
        thickness = MathF.Min(thickness, MathF.Min(maximum.X - minimum.X, maximum.Y - minimum.Y));
        EmitQuad(minimum.X, minimum.Y, maximum.X - minimum.X, thickness, color, width, height);
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
        if (_text.Add(value, x, y, UiTypography.Style(UiTextRole.Telemetry).Size * glyphScale, color, width, height, right - x)) return;

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
        // Whitespace advances the cursor without emitting geometry.
        if (character == ' ')
        {
            return;
        }

        string pattern =
            TextGlyphPattern(
                character);

        // Malformed glyph data must never terminate the render loop.
        if (pattern.Length != 35)
        {
            pattern = TextGlyphPattern('?');
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
        int height,
        float opacity = 1)
    {
        if (_skin.Icon(icon, x, y, size, new Vector4(1,1,1,opacity))) return;
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);
        string pattern =
            GlyphPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
        color.W *= opacity;
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

    private void EmitPixelLine(Vector2 a, Vector2 b, float thickness, Vector4 color, int width, int height)
    {
        if (_vertexCount + 6 > _vertexLimit || Vector2.DistanceSquared(a,b) < .01f) return;
        Vector2 normal = Vector2.Normalize(new Vector2(a.Y-b.Y,b.X-a.X)) * thickness * .5f;
        Vector2 p0 = a+normal, p1 = b+normal, p2 = b-normal, p3 = a-normal;
        p0 = new(p0.X/width*2-1,1-p0.Y/height*2); p1 = new(p1.X/width*2-1,1-p1.Y/height*2);
        p2 = new(p2.X/width*2-1,1-p2.Y/height*2); p3 = new(p3.X/width*2-1,1-p3.Y/height*2);
        _vertices[_vertexCount++] = new(p0,color); _vertices[_vertexCount++] = new(p1,color); _vertices[_vertexCount++] = new(p2,color);
        _vertices[_vertexCount++] = new(p0,color); _vertices[_vertexCount++] = new(p2,color); _vertices[_vertexCount++] = new(p3,color);
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
        if (_guidanceClip is { } clip)
        {
            var r = OnboardingPresentation.Intersect(new(x, y, quadWidth, quadHeight), clip);
            x = r.X; y = r.Y; quadWidth = r.Width; quadHeight = r.Height;
        }
        if (_vertexCount >
                _vertexLimit -
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
                CullMode = GraphicsCullMode.None,
                DepthEnabled = false,
                AlphaBlendEnabled = true
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
            ' ' => "00000000000000000000000000000000000",
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

        public void Append(double value)
        {
            if (value.TryFormat(_buffer[_length..], out int written, "F0", System.Globalization.CultureInfo.InvariantCulture))
                _length += written;
        }

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
