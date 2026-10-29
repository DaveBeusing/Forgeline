using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public interface IChunkDecorationProvider
{
    // Only immutable public scenery. No live entities, intelligence or simulation queries.
    ChunkDecorationSnapshot Capture();
}

public sealed class ChunkDecorationProvider(ChunkDecorationSnapshot snapshot) : IChunkDecorationProvider
{
    private readonly ChunkDecorationSnapshot _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public ChunkDecorationSnapshot Capture() => _snapshot;
}

public sealed class ChunkDecorationSnapshot
{
    public const int MaximumChunks = 4096, MaximumInstances = 1_000_000, MaximumAssets = 64;
    internal readonly ChunkDecorationChunk[] Chunks;
    public int ChunkCount => Chunks.Length;
    public int InstanceCount { get; }
    public long CompactInstanceBytes => (long)InstanceCount * 28;

    public ChunkDecorationSnapshot(IEnumerable<EnvironmentScatterChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var copied = new List<ChunkDecorationChunk>();
        var coordinates = new HashSet<ChunkCoordinate>();
        var identities = new HashSet<ulong>();
        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (copied.Count == MaximumChunks || !coordinates.Add(chunk.Coordinate))
                throw new ArgumentException("Decoration chunk budget or unique ownership violated.", nameof(chunks));
            if ((long)InstanceCount + chunk.Instances.Length > MaximumInstances)
                throw new ArgumentException("Decoration instance budget exceeded.", nameof(chunks));
            ScatterSpecies[] species = chunk.Profile.Species.ToArray();
            foreach (var item in species)
                if (assets.Add(item.AssetId) && assets.Count > MaximumAssets)
                    throw new ArgumentException("Decoration asset palette exceeds 64 entries.", nameof(chunks));
            ScatterInstance[] instances = chunk.Instances.ToArray();
            Array.Sort(instances, (a, b) => a.StableId.CompareTo(b.StableId));
            foreach (var item in instances)
                if (!identities.Add(item.StableId))
                    throw new ArgumentException("Decoration stable identities must be globally unique.", nameof(chunks));
            copied.Add(new(chunk.Coordinate, species, instances));
            InstanceCount += instances.Length;
        }
        Chunks = copied.OrderBy(c => c.Coordinate.Z).ThenBy(c => c.Coordinate.X).ToArray();
        uint ordinal = 1;
        foreach (var chunk in Chunks) { chunk.FirstOrdinal = ordinal; ordinal = checked(ordinal + (uint)chunk.Instances.Length); }
    }

    public ScatterInstance[] CaptureInstances(ChunkCoordinate coordinate) =>
        Chunks.Single(c => c.Coordinate == coordinate).Instances.ToArray();
}

internal sealed class ChunkDecorationChunk
{
    internal readonly ChunkCoordinate Coordinate;
    internal readonly ScatterSpecies[] Species;
    internal readonly ScatterInstance[] Instances;
    internal readonly AxisAlignedBounds Bounds;
    internal uint FirstOrdinal;

    internal ChunkDecorationChunk(ChunkCoordinate coordinate, ScatterSpecies[] species, ScatterInstance[] instances)
    {
        Coordinate = coordinate; Species = species; Instances = instances;
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach (var item in instances)
        {
            ScatterTransform transform = Transform(item);
            // Sphere around the supplied pivot encloses every yaw, local anchor and wind sweep.
            float radius = transform.SizeMeters.Length() * (1 + VegetationVisualMotion.MaximumAngle);
            minimum = Vector3.Min(minimum, transform.PositionMeters - new Vector3(radius));
            maximum = Vector3.Max(maximum, transform.PositionMeters + new Vector3(radius));
        }
        Bounds = instances.Length == 0 ? default : new(minimum, maximum);
    }

    internal ScatterTransform Transform(in ScatterInstance item)
    {
        var s = Species[item.SpeciesIndex];
        return new(new((float)(((long)Coordinate.X * 256_000 + item.LocalXMillimeters) / 1000.0), item.HeightMillimeters / 1000f,
                (float)(((long)Coordinate.Z * 256_000 + item.LocalZMillimeters) / 1000.0)),
            new Vector3(s.WidthMillimeters, s.HeightMillimeters, s.DepthMillimeters) * (item.ScalePermille / 1_000_000f),
            item.YawCentidegrees / 100f, item.TintUnorm8 / 255f);
    }
}

public readonly record struct DecorationRenderMetadata(ulong StableId, string? MeshAssetId, float PivotHeight, float Tint)
{
    public bool IsSpecified => MeshAssetId is not null;
}
