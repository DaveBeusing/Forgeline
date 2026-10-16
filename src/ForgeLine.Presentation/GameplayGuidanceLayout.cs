namespace ForgeLine.Presentation;

public static class GameplayGuidanceLayout
{
    public static HudRect Resolve(in GameplayHudLayout layout)
    {
        var region = Available(layout);
        if (region.Height < 38) return default;
        return region.IsEmpty ? default : new(region.X, region.Y, region.Width,
            MathF.Min(region.Height, 156 * layout.Scale));
    }

    internal static HudRect Remaining(in GameplayHudLayout layout, bool occupied)
    {
        var region = Available(layout);
        if (!occupied) return region;
        var guide = Resolve(layout);
        float y = guide.Bottom + 4 * layout.Scale;
        return new(region.X, y, region.Width, MathF.Max(0, region.Bottom - y));
    }
    private static HudRect Available(in GameplayHudLayout layout)
    {
        var region = layout.SecondaryView with
        { Height = MathF.Max(0, layout.SecondaryView.Height - OperationsLayout.EntryBand(layout)) };
        // The compact inspector can be shorter than the independently scaled
        // command band. Keep the guide in the left column when those bands overlap.
        var commands = layout.PrimaryCommands;
        if (region.Intersects(commands))
            region = region with { Width = MathF.Max(0, commands.X -
                GameplayHudVisualStyle.ResolveSafeMargin(layout.Scale) - region.X) };
        return region;
    }
}
