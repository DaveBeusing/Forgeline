using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>
/// Shared visual colors for the interactive frontend and battlefield HUD.
/// Colors are expressed in the renderer's existing normalized RGBA space.
/// Gameplay states also retain their geometric focus and warning markers.
/// </summary>
internal static class ForgeLineUiPalette
{
    public static readonly Vector4 Background = new(0.027f, 0.039f, 0.050f, 1f);
    public static readonly Vector4 BackgroundDeep = new(0.014f, 0.024f, 0.032f, 1f);
    public static readonly Vector4 BackgroundRaised = new(0.045f, 0.063f, 0.073f, 1f);

    public static readonly Vector4 Surface = new(0.050f, 0.068f, 0.077f, 0.97f);
    public static readonly Vector4 SurfaceRaised = new(0.075f, 0.098f, 0.110f, 0.98f);
    public static readonly Vector4 SurfaceHover = new(0.112f, 0.137f, 0.147f, 0.98f);
    public static readonly Vector4 SurfaceActive = new(0.155f, 0.172f, 0.153f, 0.99f);
    public static readonly Vector4 SurfacePressed = new(0.205f, 0.185f, 0.122f, 0.99f);
    public static readonly Vector4 SurfaceDisabled = new(0.047f, 0.055f, 0.060f, 0.90f);

    public static readonly Vector4 Border = new(0.345f, 0.383f, 0.382f, 0.94f);
    public static readonly Vector4 BorderSubtle = new(0.155f, 0.196f, 0.212f, 1f);
    public static readonly Vector4 Accent = new(0.945f, 0.733f, 0.363f, 1f);
    public static readonly Vector4 AccentDim = new(0.467f, 0.357f, 0.180f, 1f);

    public static readonly Vector4 TextPrimary = new(0.945f, 0.952f, 0.931f, 1f);
    public static readonly Vector4 TextSecondary = new(0.700f, 0.746f, 0.737f, 1f);
    public static readonly Vector4 TextMuted = new(0.520f, 0.586f, 0.592f, 1f);
    public static readonly Vector4 TextDisabled = new(0.384f, 0.417f, 0.424f, 1f);
    public static readonly Vector4 Warning = new(0.953f, 0.643f, 0.298f, 1f);
    public static readonly Vector4 Critical = new(0.964f, 0.353f, 0.267f, 1f);
}
