using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class RtsInformationHudSurface : IGameplayHudSurface
{
    private readonly RtsInformationOverlayRenderer _renderer;

    public RtsInformationHudSurface(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _renderer =
            new RtsInformationOverlayRenderer(
                graphics,
                runtimeAssets);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.Minimap |
        GameplayHudRegion.SecondaryView |
        GameplayHudRegion.GlobalOverlay;

    public void Render(
        in GameplayHudRenderContext context)
    {
        _renderer.Render(
            context.Graphics,
            context.Camera,
            context.Snapshot,
            context.WorldBounds,
            context.InformationLayer,
            context.CombatGroups,
            context.Dpi,
            context.UiScale,
            context.PreAlphaUx);
    }

    public void Dispose() =>
        _renderer.Dispose();
}
