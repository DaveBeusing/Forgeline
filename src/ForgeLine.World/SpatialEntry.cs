using ForgeLine.Core;

namespace ForgeLine.World;

public enum SpatialMobility : byte
{
    Static = 0,
    Mobile = 1
}

public readonly record struct SpatialEntryMetadata(
    ulong Faction,
    ulong CategoryMask,
    SpatialMobility Mobility = SpatialMobility.Mobile);

public readonly record struct SpatialEntry(
    EntityId Entity,
    AxisAlignedBounds Bounds,
    SpatialEntryMetadata Metadata)
{
    public System.Numerics.Vector3 Position => Bounds.Center;
}

public readonly record struct SpatialQueryFilter(
    ulong? Faction = null,
    ulong AnyCategoryMask = 0,
    ulong ExcludedCategoryMask = 0,
    SpatialMobility? Mobility = null)
{
    public bool Matches(in SpatialEntry entry)
    {
        if (Faction is ulong faction &&
            entry.Metadata.Faction != faction)
        {
            return false;
        }

        if (AnyCategoryMask != 0 &&
            (entry.Metadata.CategoryMask & AnyCategoryMask) == 0)
        {
            return false;
        }

        if (ExcludedCategoryMask != 0 &&
            (entry.Metadata.CategoryMask & ExcludedCategoryMask) != 0)
        {
            return false;
        }

        return Mobility is null ||
               entry.Metadata.Mobility == Mobility.Value;
    }
}

public enum SpatialQueryOrder : byte
{
    Unspecified = 0,
    StableEntityId = 1
}
