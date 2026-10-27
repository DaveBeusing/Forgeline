namespace ForgeLine.Presentation;

/// <summary>Edits new stock-policy quantities; never rewrites persisted legacy values.</summary>
internal static class LogisticsQuantityEditor
{
    internal const double Step = 50;
    internal const double Minimum = 50;

    // Zero denotes an unspecified total capacity in legacy action snapshots.
    internal static (double Minimum, double Target, double Maximum) NewManualDefaults(double capacity)
    {
        if (double.IsPositiveInfinity(capacity)) capacity = 0;
        if (!double.IsFinite(capacity) || capacity < 0 || capacity > 0 && capacity < Minimum)
            return (0, 0, 0);
        if (capacity > 0 && capacity < 100) return (50, 50, 50);
        if (capacity > 0 && capacity < 150) return (50, 100, 100);
        return (50, 100, 150);
    }

    internal static (double Next, bool Allowed, string Reason) Decide(
        double current, int direction, double lower, double upper,
        string lowerReason, string upperReason, bool allowZeroMinimum = false)
    {
        double next = Adjust(current, direction, lower, upper, allowZeroMinimum);
        if (next != current) return (next, true, string.Empty);
        if (!double.IsFinite(current) || !double.IsFinite(lower) || !double.IsFinite(upper))
            return (current, false, "INVALID QUANTITY LIMIT");
        return (current, false, direction < 0 ? lowerReason : upperReason);
    }

    internal static (double Next, bool Allowed, string Reason) DecideField(
        PlayerStockThresholdField field, double minimum, double target,
        double maximum, double capacity, int direction, bool allowZeroMinimum = false)
    {
        if (double.IsPositiveInfinity(capacity)) capacity = 0;
        return field switch
        {
            PlayerStockThresholdField.Minimum => Decide(
                minimum, direction, allowZeroMinimum ? 0 : Minimum, target,
                allowZeroMinimum ? "MINIMUM IS ZERO" : "MINIMUM IS 50", "INCREASE TARGET FIRST", allowZeroMinimum),
            PlayerStockThresholdField.Target => Decide(
                target, direction, Math.Max(Minimum, minimum), maximum,
                "DECREASE MIN FIRST", "INCREASE MAX FIRST"),
            PlayerStockThresholdField.Maximum => Decide(
                maximum, direction, Math.Max(Minimum, target),
                capacity > 0
                    ? direction > 0 ? capacity : Math.Max(maximum, capacity)
                    : double.MaxValue,
                "DECREASE TARGET FIRST", "INVENTORY CAPACITY REACHED"),
            _ => (0, false, "UNAVAILABLE")
        };
    }

    internal static (double Next, bool Allowed, string Reason) DecideBand(
        PlayerStockThresholdField field, double minimum, double maximum, double capacity, int direction)
    {
        if (double.IsPositiveInfinity(capacity)) capacity = 0;
        return field == PlayerStockThresholdField.Minimum
            ? Decide(minimum, direction, Minimum, maximum, "MINIMUM IS 50", "INCREASE MAX FIRST")
            : Decide(maximum, direction, Math.Max(Minimum, minimum),
                capacity > 0 ? direction > 0 ? capacity : Math.Max(maximum, capacity) : double.MaxValue,
                "DECREASE MIN FIRST", "INVENTORY CAPACITY REACHED");
    }

    internal static bool FitsCapacity(double maximum, double capacity) =>
        double.IsFinite(maximum) && maximum >= 0 && capacity >= 0 &&
        (capacity == 0 || maximum <= capacity);

    internal static double Adjust(double current, int direction, double lower = Minimum, double upper = double.MaxValue,
        bool allowZeroMinimum = false)
    {
        if (direction == 0 || !double.IsFinite(current) || current < 0 ||
            !double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
            return current;

        // Crossing a legacy non-multiple moves to the next exact 50-unit boundary.
        double candidate = direction > 0
            ? (Math.Floor(current / Step) + 1) * Step
            : (Math.Ceiling(current / Step) - 1) * Step;
        candidate = Math.Max(allowZeroMinimum ? 0 : Minimum, candidate);
        if (!double.IsFinite(candidate) || candidate < lower || candidate > upper ||
            direction > 0 && candidate <= current || direction < 0 && candidate >= current)
            return current;

        return candidate;
    }
}
