using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>
/// Shared visual colors for the interactive frontend and battlefield HUD.
/// Colors are expressed in the renderer's existing normalized RGBA space.
/// Gameplay states also retain their geometric focus and warning markers.
/// Established HUD colors are preserved to keep reviewed gameplay visual fixtures stable.
/// </summary>
internal static class ForgeLineUiPalette
{
    public static readonly Vector4 Background = new(0.027f, 0.039f, 0.050f, 1f);
    public static readonly Vector4 BackgroundDeep = new(0.014f, 0.024f, 0.032f, 1f);
    public static readonly Vector4 BackgroundRaised = new(0.045f, 0.063f, 0.073f, 1f);

    public static readonly Vector4 Surface = new(0.05f, 0.06f, 0.06f, 0.97f);
    public static readonly Vector4 SurfaceRaised = new(0.075f, 0.09f, 0.09f, 0.98f);
    public static readonly Vector4 SurfaceHover = new(0.09f, 0.12f, 0.12f, 0.98f);
    public static readonly Vector4 SurfaceActive = new(0.12f, 0.18f, 0.19f, 0.98f);
    public static readonly Vector4 SurfacePressed = new(0.10f, 0.15f, 0.16f, 0.98f);
    public static readonly Vector4 SurfaceDisabled = new(0.055f, 0.06f, 0.06f, 0.88f);

    public static readonly Vector4 Border = new(0.32f, 0.35f, 0.34f, 0.90f);
    public static readonly Vector4 BorderSubtle = new(0.155f, 0.196f, 0.212f, 1f);
    public static readonly Vector4 Accent = new(0.88f, 0.80f, 0.42f, 1f);
    public static readonly Vector4 AccentDim = new(0.467f, 0.357f, 0.180f, 1f);

    public static readonly Vector4 TextPrimary = new(0.93f, 0.96f, 0.95f, 1f);
    public static readonly Vector4 TextSecondary = new(0.65f, 0.70f, 0.69f, 1f);
    public static readonly Vector4 TextMuted = new(0.520f, 0.586f, 0.592f, 1f);
    public static readonly Vector4 TextDisabled = new(0.384f, 0.417f, 0.424f, 1f);
    public static readonly Vector4 Warning = new(0.94f, 0.66f, 0.24f, 1f);
    public static readonly Vector4 Critical = new(0.96f, 0.34f, 0.20f, 1f);
}
