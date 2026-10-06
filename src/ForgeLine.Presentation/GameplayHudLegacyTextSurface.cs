using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class GameplayHudLegacyTextSurface : IGameplayHudSurface
{
    private readonly DevelopmentOverlayRenderer _renderer;

    public GameplayHudLegacyTextSurface(
        IGraphicsDevice graphics)
    {
        _renderer =
            new DevelopmentOverlayRenderer(
                graphics);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.ActionDock |
        GameplayHudRegion.AlertStack |
        GameplayHudRegion.SecondaryView |
        GameplayHudRegion.GlobalOverlay;

    public void Render(
        in GameplayHudRenderContext context)
    {
        _renderer.Render(
            context.Graphics,
            default,
            context.Camera,
            playerExperience:
                context.Snapshot.PlayerExperience,
            showDevelopmentMetrics: false,
            playerActions:
                context.Snapshot.PlayerActions,
            actionPanel:
                context.ActionPanel,
            tacticalTargeting:
                context.TacticalTargeting,
            activeFormation:
                context.ActiveFormation,
            preAlphaUx:
                context.PreAlphaUx,
            uiScale:
                context.Layout.Scale,
            gameplayOverlay:
                context.GameplayOverlay);
    }

    public void Dispose() =>
        _renderer.Dispose();
}
