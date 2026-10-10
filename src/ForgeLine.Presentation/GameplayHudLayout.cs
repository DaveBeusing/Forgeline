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

public readonly record struct GameplayHudLayout(
    int ViewportWidth,
    int ViewportHeight,
    float Scale,
    HudRect SafeArea,
    HudRect TopStatusBar,
    HudRect SelectionInspector,
    HudRect ActionDock,
    HudRect AlertStack,
    HudRect Minimap,
    HudRect SecondaryView)
{
    public HudRect PrimaryCommands
    {
        get
        {
            float gap = GameplayHudVisualStyle.ResolveSafeMargin(Scale);
            float x = SelectionInspector.Right + gap;
            float width = MathF.Min(608 * Scale, MathF.Max(0, Minimap.X - gap - x));
            return new HudRect(x, SelectionInspector.Y, width, SelectionInspector.Height);
        }
    }

    public HudRect RuntimeMetrics => SafeArea.IsEmpty ? default : new HudRect(
        SafeArea.Right - MathF.Min(142.0f * Scale, SafeArea.Width), SafeArea.Y,
        MathF.Min(142.0f * Scale, SafeArea.Width), TopStatusBar.Height);

    public float ActionRowStartOffset =>
        64.0f * Scale;

    public float ActionRowHeight =>
        16.0f * Scale;

    public float ActionBottomPadding =>
        28.0f * Scale;

    public static GameplayHudLayout Create(
        int viewportWidth,
        int viewportHeight,
        uint dpi,
        float uiScale = 1.0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);

        float normalizedUiScale =
            float.IsFinite(uiScale)
                ? Math.Clamp(
                    uiScale,
                    0.75f,
                    2.0f)
                : 1.0f;
        float scale =
            RtsUiLayout.ScaleForDpi(dpi) *
            normalizedUiScale;
        float margin =
            GameplayHudVisualStyle.ResolveSafeMargin(
                scale);
        float safeWidth =
            MathF.Max(
                0.0f,
                viewportWidth -
                margin * 2.0f);
        float safeHeight =
            MathF.Max(
                0.0f,
                viewportHeight -
                margin * 2.0f);
        var safeArea =
            new HudRect(
                margin,
                margin,
                safeWidth,
                safeHeight);

        if (safeArea.IsEmpty)
        {
            return new GameplayHudLayout(
                viewportWidth,
                viewportHeight,
                scale,
                safeArea,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        float topHeight =
            MathF.Min(
                GameplayHudVisualStyle.TopStatusBarHeight * scale,
                safeArea.Height);
        var topStatusBar =
            new HudRect(
                safeArea.X,
                safeArea.Y,
                MathF.Max(0.0f, safeArea.Width - 142.0f * scale - margin),
                topHeight);

        float minimapLimit =
            MathF.Max(
                0.0f,
                MathF.Min(
                    safeArea.Width * 0.28f,
                    safeArea.Height * 0.34f));
        float minimapSize =
            MathF.Min(
                GameplayHudVisualStyle.MinimapSize * scale,
                minimapLimit);
        var minimap =
            new HudRect(
                safeArea.Right -
                minimapSize,
                safeArea.Bottom -
                minimapSize,
                minimapSize,
                minimapSize);

        float selectionWidth =
            MathF.Min(
                GameplayHudVisualStyle.SelectionInspectorWidth * scale,
                MathF.Min(safeArea.Width * 0.30f, MathF.Max(
                    0.0f,
                    safeArea.Width -
                    minimap.Width -
                    margin)));
        float selectionHeight =
            MathF.Min(
                GameplayHudVisualStyle.SelectionInspectorHeight * scale,
                MathF.Max(
                    0.0f,
                    safeArea.Height -
                    topHeight -
                    margin));
        var selectionInspector =
            new HudRect(
                safeArea.X,
                safeArea.Bottom -
                selectionHeight,
                selectionWidth,
                selectionHeight);

        float actionWidth =
            MathF.Min(
                GameplayHudVisualStyle.ActionDockWidth * scale,
                safeArea.Width * 0.46f);
        float actionTop =
            topStatusBar.Bottom +
            margin;
        float actionBottom =
            MathF.Max(
                actionTop,
                minimap.Y -
                margin);
        float actionHeight =
            MathF.Min(
                GameplayHudVisualStyle.ActionDockHeight * scale,
                MathF.Max(
                    0.0f,
                    actionBottom -
                    actionTop));
        var actionDock =
            new HudRect(
                safeArea.Right -
                actionWidth,
                actionTop,
                actionWidth,
                actionHeight);

        float leftColumnWidth =
            MathF.Max(
                0.0f,
                actionDock.X -
                safeArea.X -
                margin);
        float alertTop =
            topStatusBar.Bottom +
            margin;
        float alertHeight =
            MathF.Min(
                GameplayHudVisualStyle.AlertStackHeight * scale,
                MathF.Max(
                    0.0f,
                    selectionInspector.Y -
                    alertTop -
                    margin -
                    MathF.Min(100 * MathF.Min(scale, 1.25f),
                        MathF.Max(0, selectionInspector.Y - alertTop - margin) * 0.5f) - margin));
        var alertStack =
            new HudRect(
                safeArea.X,
                alertTop,
                leftColumnWidth,
                alertHeight);

        float secondaryTop =
            alertStack.Bottom +
            margin;
        float secondaryBottom =
            MathF.Max(
                secondaryTop,
                selectionInspector.Y -
                margin);
        var secondaryView =
            new HudRect(
                safeArea.X,
                secondaryTop,
                leftColumnWidth,
                MathF.Max(
                    0.0f,
                    secondaryBottom -
                    secondaryTop));

        return new GameplayHudLayout(
            viewportWidth,
            viewportHeight,
            scale,
            safeArea,
            topStatusBar,
            selectionInspector,
            actionDock,
            alertStack,
            minimap,
            secondaryView);
    }
}
