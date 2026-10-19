namespace ForgeLine.Presentation;

public static class GameplayGuidanceLayout
{
    public static HudRect Resolve(in GameplayHudLayout layout, bool detailed = false)
    {
        var region = Available(layout, detailed);
        if (region.Height < 38) return default;
        return region.IsEmpty ? default : new(region.X, region.Y, region.Width,
            MathF.Min(region.Height, detailed ? 500 * MathF.Min(layout.Scale, 1.25f) : 156 * layout.Scale));
    }

    internal static HudRect Remaining(in GameplayHudLayout layout, bool occupied, HudRect? occupiedBounds = null)
    {
        var region = Available(layout, occupiedBounds.HasValue && occupiedBounds.Value.Height > layout.OnboardingBounds.Height);
        if (!occupied) return region;
        var guide = occupiedBounds ?? Resolve(layout);
        float y = guide.Bottom + 4 * layout.Scale;
        return new(region.X, y, region.Width, MathF.Max(0, region.Bottom - y));
    }
    private static HudRect Available(in GameplayHudLayout layout, bool detailed = false)
    {
        var region = layout.OnboardingBounds;
        if (detailed && !region.IsEmpty)
        {
            float bottom = layout.SecondaryView.Y - layout.Gap;
            var operations = OperationsLayout.Entry(layout);
            if (!operations.IsEmpty) bottom = MathF.Min(bottom, operations.Y - layout.Gap);
            region = region with { Height = MathF.Max(0, bottom - region.Y) };
        }
        return region;
    }
}
