using ForgeLine.Graphics;

namespace ForgeLine.Presentation.RenderContracts;

public readonly record struct RenderPreparationRequest(
    int Width, int Height, float Alpha,
    GraphicsDirectionalShadowFrame? Shadow = null,
    int MaximumShadowCasters = int.MaxValue,
    double? PresentationSeconds = null,
    bool MetricsEnabled = false);
