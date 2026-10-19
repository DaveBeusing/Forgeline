using System.Numerics;

namespace ForgeLine.Presentation;

public readonly record struct HudRect(
    float X,
    float Y,
    float Width,
    float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool IsEmpty =>
        Width <= 0.0f ||
        Height <= 0.0f;

    public bool Contains(Vector2 point) =>
        !IsEmpty &&
        point.X >= X &&
        point.X <= Right &&
        point.Y >= Y &&
        point.Y <= Bottom;

    public bool Intersects(in HudRect other) =>
        !IsEmpty &&
        !other.IsEmpty &&
        X < other.Right &&
        Right > other.X &&
        Y < other.Bottom &&
        Bottom > other.Y;
}

/// <summary>Published geometry for the remaining persistent HUD owners.</summary>
public readonly record struct GameplayHudLayout(
    int ViewportWidth, int ViewportHeight, float Scale, HudRect SafeArea,
    HudRect TopStatusBar, HudRect AlertStack, HudRect Minimap, HudRect SecondaryView)
{
    public const int ContractVersion = 4;
    public float Gap => MathF.Min(GameplayHudVisualStyle.ResolveSafeMargin(Scale), 16);
    public HudRect NotificationBounds(bool expanded = false) => AlertStack;
    public float NotificationScale => MathF.Min(Scale, AlertStack.Width / 608);
    public HudRect OnboardingBounds => SafeArea.IsEmpty ? default : new(SafeArea.X, SafeArea.Y,
        MathF.Max(0, TopStatusBar.X - Gap - SafeArea.X),
        MathF.Min(156 * MathF.Min(Scale, 1.5f), MathF.Max(0, SecondaryView.Y - Gap - SafeArea.Y)));

    public static GameplayHudLayout Create(int viewportWidth, int viewportHeight, uint dpi, float uiScale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);
        float scale = RtsUiLayout.ScaleForDpi(dpi) * (float.IsFinite(uiScale) ? Math.Clamp(uiScale, .75f, 2) : 1);
        float margin = GameplayHudVisualStyle.ResolveSafeMargin(scale);
        var safe = new HudRect(margin, margin, MathF.Max(0, viewportWidth - margin * 2), MathF.Max(0, viewportHeight - margin * 2));
        if (safe.IsEmpty || viewportWidth < 320 || viewportHeight < 240)
            return new(viewportWidth, viewportHeight, scale, default, default, default, default, default);
        float gap = MathF.Min(margin, 16);
        float topWidth = MathF.Min(1216 * scale, safe.Width * .70f);
        bool twoRows = topWidth < ResourceStripLayout.WideDesignWidth * scale;
        float stripScale = MathF.Min(scale, topWidth / (twoRows ? ResourceStripLayout.CompactDesignWidth : ResourceStripLayout.WideDesignWidth));
        var top = new HudRect(safe.Right - topWidth, safe.Y, topWidth, MathF.Min((twoRows ? 80 : 48) * stripScale, safe.Height));
        float mapSize = MathF.Min(GameplayHudVisualStyle.MinimapSize * scale, MathF.Min(safe.Width * .28f, safe.Height * .34f));
        var map = new HudRect(safe.Right - mapSize, safe.Bottom - mapSize, mapSize, mapSize);
        float notificationWidth = MathF.Min(608 * scale, safe.Width * .46f);
        float notificationTop = top.Bottom + gap;
        var alerts = new HudRect(safe.Right - notificationWidth, notificationTop, notificationWidth,
            MathF.Min(224 * MathF.Max(1, MathF.Min(scale, notificationWidth / 608)), MathF.Max(0, map.Y - gap - notificationTop)));
        float leftWidth = MathF.Max(0, top.X - gap - safe.X);
        float secondaryHeight = MathF.Min(180 * MathF.Min(scale, 1.25f), safe.Height * .30f);
        var secondary = new HudRect(safe.X, safe.Bottom - secondaryHeight, leftWidth, secondaryHeight);
        return new(viewportWidth, viewportHeight, scale, safe, top, alerts, map, secondary);
    }
}
