using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class FormationRoutingBenchmarks
{
    private HierarchicalPathfinder _pathfinder = null!;
    private NavigationCapabilities _capabilities;
    private Vector3[] _starts = [];
    private Vector3 _destination;

    [Params(10, 50, 100)]
    public int UnitCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int chunksPerSide = 8;
        const float chunkSize = 64.0f;

        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = chunkSize,
            RegionSizeInChunks = 4,
            HeightSamplesPerSide = 3
        };

        var chunks =
            new List<TerrainChunk>(
                chunksPerSide * chunksPerSide);

        for (int z = 0; z < chunksPerSide; z++)
        {
            for (int x = 0; x < chunksPerSide; x++)
            {
                chunks.Add(
                    new TerrainChunk(
                        new ChunkCoordinate(x, z),
                        new TerrainHeightfield(
                            settings.HeightSamplesPerSide,
                            settings.ChunkSizeMeters,
                            new float[
                                settings.HeightSamplesPerSide *
                                settings.HeightSamplesPerSide])));
            }
        }

        var terrain = new TerrainWorld(settings, chunks);
        NavigationWorld world = NavigationWorld.Build(
            terrain,
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 8.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        _pathfinder = new HierarchicalPathfinder(world);
        _capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        _destination = new Vector3(
            chunksPerSide * chunkSize - 12.0f,
            0.0f,
            chunksPerSide * chunkSize - 12.0f);

        _starts = new Vector3[UnitCount];
        int side = checked(
            (int)MathF.Ceiling(MathF.Sqrt(UnitCount)));

        for (int index = 0; index < UnitCount; index++)
        {
            int x = index % side;
            int z = index / side;
            _starts[index] = new Vector3(
                12.0f + x * 2.0f,
                0.0f,
                12.0f + z * 2.0f);
        }
    }

    [Benchmark(Baseline = true)]
    public int IndependentStrategicRoutes()
    {
        int waypointCount = 0;

        for (int index = 0; index < _starts.Length; index++)
        {
            NavigationSearchResult result =
                _pathfinder.FindPath(
                    _starts[index],
                    _destination,
                    _capabilities);

            waypointCount += result.Path?.Waypoints.Count ?? 0;
        }

        return waypointCount;
    }

    [Benchmark]
    public int SharedFormationRoute()
    {
        Vector3 centroid = Vector3.Zero;

        for (int index = 0; index < _starts.Length; index++)
        {
            centroid += _starts[index];
        }

        centroid /= _starts.Length;

        NavigationSearchResult result =
            _pathfinder.FindPath(
                centroid,
                _destination,
                _capabilities);

        return result.Path?.Waypoints.Count ?? 0;
    }
}
