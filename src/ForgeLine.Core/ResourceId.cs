using System.Globalization;

namespace ForgeLine.Core;

public readonly record struct ResourceId(uint Value) : IComparable<ResourceId>
{
    public static ResourceId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(ResourceId other) => Value.CompareTo(other.Value);

    public override string ToString() =>
        Value.ToString(CultureInfo.InvariantCulture);
}

public static class ResourceIds
{
    public static ResourceId FerrousOre => new(1);

    public static ResourceId Volatiles => new(2);

    public static ResourceId Silicates => new(3);
}
