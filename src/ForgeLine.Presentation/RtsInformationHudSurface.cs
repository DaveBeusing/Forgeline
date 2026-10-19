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

    public UiTextDiagnostics TextDiagnostics => _renderer.TextDiagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _renderer.SkinDiagnostics;
    public int LastRenderedVertexCount => _renderer.LastRenderedVertexCount;
    public int LastGuidanceVertexCount => _renderer.LastGuidanceVertexCount;

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
            context.PreAlphaUx,
            context.TacticalTargeting,
            context.PresentationTime,
            context.AnimateNotifications);
    }

    public void Dispose() =>
        _renderer.Dispose();
}
