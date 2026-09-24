namespace ForgeLine.World;

public readonly record struct SpatialIndexDiagnosticsSnapshot(
    int IndexedEntities,
    int OccupiedCells,
    int MaximumCellOccupancy,
    double AverageCellOccupancy,
    long QueryCount,
    TimeSpan TotalQueryDuration,
    TimeSpan AverageQueryDuration,
    TimeSpan MaximumQueryDuration);
