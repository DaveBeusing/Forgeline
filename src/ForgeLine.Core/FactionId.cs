using System.Globalization;

namespace ForgeLine.Core;

public readonly record struct FactionId(uint Value) : IComparable<FactionId>
{
    public static FactionId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(FactionId other) => Value.CompareTo(other.Value);

    public override string ToString() =>
        Value.ToString(CultureInfo.InvariantCulture);
}
