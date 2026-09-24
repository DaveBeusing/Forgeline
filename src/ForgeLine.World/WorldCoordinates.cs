using System.Numerics;

namespace ForgeLine.World;

public sealed record WorldGridSettings
{
    public const float DefaultChunkSizeMeters = 256.0f;
    public const int DefaultRegionSizeInChunks = 8;
    public const int DefaultHeightSamplesPerSide = 65;

    public float ChunkSizeMeters { get; init; } = DefaultChunkSizeMeters;

    public int RegionSizeInChunks { get; init; } = DefaultRegionSizeInChunks;

    public int HeightSamplesPerSide { get; init; } = DefaultHeightSamplesPerSide;

    public float HeightSampleSpacing =>
        ChunkSizeMeters / (HeightSamplesPerSide - 1);

    public void Validate()
    {
        if (!float.IsFinite(ChunkSizeMeters) || ChunkSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(ChunkSizeMeters));
        }

        if (RegionSizeInChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RegionSizeInChunks));
        }

        if (HeightSamplesPerSide < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(HeightSamplesPerSide));
        }
    }
}

public readonly record struct ChunkCoordinate(int X, int Z)
{
    public override string ToString() => $"({X},{Z})";
}

public readonly record struct RegionCoordinate(int X, int Z)
{
    public override string ToString() => $"({X},{Z})";
}

public readonly record struct ChunkLocalPosition(float X, float Z);

public readonly record struct ChunkLocation(
    ChunkCoordinate Chunk,
    ChunkLocalPosition Local);

public readonly record struct RegionChunkLocation(
    RegionCoordinate Region,
    int LocalChunkX,
    int LocalChunkZ);

public readonly struct AxisAlignedBounds
{
    public AxisAlignedBounds(Vector3 minimum, Vector3 maximum)
    {
        if (!IsFinite(minimum) || !IsFinite(maximum))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "World bounds must contain finite values.");
        }

        if (minimum.X > maximum.X ||
            minimum.Y > maximum.Y ||
            minimum.Z > maximum.Z)
        {
            throw new ArgumentException(
                "World bounds minimum must not exceed the maximum.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public Vector3 Minimum { get; }

    public Vector3 Maximum { get; }

    public Vector3 Center => (Minimum + Maximum) * 0.5f;

    public Vector3 Extents => (Maximum - Minimum) * 0.5f;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public static class WorldCoordinateConverter
{
    public static ChunkLocation WorldToChunk(
        float worldX,
        float worldZ,
        WorldGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        RequireFinite(worldX, nameof(worldX));
        RequireFinite(worldZ, nameof(worldZ));

        float chunkSize = settings.ChunkSizeMeters;
        int chunkX = checked((int)MathF.Floor(worldX / chunkSize));
        int chunkZ = checked((int)MathF.Floor(worldZ / chunkSize));

        float localX = worldX - chunkX * chunkSize;
        float localZ = worldZ - chunkZ * chunkSize;

        localX = NormalizeLocal(localX, chunkSize);
        localZ = NormalizeLocal(localZ, chunkSize);

        return new ChunkLocation(
            new ChunkCoordinate(chunkX, chunkZ),
            new ChunkLocalPosition(localX, localZ));
    }

    public static RegionChunkLocation ChunkToRegion(
        ChunkCoordinate chunk,
        WorldGridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        int regionSize = settings.RegionSizeInChunks;
        int regionX = FloorDivide(chunk.X, regionSize);
        int regionZ = FloorDivide(chunk.Z, regionSize);

        return new RegionChunkLocation(
            new RegionCoordinate(regionX, regionZ),
            chunk.X - regionX * regionSize,
            chunk.Z - regionZ * regionSize);
    }

    public static Vector3 ChunkOrigin(
        ChunkCoordinate chunk,
        WorldGridSettings settings,
        float worldY = 0.0f)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        RequireFinite(worldY, nameof(worldY));

        return new Vector3(
            chunk.X * settings.ChunkSizeMeters,
            worldY,
            chunk.Z * settings.ChunkSizeMeters);
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

    private static float NormalizeLocal(float value, float chunkSize)
    {
        if (value < 0.0f && value > -0.0001f)
        {
            return 0.0f;
        }

        if (value >= chunkSize && value < chunkSize + 0.0001f)
        {
            return 0.0f;
        }

        return value;
    }

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
