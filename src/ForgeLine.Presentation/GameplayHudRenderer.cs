using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class GameplayHudRenderer : IDisposable
{
    private readonly IGameplayHudSurface[] _surfaces;
    private bool _disposed;

    public GameplayHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _surfaces =
        [
            new ResourcePowerHudSurface(
                graphics,
                runtimeAssets),
            new SelectionInspectorHudSurface(
                graphics,
                runtimeAssets),
            new PlayerActionDockHudSurface(
                graphics,
                runtimeAssets),
            new RtsInformationHudSurface(
                graphics,
                runtimeAssets),
            new GameplayHudLegacyTextSurface(
                graphics)
        ];
    }

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
        PresentationSnapshot snapshot,
        in AxisAlignedBounds worldBounds,
        in RtsInformationLayerView informationLayer,
        in PlayerActionPanelView actionPanel,
        in TacticalTargetingView tacticalTargeting,
        FormationTemplate activeFormation,
        CombatGroupOverviewView combatGroups,
        in PreAlphaUxView preAlphaUx,
        uint dpi,
        float uiScale,
        DebugDraw? gameplayOverlay = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(combatGroups);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);
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
                gameplayOverlay);

        foreach (IGameplayHudSurface surface in _surfaces)
        {
            surface.Render(
                renderContext);
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
