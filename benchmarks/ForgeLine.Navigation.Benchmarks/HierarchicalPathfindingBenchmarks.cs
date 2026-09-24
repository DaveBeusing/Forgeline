using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.World;

namespace ForgeLine.Navigation.Benchmarks;

[MemoryDiagnoser]
public class HierarchicalPathfindingBenchmarks
{
    private HierarchicalPathfinder _pathfinder = null!;
    private NavigationCapabilities _capabilities;
    private Vector3 _start;
    private Vector3 _destination;

    [GlobalSetup]
    public void Setup()
    {
        const int chunksPerSide = 16;
        const float chunkSize = 128.0f;

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
                SectorSizeCells = 8
            });

        _pathfinder = new HierarchicalPathfinder(world);
        _capabilities =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        _start = new Vector3(4.0f, 0.0f, 4.0f);
        _destination = new Vector3(
            chunksPerSide * chunkSize - 4.0f,
            0.0f,
            chunksPerSide * chunkSize - 4.0f);
    }

    [Benchmark]
    public NavigationSearchResult FindLongDistanceRoute()
    {
        return _pathfinder.FindPath(
            _start,
            _destination,
            _capabilities);
    }
}
