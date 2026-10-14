namespace ForgeLine.Presentation;

/// <summary>Local content density within the existing interaction regions.</summary>
internal readonly record struct ResourceStripLayout(HudRect Bounds, float Scale, bool TwoRows)
{
    public static ResourceStripLayout Create(in GameplayHudLayout layout)
    {
        HudRect bounds = layout.TopStatusBar;
        bool twoRows = bounds.Width < 930 * layout.Scale && bounds.Height >= 54;
        float scale = MathF.Min(layout.Scale, MathF.Min(bounds.Width / (twoRows ? 640 : 930),
            bounds.Height / (twoRows ? 64 : 36)));
        return new(bounds, MathF.Max(0, scale), twoRows);
    }

    public HudRect Resource(int index)
    {
        if (index is < 0 or > 6 || Scale <= 0) return default;
        float powerWidth = 210 * Scale;
        int columns = TwoRows ? 4 : 7;
        float cell = MathF.Max(0, (Bounds.Width - powerWidth - 16 * Scale) / columns);
        float rowHeight = Bounds.Height / (TwoRows ? 2 : 1);
        return new(Bounds.X + 6 * Scale + index % columns * cell,
            Bounds.Y + index / columns * rowHeight, cell - 3 * Scale, rowHeight);
    }

    public HudRect Power => new(Bounds.Right - 210 * Scale, Bounds.Y, 204 * Scale, Bounds.Height);
}

internal static class HudInformationLayout
{
    public static float InspectorScale(in GameplayHudLayout layout) =>
        MathF.Min(layout.Scale, MathF.Min(layout.SelectionInspector.Width / 360, layout.SelectionInspector.Height / 112));

    public static float MinimapScale(in GameplayHudLayout layout) =>
        MathF.Min(layout.Scale, layout.Minimap.Width / GameplayHudVisualStyle.MinimapSize);

    public static HudRect Alerts(in GameplayHudLayout layout) => layout.AlertStack with
    { Width = MathF.Min(layout.AlertStack.Width, 620 * AlertScale(layout)) };

    public static float AlertScale(in GameplayHudLayout layout) =>
        MathF.Min(layout.Scale, MathF.Max(0, layout.AlertStack.Width / 440));
}
