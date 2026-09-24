namespace ForgeLine.Core;

public readonly record struct EntityId(uint Index, uint Generation) : IComparable<EntityId>
{
    public static EntityId Invalid => default;

    public bool IsValid => Generation != 0;

    public int CompareTo(EntityId other)
    {
        int indexComparison = Index.CompareTo(other.Index);
        return indexComparison != 0 ? indexComparison : Generation.CompareTo(other.Generation);
    }

    public override string ToString()
    {
        return IsValid ? $"{Index}:{Generation}" : "Invalid";
    }
}
