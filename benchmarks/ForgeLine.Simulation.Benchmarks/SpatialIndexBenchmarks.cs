using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class SpatialIndexBenchmarks
{
    private const int EntityCount = 10_000;

    private readonly SpatialEntry[] _entriesA = new SpatialEntry[EntityCount];
    private readonly SpatialEntry[] _entriesB = new SpatialEntry[EntityCount];

    private SpatialGridIndex _index = null!;
    private SpatialQueryBuffer _queryBuffer = null!;
    private bool _useAlternatePositions;

    [GlobalSetup]
    public void Setup()
    {
        var settings = new SpatialGridSettings
        {
            World = new WorldGridSettings
            {
                ChunkSizeMeters = 256.0f,
                RegionSizeInChunks = 8,
                HeightSamplesPerSide = 65
            },
            CellSizeMeters = 16.0f
        };

        _index = new SpatialGridIndex(settings);
        _queryBuffer = new SpatialQueryBuffer(initialCapacity: 512);

        for (int index = 0; index < EntityCount; index++)
        {
            float x = (index % 100) * 8.0f - 400.0f;
            float z = (index / 100) * 8.0f - 400.0f;

            _entriesA[index] = CreateEntry(
                index,
                new Vector3(x, 0.0f, z));
            _entriesB[index] = CreateEntry(
                index,
                new Vector3(x + 5.5f, 0.0f, z - 3.25f));

            _index.Insert(_entriesA[index]);
        }

        _index.QueryRadius(
            Vector3.Zero,
            64.0f,
            _queryBuffer);
    }

    [Benchmark]
    public int RadiusQuery64Meters()
    {
        return _index.QueryRadius(
            Vector3.Zero,
            64.0f,
            _queryBuffer);
    }

    [Benchmark]
    public int RadiusQuery160Meters()
    {
        return _index.QueryRadius(
            Vector3.Zero,
            160.0f,
            _queryBuffer);
    }

    [Benchmark]
    public int AabbQuery256Meters()
    {
        return _index.QueryAabb(
            new AxisAlignedBounds(
                new Vector3(-128.0f, -10.0f, -128.0f),
                new Vector3(128.0f, 10.0f, 128.0f)),
            _queryBuffer);
    }

    [Benchmark]
    public int StableFilteredRadiusQuery()
    {
        return _index.QueryRadius(
            Vector3.Zero,
            96.0f,
            _queryBuffer,
            new SpatialQueryFilter(
                Faction: 1,
                AnyCategoryMask: 0b001),
            SpatialQueryOrder.StableEntityId);
    }

    [Benchmark]
    public int UpdateTenThousandEntries()
    {
        SpatialEntry[] entries = _useAlternatePositions
            ? _entriesA
            : _entriesB;

        for (int index = 0; index < entries.Length; index++)
        {
            _index.Update(entries[index]);
        }

        _useAlternatePositions = !_useAlternatePositions;
        return _index.Count;
    }

    private static SpatialEntry CreateEntry(
        int index,
        Vector3 position)
    {
        Vector3 halfExtents = new(1.5f, 1.0f, 2.0f);

        return new SpatialEntry(
            new EntityId((uint)index, 1),
            new AxisAlignedBounds(
                position - halfExtents,
                position + halfExtents),
            new SpatialEntryMetadata(
                (ulong)(index % 4 + 1),
                1UL << (index % 4),
                SpatialMobility.Mobile));
    }
}
