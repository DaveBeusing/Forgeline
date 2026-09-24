namespace ForgeLine.World;

public readonly record struct SpatialDebugCell(
    SpatialCellAddress Address,
    AxisAlignedBounds Bounds,
    int Occupancy);

public sealed class SpatialIndexDebugSnapshot
{
    private readonly SpatialDebugCell[] _cells;

    internal SpatialIndexDebugSnapshot(
        int indexedEntities,
        SpatialDebugCell[] cells)
    {
        IndexedEntities = indexedEntities;
        _cells = cells ??
            throw new ArgumentNullException(nameof(cells));
    }

    public int IndexedEntities { get; }

    public IReadOnlyList<SpatialDebugCell> Cells => _cells;
}
