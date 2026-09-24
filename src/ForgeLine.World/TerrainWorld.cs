using System.Numerics;

namespace ForgeLine.World;

public sealed class TerrainChunk
{
    public TerrainChunk(
        ChunkCoordinate coordinate,
        TerrainHeightfield heightfield)
    {
        Coordinate = coordinate;
        Heightfield = heightfield ??
            throw new ArgumentNullException(nameof(heightfield));

        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = heightfield.ChunkSizeMeters,
            HeightSamplesPerSide = heightfield.SamplesPerSide
        };

        Vector3 origin = WorldCoordinateConverter.ChunkOrigin(
            coordinate,
            settings);

        Bounds = new AxisAlignedBounds(
            new Vector3(
                origin.X,
                heightfield.MinimumHeight,
                origin.Z),
            new Vector3(
                origin.X + heightfield.ChunkSizeMeters,
                heightfield.MaximumHeight,
                origin.Z + heightfield.ChunkSizeMeters));
    }

    public ChunkCoordinate Coordinate { get; }

    public TerrainHeightfield Heightfield { get; }

    public AxisAlignedBounds Bounds { get; }
}

public interface ITerrainQuery
{
    AxisAlignedBounds WorldBounds { get; }

    bool TrySampleHeight(float worldX, float worldZ, out float height);

    bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal);
}

public sealed class TerrainWorld : ITerrainQuery
{
    private readonly Dictionary<ChunkCoordinate, TerrainChunk> _chunksByCoordinate;
    private readonly TerrainChunk[] _chunks;

    public TerrainWorld(
        WorldGridSettings settings,
        IEnumerable<TerrainChunk> chunks)
    {
        Settings = settings ??
            throw new ArgumentNullException(nameof(settings));
        Settings.Validate();
        ArgumentNullException.ThrowIfNull(chunks);

        _chunks = chunks
            .OrderBy(static chunk => chunk.Coordinate.Z)
            .ThenBy(static chunk => chunk.Coordinate.X)
            .ToArray();

        if (_chunks.Length == 0)
        {
            throw new ArgumentException(
                "A terrain world must contain at least one chunk.",
                nameof(chunks));
        }

        _chunksByCoordinate = new Dictionary<ChunkCoordinate, TerrainChunk>(
            _chunks.Length);

        foreach (TerrainChunk chunk in _chunks)
        {
            if (MathF.Abs(
                    chunk.Heightfield.ChunkSizeMeters -
                    Settings.ChunkSizeMeters) > 0.0001f)
            {
                throw new ArgumentException(
                    $"Chunk {chunk.Coordinate} uses a different chunk size.",
                    nameof(chunks));
            }

            if (!_chunksByCoordinate.TryAdd(chunk.Coordinate, chunk))
            {
                throw new ArgumentException(
                    $"Duplicate terrain chunk {chunk.Coordinate}.",
                    nameof(chunks));
            }
        }

        float minimumX = _chunks.Min(static chunk => chunk.Bounds.Minimum.X);
        float minimumY = _chunks.Min(static chunk => chunk.Bounds.Minimum.Y);
        float minimumZ = _chunks.Min(static chunk => chunk.Bounds.Minimum.Z);
        float maximumX = _chunks.Max(static chunk => chunk.Bounds.Maximum.X);
        float maximumY = _chunks.Max(static chunk => chunk.Bounds.Maximum.Y);
        float maximumZ = _chunks.Max(static chunk => chunk.Bounds.Maximum.Z);

        WorldBounds = new AxisAlignedBounds(
            new Vector3(minimumX, minimumY, minimumZ),
            new Vector3(maximumX, maximumY, maximumZ));
    }

    public WorldGridSettings Settings { get; }

    public IReadOnlyList<TerrainChunk> Chunks => _chunks;

    public AxisAlignedBounds WorldBounds { get; }

    public bool TryGetChunk(
        ChunkCoordinate coordinate,
        out TerrainChunk chunk) =>
        _chunksByCoordinate.TryGetValue(coordinate, out chunk!);

    public bool TrySampleHeight(
        float worldX,
        float worldZ,
        out float height)
    {
        ChunkLocation location = WorldCoordinateConverter.WorldToChunk(
            worldX,
            worldZ,
            Settings);

        if (!_chunksByCoordinate.TryGetValue(location.Chunk, out TerrainChunk? chunk))
        {
            height = default;
            return false;
        }

        height = chunk.Heightfield.SampleHeight(
            location.Local.X,
            location.Local.Z);
        return true;
    }

    public bool TrySampleNormal(
        float worldX,
        float worldZ,
        out Vector3 normal)
    {
        ChunkLocation location = WorldCoordinateConverter.WorldToChunk(
            worldX,
            worldZ,
            Settings);

        if (!_chunksByCoordinate.TryGetValue(location.Chunk, out TerrainChunk? chunk))
        {
            normal = default;
            return false;
        }

        normal = chunk.Heightfield.SampleNormal(
            location.Local.X,
            location.Local.Z);
        return true;
    }
}
