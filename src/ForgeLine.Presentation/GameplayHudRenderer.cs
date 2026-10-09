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

        _statusSurface = new ResourcePowerHudSurface(graphics, runtimeAssets);
        _surfaces =
        [
            _statusSurface,
            new SelectionInspectorHudSurface(
                graphics,
                runtimeAssets),
            new PlayerActionDockHudSurface(
                graphics,
                runtimeAssets),
            new RtsInformationHudSurface(
                graphics,
                runtimeAssets),
            new WorldHoverTooltipSurface(graphics)
        ];
    }

    public GameplayHudState State { get; private set; }

    public int LastRenderedVertexCount { get; private set; }

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
        HoverTooltipView hoverTooltip = default)
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
            _statusSurface?.RenderWaiting(context, layout, runtimeMetrics);
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
                hoverTooltip);

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
