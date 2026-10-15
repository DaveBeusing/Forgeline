using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum GameplayHudState : byte
{
    WaitingForSnapshot,
    WaitingForPlayerData,
    Ready
}

public sealed class GameplayHudRenderer : IDisposable
{
    private readonly IGameplayHudSurface[] _surfaces;
    private readonly ResourcePowerHudSurface? _statusSurface;
    private bool _disposed;

    public GameplayHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var prepared = new List<IGameplayHudSurface>();
        try
        {
            _statusSurface = new ResourcePowerHudSurface(graphics, runtimeAssets);
            prepared.Add(_statusSurface);
            prepared.Add(new SelectionInspectorHudSurface(graphics, runtimeAssets));
            prepared.Add(new PlayerActionDockHudSurface(graphics, runtimeAssets));
            prepared.Add(new RtsInformationHudSurface(graphics, runtimeAssets));
            prepared.Add(new WorldHoverTooltipSurface(graphics, runtimeAssets));
            prepared.Add(new OperationsHudSurface(graphics));
            _surfaces = prepared.ToArray();
        }
        catch
        {
            for (int i = prepared.Count - 1; i >= 0; i--) prepared[i].Dispose();
            throw;
        }
    }

    public GameplayHudState State { get; private set; }

    public int LastRenderedVertexCount { get; private set; }

    public GameplayHudSkinDiagnostics SkinDiagnostics
    {
        get
        {
            GameplayHudSkinDiagnostics result = default;
            foreach (var surface in _surfaces) result = result.Combine(surface.SkinDiagnostics);
            return result;
        }
    }

    public UiTextDiagnostics TextDiagnostics
    {
        get
        {
            int glyphs = 0, vertices = 0, missing = 0, dropped = 0; long upload = 0; bool ready = true; string? failure = null;
            foreach (var surface in _surfaces)
            {
                var text = surface.TextDiagnostics; glyphs += text.Glyphs; vertices += text.Vertices;
                missing += text.MissingGlyphs; dropped += text.DroppedGlyphs; upload += text.UploadBytes;
                ready &= text.AtlasReady; failure ??= text.FailureReason;
            }
            return new(glyphs, vertices, missing, dropped, upload, ready, failure);
        }
    }

    public int LastHoverTooltipVertexCount { get; private set; }
    public int LastGuidanceVertexCount { get; private set; }

    public static GameplayHudState ResolveState(PresentationSnapshot? snapshot) =>
        snapshot is null ? GameplayHudState.WaitingForSnapshot :
        snapshot.PlayerExperience is null ? GameplayHudState.WaitingForPlayerData : GameplayHudState.Ready;

    internal GameplayHudRenderer(
        IGameplayHudSurface[] surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        if (surfaces.Any(
                static surface =>
                    surface is null))
        {
            throw new ArgumentException(
                "Gameplay HUD surfaces cannot contain null entries.",
                nameof(surfaces));
        }

        _surfaces =
            surfaces.ToArray();
    }

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera,
        PresentationSnapshot? snapshot,
        in AxisAlignedBounds worldBounds,
        in RtsInformationLayerView informationLayer,
        in PlayerActionPanelView actionPanel,
        in TacticalTargetingView tacticalTargeting,
        FormationTemplate activeFormation,
        CombatGroupOverviewView combatGroups,
        in PreAlphaUxView preAlphaUx,
        uint dpi,
        float uiScale,
        DebugDraw? gameplayOverlay = null,
        RuntimeMetricsView runtimeMetrics = default,
        HoverTooltipView hoverTooltip = default,
        bool showRuntimeMetrics = false,
        OperationsView operations = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(combatGroups);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);
        State = ResolveState(snapshot);
        LastRenderedVertexCount = 0;
        LastHoverTooltipVertexCount = 0;
        LastGuidanceVertexCount = 0;
        if (snapshot is null)
        {
            _statusSurface?.RenderWaiting(context, layout, runtimeMetrics, showRuntimeMetrics);
            LastRenderedVertexCount = _statusSurface?.LastRenderedVertexCount ?? 0;
            return;
        }
        var renderContext =
            new GameplayHudRenderContext(
                context,
                camera,
                snapshot,
                worldBounds,
                informationLayer,
                actionPanel,
                tacticalTargeting,
                activeFormation,
                combatGroups,
                preAlphaUx,
                layout,
                dpi,
                uiScale,
                gameplayOverlay,
                runtimeMetrics,
                hoverTooltip,
                showRuntimeMetrics,
                operations);

        foreach (IGameplayHudSurface surface in _surfaces)
        {
            surface.Render(
                renderContext);
            LastRenderedVertexCount += surface.LastRenderedVertexCount;
            if (surface is RtsInformationHudSurface information)
                LastGuidanceVertexCount = information.LastGuidanceVertexCount;
            if (surface is WorldHoverTooltipSurface)
                LastHoverTooltipVertexCount = surface.LastRenderedVertexCount;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int index =
                 _surfaces.Length - 1;
             index >= 0;
             index--)
        {
            _surfaces[index].Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}
