using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

[Flags]
public enum GameplayHudRegion : byte
{
    None = 0,
    TopStatusBar = 1 << 0,
    SelectionInspector = 1 << 1,
    ActionDock = 1 << 2,
    AlertStack = 1 << 3,
    Minimap = 1 << 4,
    SecondaryView = 1 << 5,
    GlobalOverlay = 1 << 6
}

public readonly record struct GameplayHudRenderContext(
    IGraphicsCommandContext Graphics,
    RtsCamera Camera,
    PresentationSnapshot Snapshot,
    AxisAlignedBounds WorldBounds,
    RtsInformationLayerView InformationLayer,
    PlayerActionPanelView ActionPanel,
    TacticalTargetingView TacticalTargeting,
    FormationTemplate ActiveFormation,
    CombatGroupOverviewView CombatGroups,
    PreAlphaUxView PreAlphaUx,
    GameplayHudLayout Layout,
    uint Dpi,
    float UiScale,
    DebugDraw? GameplayOverlay,
    RuntimeMetricsView RuntimeMetrics = default);

public interface IGameplayHudSurface : IDisposable
{
    GameplayHudRegion Regions { get; }

    int LastRenderedVertexCount => 0;

    void Render(
        in GameplayHudRenderContext context);
}
