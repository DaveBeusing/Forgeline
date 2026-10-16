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
            // The command dock retains its established requested-scale band independently
            // of the inspector's compact content height.
            float legacyInspectorHeight = MathF.Min(GameplayHudVisualStyle.SelectionInspectorHeight * Scale,
                MathF.Max(0, SafeArea.Height - TopStatusBar.Height - gap));
            float y = MathF.Max(SafeArea.Bottom - legacyInspectorHeight, Minimap.Y);
            return new HudRect(x, y, width, MathF.Max(0, SafeArea.Bottom - y));
        }
    }

    public const int ContractVersion = 2;

    public float Gap => MathF.Min(GameplayHudVisualStyle.ResolveSafeMargin(Scale), 16);

    // Capacity is stable while modes change. Only drawn controls own pointer input.
    public HudRect VisibleActionDock(bool expanded) => expanded
        ? ActionDock : PlayerActionDockInteractionLayout.GetCollapsedRect(this);

    public HudRect NotificationBounds(bool expanded)
    {
        if (SafeArea.IsEmpty || ActionDock.IsEmpty) return default;
        float gap = Gap;
        float top = VisibleActionDock(expanded).Bottom + gap;
        float bottom = MathF.Max(top, Minimap.Y - gap);
        return new(ActionDock.X, top, ActionDock.Width,
            MathF.Min(GameplayHudVisualStyle.AlertStackHeight * DockContentScale, bottom - top));
    }

    public float DockContentScale => MathF.Min(Scale, ActionDock.Width / 608);

    public HudRect OnboardingBounds => SafeArea.IsEmpty ? default : new(
        SafeArea.X, SafeArea.Y,
        MathF.Max(0, TopStatusBar.X - Gap - SafeArea.X),
        MathF.Min(156 * MathF.Min(Scale, 1.5f), MathF.Max(0, SelectionInspector.Y - SafeArea.Y -
            Gap)));

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

        if (safeArea.IsEmpty || viewportWidth < 320 || viewportHeight < 240)
        {
            return new GameplayHudLayout(
                viewportWidth,
                viewportHeight,
                scale,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        margin = MathF.Min(margin, 16);
        float actionWidth = MathF.Min(GameplayHudVisualStyle.ActionDockWidth * scale, safeArea.Width * .46f);
        float topWidth = MathF.Min(2 * actionWidth, safeArea.Width * .70f);
        bool twoRows = topWidth < 930 * scale;
        float stripScale = MathF.Min(scale, topWidth / (twoRows ? 640 : 930));
        float topHeight = MathF.Min((twoRows ? 80 : 48) * stripScale, safeArea.Height);
        var topStatusBar = new HudRect(safeArea.Right - topWidth, safeArea.Y, topWidth, topHeight);

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
                GameplayHudVisualStyle.SelectionInspectorHeight * MathF.Min(scale,
                    selectionWidth / GameplayHudVisualStyle.SelectionInspectorWidth),
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

        float actionTop =
            topStatusBar.Bottom +
            margin;
        float actionBottom =
            MathF.Max(
                actionTop,
                minimap.Y - margin - MathF.Min(GameplayHudVisualStyle.AlertStackHeight *
                    MathF.Min(scale, actionWidth / 608), safeArea.Height * .10f) - margin);
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
        float alertTop = MathF.Max(topStatusBar.Bottom,
            safeArea.Y + MathF.Min(156 * MathF.Min(scale, 1.5f),
                MathF.Max(0, selectionInspector.Y - safeArea.Y - margin))) + margin;
        float alertHeight =
            MathF.Min(
                GameplayHudVisualStyle.AlertStackHeight * scale,
                MathF.Max(
                    0.0f,
                    selectionInspector.Y -
                    alertTop -
                    margin -
                    MathF.Min(156 * scale,
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
