namespace ForgeLine.Presentation;

/// <summary>Edits new stock-policy quantities; never rewrites persisted legacy values.</summary>
internal static class LogisticsQuantityEditor
{
    internal const double Step = 50;
    internal const double Minimum = 50;

    internal static double Adjust(double current, int direction, double lower = Minimum, double upper = double.MaxValue)
    {
        if (direction == 0 || !double.IsFinite(current) || current < 0 ||
            !double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
            return current;

        // Crossing a legacy non-multiple moves to the next exact 50-unit boundary.
        double candidate = direction > 0
            ? (Math.Floor(current / Step) + 1) * Step
            : (Math.Ceiling(current / Step) - 1) * Step;
        candidate = Math.Max(Minimum, candidate);
        if (!double.IsFinite(candidate) || candidate < lower || candidate > upper ||
            direction > 0 && candidate <= current || direction < 0 && candidate >= current)
            return current;

        return candidate;
    }
}
