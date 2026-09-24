namespace ForgeLine.World;

public sealed record SpatialGridSettings
{
    public const float DefaultCellSizeMeters = 16.0f;

    public WorldGridSettings World { get; init; } = new();

    public float CellSizeMeters { get; init; } = DefaultCellSizeMeters;

    public bool EnableQueryTiming { get; init; }

    public int CellsPerChunk
    {
        get
        {
            Validate();
            return checked((int)MathF.Round(World.ChunkSizeMeters / CellSizeMeters));
        }
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(World);
        World.Validate();

        if (!float.IsFinite(CellSizeMeters) || CellSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(CellSizeMeters));
        }

        float cellsPerChunk = World.ChunkSizeMeters / CellSizeMeters;
        float roundedCellsPerChunk = MathF.Round(cellsPerChunk);

        if (roundedCellsPerChunk < 1.0f ||
            MathF.Abs(cellsPerChunk - roundedCellsPerChunk) > 0.0001f)
        {
            throw new ArgumentException(
                "Spatial cell size must divide the world chunk size into an integral number of cells.",
                nameof(CellSizeMeters));
        }
    }
}
