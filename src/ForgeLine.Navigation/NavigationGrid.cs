using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public sealed record NavigationGridSettings
{
    public const float DefaultCellSizeMeters = 8.0f;

    public float CellSizeMeters { get; init; } =
        DefaultCellSizeMeters;

    public float StaticObstacleClearanceMeters { get; init; } = 0.5f;

    public void Validate(WorldGridSettings worldSettings)
    {
        ArgumentNullException.ThrowIfNull(worldSettings);
        worldSettings.Validate();

        if (!float.IsFinite(CellSizeMeters) ||
            CellSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(CellSizeMeters));
        }

        if (!float.IsFinite(StaticObstacleClearanceMeters) ||
            StaticObstacleClearanceMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StaticObstacleClearanceMeters));
        }

        float cellsPerChunk =
            worldSettings.ChunkSizeMeters / CellSizeMeters;
        float rounded = MathF.Round(cellsPerChunk);

        if (rounded < 1.0f ||
            MathF.Abs(cellsPerChunk - rounded) > 0.0001f)
        {
            throw new ArgumentException(
                "Navigation cell size must divide the world chunk size exactly.",
                nameof(CellSizeMeters));
        }
    }
}

public readonly record struct NavigationCellCoordinate(int X, int Z)
{
    public override string ToString() => $"({X},{Z})";
}

public readonly record struct NavigationCellSample(
    float Height,
    float SlopeDegrees,
    bool StaticBlocked,
    bool HasTerrain);

public sealed class NavigationGrid
{
    private readonly NavigationCellSample[] _cells;

    internal NavigationGrid(
        Vector2 origin,
        int width,
        int height,
        NavigationGridSettings settings,
        NavigationCellSample[] cells)
    {
        Origin = origin;
        Width = width;
        Height = height;
        Settings = settings;
        _cells = cells;
    }

    public Vector2 Origin { get; }

    public int Width { get; }

    public int Height { get; }

    public int CellCount => _cells.Length;

    public NavigationGridSettings Settings { get; }

    public bool Contains(NavigationCellCoordinate coordinate) =>
        (uint)coordinate.X < (uint)Width &&
        (uint)coordinate.Z < (uint)Height;

    public NavigationCellSample GetCell(
        NavigationCellCoordinate coordinate)
    {
        return _cells[GetIndex(coordinate)];
    }

    public bool IsTraversable(
        NavigationCellCoordinate coordinate,
        in NavigationCapabilities capabilities)
    {
        if (!Contains(coordinate))
        {
            return false;
        }

        NavigationCellSample cell =
            _cells[coordinate.Z * Width + coordinate.X];

        return cell.HasTerrain &&
               !cell.StaticBlocked &&
               cell.SlopeDegrees <= capabilities.MaximumSlopeDegrees;
    }

    public float GetTraversalCost(
        NavigationCellCoordinate coordinate,
        in NavigationCapabilities capabilities)
    {
        NavigationCellSample cell = GetCell(coordinate);

        if (!cell.HasTerrain ||
            cell.StaticBlocked ||
            cell.SlopeDegrees > capabilities.MaximumSlopeDegrees)
        {
            return float.PositiveInfinity;
        }

        float normalizedSlope =
            capabilities.MaximumSlopeDegrees <= 0.0f
                ? 0.0f
                : cell.SlopeDegrees / capabilities.MaximumSlopeDegrees;

        return 1.0f +
               normalizedSlope * capabilities.SlopeCostWeight;
    }

    public bool TryWorldToCell(
        Vector3 worldPosition,
        out NavigationCellCoordinate coordinate)
    {
        if (!IsFinite(worldPosition))
        {
            coordinate = default;
            return false;
        }

        float localX = worldPosition.X - Origin.X;
        float localZ = worldPosition.Z - Origin.Y;
        float maximumX = Width * Settings.CellSizeMeters;
        float maximumZ = Height * Settings.CellSizeMeters;

        if (localX < 0.0f ||
            localZ < 0.0f ||
            localX > maximumX ||
            localZ > maximumZ)
        {
            coordinate = default;
            return false;
        }

        int x = Math.Min(
            (int)MathF.Floor(localX / Settings.CellSizeMeters),
            Width - 1);
        int z = Math.Min(
            (int)MathF.Floor(localZ / Settings.CellSizeMeters),
            Height - 1);

        coordinate = new NavigationCellCoordinate(x, z);
        return true;
    }

    public Vector3 GetCellCenter(
        NavigationCellCoordinate coordinate)
    {
        NavigationCellSample cell = GetCell(coordinate);
        float half = Settings.CellSizeMeters * 0.5f;

        return new Vector3(
            Origin.X + coordinate.X * Settings.CellSizeMeters + half,
            cell.Height,
            Origin.Y + coordinate.Z * Settings.CellSizeMeters + half);
    }

    public AxisAlignedBounds GetCellBounds(
        NavigationCellCoordinate coordinate,
        float verticalHalfExtent = 0.05f)
    {
        if (!float.IsFinite(verticalHalfExtent) ||
            verticalHalfExtent < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(verticalHalfExtent));
        }

