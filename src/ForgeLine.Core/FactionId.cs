using System.Globalization;

namespace ForgeLine.Core;

public readonly record struct FactionId(uint Value) : IComparable<FactionId>
{
    public static FactionId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(FactionId other) => Value.CompareTo(other.Value);

    public static bool operator <(FactionId left, FactionId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(FactionId left, FactionId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(FactionId left, FactionId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(FactionId left, FactionId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(CultureInfo.InvariantCulture);
}
