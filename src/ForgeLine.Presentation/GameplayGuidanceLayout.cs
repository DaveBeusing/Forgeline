namespace ForgeLine.Presentation;

public static class GameplayGuidanceLayout
{
    public static HudRect Resolve(in GameplayHudLayout layout)
    {
        var region = layout.SecondaryView;
        if (region.Height < 38 * MathF.Min(layout.Scale, 1.25f)) return default;
        return region.IsEmpty ? default : new(region.X, region.Y, region.Width,
            MathF.Min(region.Height, 100 * MathF.Min(layout.Scale, 1.25f)));
    }

    internal static HudRect Remaining(in GameplayHudLayout layout, bool occupied)
    {
        if (!occupied) return layout.SecondaryView;
        var guide = Resolve(layout);
        float y = guide.Bottom + 4 * MathF.Min(layout.Scale, 1.25f);
        return new(layout.SecondaryView.X, y, layout.SecondaryView.Width, MathF.Max(0, layout.SecondaryView.Bottom - y));
    }
}
