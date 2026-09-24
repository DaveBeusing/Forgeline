using System.Numerics;

namespace ForgeLine.World;

public readonly record struct SpatialCellAddress(
    ChunkCoordinate Chunk,
    int LocalX,
    int LocalZ) : IComparable<SpatialCellAddress>
{
    public int CompareTo(SpatialCellAddress other)
    {
        int zComparison = Chunk.Z.CompareTo(other.Chunk.Z);
        if (zComparison != 0)
        {
            return zComparison;
        }

        int xComparison = Chunk.X.CompareTo(other.Chunk.X);
        if (xComparison != 0)
        {
            return xComparison;
        }

        int localZComparison = LocalZ.CompareTo(other.LocalZ);
        return localZComparison != 0
            ? localZComparison
            : LocalX.CompareTo(other.LocalX);
    }

    public override string ToString() =>
        $"{Chunk}/[{LocalX},{LocalZ}]";
}

public readonly record struct SpatialCellCoordinate(int X, int Z);

public readonly record struct SpatialCellRange(
    SpatialCellCoordinate Minimum,
    SpatialCellCoordinate Maximum)
{
    public int Width => checked(Maximum.X - Minimum.X + 1);

    public int Height => checked(Maximum.Z - Minimum.Z + 1);
}

public static class SpatialAddressing
{
    public static SpatialCellAddress WorldToCell(
        Vector3 position,
        SpatialGridSettings settings)
    {
        return WorldToCell(position.X, position.Z, settings);
    }

    public static SpatialCellAddress WorldToCell(
        float worldX,
        float worldZ,
        SpatialGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        ChunkLocation chunkLocation = WorldCoordinateConverter.WorldToChunk(
            worldX,
            worldZ,
            settings.World);

        int localX = checked((int)MathF.Floor(
            chunkLocation.Local.X / settings.CellSizeMeters));
        int localZ = checked((int)MathF.Floor(
            chunkLocation.Local.Z / settings.CellSizeMeters));

        int cellsPerChunk = settings.CellsPerChunk;
        localX = Math.Clamp(localX, 0, cellsPerChunk - 1);
        localZ = Math.Clamp(localZ, 0, cellsPerChunk - 1);

        return new SpatialCellAddress(
            chunkLocation.Chunk,
            localX,
            localZ);
    }

    public static SpatialCellCoordinate ToGlobalCell(
        SpatialCellAddress address,
        SpatialGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        ValidateLocalAddress(address, settings.CellsPerChunk);

        int cellsPerChunk = settings.CellsPerChunk;

        return new SpatialCellCoordinate(
            checked(address.Chunk.X * cellsPerChunk + address.LocalX),
            checked(address.Chunk.Z * cellsPerChunk + address.LocalZ));
    }

    public static SpatialCellAddress FromGlobalCell(
        SpatialCellCoordinate coordinate,
        SpatialGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        int cellsPerChunk = settings.CellsPerChunk;
        int chunkX = FloorDivide(coordinate.X, cellsPerChunk);
        int chunkZ = FloorDivide(coordinate.Z, cellsPerChunk);

        return new SpatialCellAddress(
            new ChunkCoordinate(chunkX, chunkZ),
            coordinate.X - chunkX * cellsPerChunk,
            coordinate.Z - chunkZ * cellsPerChunk);
    }

    public static SpatialCellRange BoundsToCellRange(
        AxisAlignedBounds bounds,
        SpatialGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        SpatialCellAddress minimumAddress = WorldToCell(
            bounds.Minimum.X,
            bounds.Minimum.Z,
            settings);
        SpatialCellAddress maximumAddress = WorldToCell(
            bounds.Maximum.X,
            bounds.Maximum.Z,
            settings);

        return new SpatialCellRange(
            ToGlobalCell(minimumAddress, settings),
            ToGlobalCell(maximumAddress, settings));
    }

    public static AxisAlignedBounds GetCellBounds(
        SpatialCellAddress address,
        SpatialGridSettings settings,
        float minimumY = 0.0f,
        float maximumY = 0.0f)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        if (!float.IsFinite(minimumY) ||
            !float.IsFinite(maximumY) ||
            minimumY > maximumY)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumY));
        }

        SpatialCellCoordinate global = ToGlobalCell(address, settings);
        float minimumX = global.X * settings.CellSizeMeters;
        float minimumZ = global.Z * settings.CellSizeMeters;

        return new AxisAlignedBounds(
            new Vector3(minimumX, minimumY, minimumZ),
            new Vector3(
                minimumX + settings.CellSizeMeters,
                maximumY,
                minimumZ + settings.CellSizeMeters));
    }

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder < 0)
        {
            quotient--;
        }

        return quotient;
    }

    private static void ValidateLocalAddress(
        SpatialCellAddress address,
        int cellsPerChunk)
    {
        if (address.LocalX < 0 ||
            address.LocalX >= cellsPerChunk ||
            address.LocalZ < 0 ||
            address.LocalZ >= cellsPerChunk)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "Spatial cell local coordinates must be inside the owning chunk.");
        }
    }
}
