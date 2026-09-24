namespace ForgeLine.Navigation;

public readonly record struct NavigationVersion(ulong Value)
    : IComparable<NavigationVersion>
{
    public static NavigationVersion Initial { get; } = new(1);

    public bool IsValid => Value != 0;

    public int CompareTo(NavigationVersion other) =>
        Value.CompareTo(other.Value);

    public static bool operator <(
        NavigationVersion left,
        NavigationVersion right) =>
        left.Value < right.Value;

    public static bool operator >(
        NavigationVersion left,
        NavigationVersion right) =>
        left.Value > right.Value;

    public static bool operator <=(
        NavigationVersion left,
        NavigationVersion right) =>
        left.Value <= right.Value;

    public static bool operator >=(
        NavigationVersion left,
        NavigationVersion right) =>
        left.Value >= right.Value;
}

public sealed class NavigationVersionTracker
{
    private long _value = 1;

    public NavigationVersion Current =>
        new(unchecked((ulong)Volatile.Read(ref _value)));

    public NavigationVersion Invalidate()
    {
        long value = Interlocked.Increment(ref _value);
        if (value <= 0)
        {
            throw new OverflowException(
                "Navigation version space has been exhausted.");
        }

        return new NavigationVersion(unchecked((ulong)value));
    }
}
