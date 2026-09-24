using System.Numerics;
using ForgeLine.Core;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class SpatialGridIndexTests
{
    private static readonly SpatialGridSettings Settings = new()
    {
        World = new WorldGridSettings
        {
            ChunkSizeMeters = 256.0f,
            RegionSizeInChunks = 8,
            HeightSamplesPerSide = 65
        },
        CellSizeMeters = 16.0f
    };

    [Theory]
    [InlineData(0.0f, 0, 0, 0)]
    [InlineData(15.999f, 0, 0, 0)]
    [InlineData(16.0f, 0, 1, 1)]
    [InlineData(255.999f, 0, 15, 15)]
    [InlineData(256.0f, 1, 0, 16)]
    [InlineData(-0.001f, -1, 15, -1)]
    [InlineData(-256.0f, -1, 0, -16)]
    [InlineData(-256.001f, -2, 15, -17)]
    public void CellAddressingUsesChunkAwareFloorSemantics(
        float worldX,
        int expectedChunkX,
        int expectedLocalX,
        int expectedGlobalX)
    {
        SpatialCellAddress address = SpatialAddressing.WorldToCell(
            worldX,
            0.0f,
            Settings);

        SpatialCellCoordinate global = SpatialAddressing.ToGlobalCell(
            address,
            Settings);

        Assert.Equal(expectedChunkX, address.Chunk.X);
        Assert.Equal(expectedLocalX, address.LocalX);
        Assert.Equal(expectedGlobalX, global.X);
        Assert.Equal(
            address,
            SpatialAddressing.FromGlobalCell(global, Settings));
    }

    [Fact]
    public void InsertUpdateAndRemoveMaintainCellOccupancy()
    {
        var index = new SpatialGridIndex(Settings);
        var buffer = new SpatialQueryBuffer();
        var entity = new EntityId(7, 1);

        index.Insert(CreateEntry(entity, new Vector3(8.0f, 1.0f, 8.0f)));

        Assert.Equal(1, index.Count);
        Assert.Equal(
            1,
            index.QueryCell(
                SpatialAddressing.WorldToCell(8.0f, 8.0f, Settings),
                buffer));
        Assert.Equal(entity, buffer.Results[0]);

        Assert.True(index.Update(
            CreateEntry(entity, new Vector3(40.0f, 1.0f, 8.0f))));

        Assert.Equal(
            0,
            index.QueryCell(
                SpatialAddressing.WorldToCell(8.0f, 8.0f, Settings),
                buffer));
        Assert.Equal(
            1,
            index.QueryCell(
                SpatialAddressing.WorldToCell(40.0f, 8.0f, Settings),
                buffer));

        Assert.True(index.Remove(entity));
        Assert.False(index.Remove(entity));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.OccupiedCellCount);
    }

    [Fact]
    public void AreaRadiusAndNearestQueriesRespectFiltersAndStableOrdering()
    {
        var index = new SpatialGridIndex(Settings);
        var buffer = new SpatialQueryBuffer();
        var first = new EntityId(8, 1);
        var second = new EntityId(3, 1);
        var excluded = new EntityId(5, 1);

        index.Insert(CreateEntry(
            first,
            new Vector3(4.0f, 0.0f, 4.0f),
            faction: 1,
            categoryMask: 0b001));
        index.Insert(CreateEntry(
            second,
            new Vector3(6.0f, 0.0f, 4.0f),
            faction: 1,
            categoryMask: 0b011));
        index.Insert(CreateEntry(
            excluded,
            new Vector3(5.0f, 0.0f, 4.0f),
            faction: 2,
            categoryMask: 0b001));

        var filter = new SpatialQueryFilter(
            Faction: 1,
            AnyCategoryMask: 0b001,
            ExcludedCategoryMask: 0b100);

        int areaCount = index.QueryAabb(
            new AxisAlignedBounds(
                new Vector3(0.0f, -2.0f, 0.0f),
                new Vector3(10.0f, 2.0f, 10.0f)),
            buffer,
            filter,
            SpatialQueryOrder.StableEntityId);

        Assert.Equal(2, areaCount);
        Assert.Equal(second, buffer.Results[0]);
        Assert.Equal(first, buffer.Results[1]);

        int radiusCount = index.QueryRadius(
            new Vector3(5.0f, 0.0f, 4.0f),
            2.0f,
            buffer,
            filter,
            SpatialQueryOrder.StableEntityId);

        Assert.Equal(2, radiusCount);
        Assert.True(index.TryFindNearest(
            new Vector3(5.0f, 0.0f, 4.0f),
            10.0f,
            buffer,
            out EntityId nearest,
            out float distanceSquared,
            filter));
        Assert.Equal(second, nearest);
        Assert.Equal(0.25f, distanceSquared);
    }

    [Fact]
    public void MultiCellEntriesAreReturnedOnlyOnce()
    {
        var index = new SpatialGridIndex(Settings);
        var buffer = new SpatialQueryBuffer();
        var entity = new EntityId(11, 1);

        index.Insert(new SpatialEntry(
            entity,
            new AxisAlignedBounds(
                new Vector3(8.0f, -1.0f, 8.0f),
                new Vector3(40.0f, 1.0f, 40.0f)),
            new SpatialEntryMetadata(1, 1)));

        int count = index.QueryAabb(
            new AxisAlignedBounds(
                Vector3.Zero,
                new Vector3(64.0f, 2.0f, 64.0f)),
            buffer);

        Assert.Equal(1, count);
        Assert.Equal(entity, buffer.Results[0]);
    }

    [Fact]
    public void QueriesMatchBruteForceReferenceAcrossNegativeCoordinates()
    {
        var index = new SpatialGridIndex(Settings);
        var buffer = new SpatialQueryBuffer();
        var entries = new List<SpatialEntry>();
        var random = new Random(12345);

        for (int item = 0; item < 512; item++)
        {
            float x = (float)(random.NextDouble() * 900.0 - 450.0);
            float z = (float)(random.NextDouble() * 900.0 - 450.0);
            var entity = new EntityId((uint)item, 1);
            SpatialEntry entry = CreateEntry(
                entity,
                new Vector3(x, 0.0f, z),
                faction: (ulong)(item % 3 + 1),
                categoryMask: 1UL << (item % 4));

            entries.Add(entry);
            index.Insert(entry);
        }

        for (int query = 0; query < 40; query++)
        {
            float x = (float)(random.NextDouble() * 700.0 - 350.0);
            float z = (float)(random.NextDouble() * 700.0 - 350.0);
            float radius = (float)(random.NextDouble() * 90.0 + 5.0);
            var center = new Vector3(x, 0.0f, z);

            index.QueryRadius(
                center,
                radius,
                buffer,
                order: SpatialQueryOrder.StableEntityId);

            EntityId[] expected = entries
                .Where(entry =>
                    HorizontalDistanceSquared(center, entry.Bounds) <=
                    radius * radius)
                .Select(static entry => entry.Entity)
                .Order()
                .ToArray();

            Assert.Equal(expected, buffer.Results.ToArray());
        }
    }

    [Fact]
    public void TenThousandMovingEntriesRemainQueryable()
    {
        var index = new SpatialGridIndex(Settings);
        var buffer = new SpatialQueryBuffer(initialCapacity: 256);
        const int entityCount = 10_000;

        for (int item = 0; item < entityCount; item++)
        {
            float x = (item % 100) * 8.0f - 400.0f;
            float z = (item / 100) * 8.0f - 400.0f;
            index.Insert(CreateEntry(
                new EntityId((uint)item, 1),
                new Vector3(x, 0.0f, z)));
        }

        for (int step = 1; step <= 8; step++)
        {
            for (int item = 0; item < entityCount; item++)
            {
                float x = (item % 100) * 8.0f - 400.0f + step * 2.25f;
                float z = (item / 100) * 8.0f - 400.0f - step * 1.5f;

                Assert.True(index.Update(CreateEntry(
                    new EntityId((uint)item, 1),
                    new Vector3(x, 0.0f, z))));
            }
        }

        int count = index.QueryRadius(
            Vector3.Zero,
            80.0f,
            buffer);

        Assert.True(count > 0);
        Assert.Equal(entityCount, index.Count);
        Assert.True(index.OccupiedCellCount > 0);
    }

    [Fact]
    public void DiagnosticsAndDebugSnapshotsReportCurrentOccupancy()
    {
        var settings = Settings with { EnableQueryTiming = true };
        var index = new SpatialGridIndex(settings);
        var buffer = new SpatialQueryBuffer();
        var entity = new EntityId(21, 1);

        index.Insert(CreateEntry(
            entity,
            new Vector3(12.0f, 0.0f, -4.0f)));

        Assert.Equal(
            1,
            index.QueryRadius(
                new Vector3(12.0f, 0.0f, -4.0f),
                5.0f,
                buffer));

        SpatialIndexDiagnosticsSnapshot diagnostics =
            index.CaptureDiagnostics();
        SpatialIndexDebugSnapshot debug =
            index.CaptureDebugSnapshot();

        Assert.Equal(1, diagnostics.IndexedEntities);
        Assert.True(diagnostics.OccupiedCells > 0);
        Assert.True(diagnostics.MaximumCellOccupancy > 0);
        Assert.Equal(1, diagnostics.QueryCount);
        Assert.Equal(1, debug.IndexedEntities);
        Assert.NotEmpty(debug.Cells);
        Assert.All(
            debug.Cells,
            static cell => Assert.True(cell.Occupancy > 0));
    }

    private static SpatialEntry CreateEntry(
        EntityId entity,
        Vector3 position,
        ulong faction = 1,
        ulong categoryMask = 1)
    {
        Vector3 extents = new(0.5f, 0.5f, 0.5f);

        return new SpatialEntry(
            entity,
            new AxisAlignedBounds(
                position - extents,
                position + extents),
            new SpatialEntryMetadata(
                faction,
                categoryMask,
                SpatialMobility.Mobile));
    }

    private static float HorizontalDistanceSquared(
        Vector3 point,
        AxisAlignedBounds bounds)
    {
        float closestX = Math.Clamp(
            point.X,
            bounds.Minimum.X,
            bounds.Maximum.X);
        float closestZ = Math.Clamp(
            point.Z,
            bounds.Minimum.Z,
            bounds.Maximum.Z);
        float deltaX = point.X - closestX;
        float deltaZ = point.Z - closestZ;
        return deltaX * deltaX + deltaZ * deltaZ;
    }
}
