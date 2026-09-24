using System.Numerics;
using ForgeLine.Core;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SpatialIndexDebugVisualizationTests
{
    [Fact]
    public void OccupiedCellVisualizationProducesGeometryAndLabels()
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
        var index = new SpatialGridIndex(settings);
        var entity = new EntityId(1, 1);

        index.Insert(new SpatialEntry(
            entity,
            new AxisAlignedBounds(
                new Vector3(1.0f, -1.0f, 1.0f),
                new Vector3(2.0f, 1.0f, 2.0f)),
            new SpatialEntryMetadata(1, 1)));

        var debugDraw = new DebugDraw { Enabled = true };
        SpatialIndexDebugSnapshot snapshot =
            index.CaptureDebugSnapshot(planeY: 0.25f);

        SpatialIndexDebugVisualization.DrawOccupiedCells(
            debugDraw,
            snapshot,
            Vector4.One,
            Vector4.One,
            maximumCells: 16,
            maximumLabels: 16);
        SpatialIndexDebugVisualization.DrawRadiusQuery(
            debugDraw,
            Vector3.Zero,
            8.0f,
            Vector4.One);
        SpatialIndexDebugVisualization.DrawAabbQuery(
            debugDraw,
            new AxisAlignedBounds(
                Vector3.Zero,
                Vector3.One),
            Vector4.One);

        Assert.NotEmpty(debugDraw.Lines.ToArray());
        Assert.NotEmpty(debugDraw.Labels);
    }
}
