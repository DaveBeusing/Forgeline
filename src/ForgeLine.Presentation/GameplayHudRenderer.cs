using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class GameplayHudRenderer : IDisposable
{
    private readonly RtsInformationOverlayRenderer _informationRenderer;
    private readonly DevelopmentOverlayRenderer _playerTextRenderer;
    private bool _disposed;

    public GameplayHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _informationRenderer =
            new RtsInformationOverlayRenderer(
                graphics,
                runtimeAssets);
        _playerTextRenderer =
            new DevelopmentOverlayRenderer(
                graphics);
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
        in PreAlphaUxView preAlphaUx,
        uint dpi,
        float uiScale,
        DebugDraw? gameplayOverlay = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(snapshot);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);

        _informationRenderer.Render(
            context,
            camera,
            snapshot,
            worldBounds,
            informationLayer,
            dpi,
            uiScale);

        _playerTextRenderer.Render(
            context,
            default,
            camera,
            playerExperience:
                snapshot.PlayerExperience,
            showDevelopmentMetrics: false,
            playerActions:
                snapshot.PlayerActions,
            actionPanel:
                actionPanel,
            tacticalTargeting:
                tacticalTargeting,
            activeFormation:
                activeFormation,
            preAlphaUx:
                preAlphaUx,
            uiScale:
                layout.Scale,
            gameplayOverlay:
                gameplayOverlay);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _playerTextRenderer.Dispose();
        _informationRenderer.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}
