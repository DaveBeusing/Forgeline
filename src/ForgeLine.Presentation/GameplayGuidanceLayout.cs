namespace ForgeLine.Presentation;

public static class GameplayGuidanceLayout
{
    public static HudRect Resolve(in GameplayHudLayout layout)
    {
        var region = Available(layout);
        if (region.Height < 38 * MathF.Min(layout.Scale, 1.25f)) return default;
        return region.IsEmpty ? default : new(region.X, region.Y, region.Width,
            MathF.Min(region.Height, 100 * MathF.Min(layout.Scale, 1.25f)));
    }

    internal static HudRect Remaining(in GameplayHudLayout layout, bool occupied)
    {
        var region = Available(layout);
        if (!occupied) return region;
        var guide = Resolve(layout);
        float y = guide.Bottom + 4 * MathF.Min(layout.Scale, 1.25f);
        return new(region.X, y, region.Width, MathF.Max(0, region.Bottom - y));
    }
    private static HudRect Available(in GameplayHudLayout layout) => layout.SecondaryView with
    { Height = MathF.Max(0, layout.SecondaryView.Height - OperationsLayout.EntryBand(layout)) };
}
