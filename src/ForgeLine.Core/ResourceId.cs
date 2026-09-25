using System.Globalization;

namespace ForgeLine.Core;

public readonly record struct ResourceId(uint Value) : IComparable<ResourceId>
{
    public static ResourceId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(ResourceId other) => Value.CompareTo(other.Value);

    public static bool operator <(ResourceId left, ResourceId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(ResourceId left, ResourceId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(ResourceId left, ResourceId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(ResourceId left, ResourceId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(CultureInfo.InvariantCulture);
}

public static class ResourceIds
{
    public static ResourceId FerrousOre => new(1);

    public static ResourceId Volatiles => new(2);

    public static ResourceId Silicates => new(3);

    public static ResourceId Steel => new(4);

    public static ResourceId Fuel => new(5);

    public static ResourceId Electronics => new(6);

    public static ResourceId Ammunition => new(7);
}
