using System.Numerics;

namespace ForgeLine.Presentation;

public enum OnboardingPhase { Hidden, Expanded, Collapsing, Collapsed, Expanding }

/// <summary>One copied physical-pixel transform shared by painting and input.</summary>
public readonly record struct OnboardingPresentation(OnboardingPhase Phase, HudRect Panel, HudRect Clip,
    HudRect VisiblePanel, HudRect Tab, float Collapse, bool Focused = false)
{
    public const int ContractVersion = 2;
    public bool Detailed { get; init; }
    public int FocusControl { get; init; } = -1;
    public const float BackgroundAlpha = .84f;
    public const float TabWidth = 56;
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);
    public string AccessibleName => Collapse > 0 ? "Restore onboarding hint" : "Minimize onboarding hint";
    public HudRect TabLabelBounds => new(Tab.X + 4, Tab.Y + 4, MathF.Max(0, Tab.Width - 8), Tab.Height * .5f);
    public float TabTextSize => MathF.Min(24, MathF.Max(14, Tab.Width * 16 / TabWidth));
    public bool Compact => Clip.Width < 260 || Clip.Height < 140;
    public bool Visible => Phase != OnboardingPhase.Hidden;
    public bool Contains(Vector2 point) => Visible && (VisiblePanel.Contains(point) || Tab.Contains(point));
    public static OnboardingPresentation Resolve(in GameplayHudLayout layout, float collapse, bool visible = true, bool focused = false, bool detailed = false)
    {
        var clip = GameplayGuidanceLayout.Resolve(layout, detailed);
        if (!visible || clip.IsEmpty) return default;
        collapse = Math.Clamp(collapse, 0, 1);
        var panel = clip with { X = clip.X - clip.Width * collapse };
        var painted = Intersect(panel, clip);
        var tab = collapse > 0 ? new HudRect(clip.X, clip.Y,
            MathF.Min(clip.Width, TabWidth * MathF.Max(1, MathF.Min(layout.Scale, 1.5f))),
            MathF.Min(clip.Height, 48 * MathF.Max(1, MathF.Min(layout.Scale, 1.5f)))) : default;
        return new(collapse == 0 ? OnboardingPhase.Expanded : collapse == 1 ? OnboardingPhase.Collapsed : OnboardingPhase.Collapsing,
            panel, clip, painted, tab, collapse, focused) { Detailed = detailed };
    }
    public static HudRect Intersect(HudRect a, HudRect b)
    {
        float x = MathF.Max(a.X, b.X), y = MathF.Max(a.Y, b.Y);
        return new(x, y, MathF.Max(0, MathF.Min(a.Right, b.Right) - x), MathF.Max(0, MathF.Min(a.Bottom, b.Bottom) - y));
    }
}
