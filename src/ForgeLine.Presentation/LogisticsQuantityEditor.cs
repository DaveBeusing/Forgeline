namespace ForgeLine.Presentation;

/// <summary>Edits new stock-policy quantities; never rewrites persisted legacy values.</summary>
internal static class LogisticsQuantityEditor
{
    internal const double Step = 50;
    internal const double Minimum = 50;

    // Zero denotes an unspecified total capacity in legacy action snapshots.
    internal static (double Minimum, double Target, double Maximum) NewManualDefaults(double capacity)
    {
        if (!double.IsFinite(capacity) || capacity < 0 || capacity > 0 && capacity < Minimum)
            return (0, 0, 0);
        if (capacity > 0 && capacity < 100) return (50, 50, 50);
        if (capacity > 0 && capacity < 150) return (50, 100, 100);
        return (50, 100, 150);
    }

    internal static (double Next, bool Allowed, string Reason) Decide(
        double current, int direction, double lower, double upper,
        string lowerReason, string upperReason)
    {
        double next = Adjust(current, direction, lower, upper);
        if (next != current) return (next, true, string.Empty);
        if (!double.IsFinite(current) || !double.IsFinite(lower) || !double.IsFinite(upper))
            return (current, false, "INVALID QUANTITY LIMIT");
        return (current, false, direction < 0 ? lowerReason : upperReason);
    }

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