        Vector3 center = GetCellCenter(coordinate);
        float half = Settings.CellSizeMeters * 0.5f;

        return new AxisAlignedBounds(
            new Vector3(
                center.X - half,
                center.Y - verticalHalfExtent,
                center.Z - half),
            new Vector3(
                center.X + half,
                center.Y + verticalHalfExtent,
                center.Z + half));
    }

    public int GetIndex(NavigationCellCoordinate coordinate)
    {
        if (!Contains(coordinate))
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        }

        return coordinate.Z * Width + coordinate.X;
    }

    public NavigationCellCoordinate GetCoordinate(int index)
    {
        if ((uint)index >= (uint)_cells.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new NavigationCellCoordinate(
            index % Width,
            index / Width);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public static class NavigationGridBuilder
{
    public static NavigationGrid Build(
        TerrainWorld terrain,
        IEnumerable<AxisAlignedBounds>? staticObstacles = null,
        NavigationGridSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        NavigationGridSettings resolvedSettings =
            settings ?? new NavigationGridSettings();
        resolvedSettings.Validate(terrain.Settings);

        AxisAlignedBounds worldBounds = terrain.WorldBounds;
        float widthMeters =
            worldBounds.Maximum.X - worldBounds.Minimum.X;
        float heightMeters =
            worldBounds.Maximum.Z - worldBounds.Minimum.Z;

        int width = checked((int)MathF.Round(
            widthMeters / resolvedSettings.CellSizeMeters));
        int height = checked((int)MathF.Round(
            heightMeters / resolvedSettings.CellSizeMeters));

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException(
                "Terrain bounds do not contain a navigable horizontal area.",
                nameof(terrain));
        }

        AxisAlignedBounds[] obstacles =
            staticObstacles?.ToArray() ?? [];
        ValidateObstacles(obstacles);

        var cells =
            new NavigationCellSample[checked(width * height)];
        float halfCell =
            resolvedSettings.CellSizeMeters * 0.5f;

        for (int z = 0; z < height; z++)
        {
            float worldZ =
                worldBounds.Minimum.Z +
                z * resolvedSettings.CellSizeMeters +
                halfCell;

            for (int x = 0; x < width; x++)
            {
                float worldX =
                    worldBounds.Minimum.X +
                    x * resolvedSettings.CellSizeMeters +
                    halfCell;

                bool hasHeight =
                    terrain.TrySampleHeight(
                        worldX,
                        worldZ,
                        out float sampledHeight);
                bool hasNormal =
                    terrain.TrySampleNormal(
                        worldX,
                        worldZ,
                        out Vector3 normal);

                float slopeDegrees =
                    hasNormal
                        ? MathF.Acos(
                            Math.Clamp(normal.Y, -1.0f, 1.0f)) *
                          (180.0f / MathF.PI)
                        : 90.0f;

                bool blocked =
                    IntersectsStaticObstacle(
                        worldX,
                        worldZ,
                        halfCell,
                        resolvedSettings.StaticObstacleClearanceMeters,
                        obstacles);

                cells[z * width + x] =
                    new NavigationCellSample(
                        hasHeight ? sampledHeight : 0.0f,
                        slopeDegrees,
                        blocked,
                        hasHeight && hasNormal);
            }
        }

        return new NavigationGrid(
            new Vector2(
                worldBounds.Minimum.X,
                worldBounds.Minimum.Z),
            width,
            height,
            resolvedSettings,
            cells);
    }

    private static bool IntersectsStaticObstacle(
        float centerX,
        float centerZ,
        float halfCell,
        float clearance,
        ReadOnlySpan<AxisAlignedBounds> obstacles)
    {
        float cellMinimumX = centerX - halfCell;
        float cellMaximumX = centerX + halfCell;
        float cellMinimumZ = centerZ - halfCell;
        float cellMaximumZ = centerZ + halfCell;

        for (int index = 0; index < obstacles.Length; index++)
        {
            AxisAlignedBounds obstacle = obstacles[index];

            if (cellMinimumX <= obstacle.Maximum.X + clearance &&
                cellMaximumX >= obstacle.Minimum.X - clearance &&
                cellMinimumZ <= obstacle.Maximum.Z + clearance &&
                cellMaximumZ >= obstacle.Minimum.Z - clearance)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateObstacles(
        ReadOnlySpan<AxisAlignedBounds> obstacles)
    {
        for (int index = 0; index < obstacles.Length; index++)
        {
            AxisAlignedBounds obstacle = obstacles[index];

            if (!float.IsFinite(obstacle.Minimum.X) ||
                !float.IsFinite(obstacle.Minimum.Z) ||
                !float.IsFinite(obstacle.Maximum.X) ||
                !float.IsFinite(obstacle.Maximum.Z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(obstacles),
                    "Static obstacle bounds must be finite.");
            }
        }
    }
}
