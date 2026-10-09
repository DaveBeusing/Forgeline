using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectionMarkerTests
{
    [Theory]
    [InlineData(ControllableEntityCategory.Unit, 8f, 12f)]
    [InlineData(ControllableEntityCategory.Building, 60f, 90f)]
    public void CircularRingEnclosesFootprintAndSamplesSlopedTerrain(
        ControllableEntityCategory category, float width, float depth)
    {
        RenderInstance instance = Instance(category, new(width, 10f, depth));
        var draw = new DebugDraw { Enabled = true };
        RtsWorldMarkerVisualization.DrawSelected(draw, instance, Vector4.One, new SlopedTerrain());
        Assert.Equal(24, draw.Lines.Length);
        float radius = new Vector2(width * 0.5f, depth * 0.5f).Length() * 1.08f;
        foreach (DebugLine line in draw.Lines)
        {
            Vector3 p = line.Start;
            Assert.InRange(new Vector2(p.X, p.Z).Length(), radius - 0.001f, radius + 0.001f);
            Assert.InRange(p.Y, p.X * 0.1f + p.Z * 0.2f + 0.079f, p.X * 0.1f + p.Z * 0.2f + 0.081f);
        }
    }

    [Fact]
    public void HiddenObjectsEmitNoMarkerAndHoverUsesBrokenRing()
    {
        var draw = new DebugDraw { Enabled = true };
        RenderInstance instance = Instance(ControllableEntityCategory.Unit, new(8f));
        RtsWorldMarkerVisualization.DrawHover(draw, instance, Vector4.One);
        Assert.Equal(12, draw.Lines.Length);
        draw.Clear();
        RtsWorldMarkerVisualization.DrawSelected(draw, instance with { Visibility = RenderVisibilityMask.None }, Vector4.One);
        Assert.Empty(draw.Lines.ToArray());
    }

    [Fact]
    public void MinimumZoomRadiusAndForeignHoverRemainDistinctWithoutColor()
    {
        var draw = new DebugDraw { Enabled = true };
        RenderInstance instance = Instance(ControllableEntityCategory.Unit, new(1f));
        RtsWorldMarkerVisualization.DrawHover(draw, instance, Vector4.One, minimumRadius: 8f, foreignOwned: true);
        Assert.Equal(16, draw.Lines.Length);
        Assert.All(draw.Lines.ToArray(), line =>
            Assert.InRange(new Vector2(line.Start.X, line.Start.Z).Length(), 7.999f, 8.001f));
    }

    [Fact]
    public void ThousandSelectedObjectsFitExistingSingleBatchLineBudget()
    {
        var draw = new DebugDraw { Enabled = true };
        for (int index = 0; index < 1000; index++)
        {
            RtsWorldMarkerVisualization.DrawSelected(draw, Instance(ControllableEntityCategory.Unit, new(8f)), Vector4.One);
        }
        Assert.Equal(24_000, draw.Lines.Length);
        Assert.True(draw.Lines.Length < 32_768);
    }

    private static RenderInstance Instance(ControllableEntityCategory category, Vector3 scale) =>
        new(new EntityId(1, 1), new RenderTransform(Vector3.Zero, Quaternion.Identity, scale),
            new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, 1,
            new SelectablePresentationMetadata(new PlayerId(1), category));

    private sealed class SlopedTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-1000f), new(1000f));
        public bool TrySampleHeight(float x, float z, out float height)
        {
            height = x * 0.1f + z * 0.2f;
            return true;
        }
        public bool TrySampleNormal(float x, float z, out Vector3 normal)
        {
            normal = Vector3.Normalize(new(-0.1f, 1f, -0.2f));
            return true;
        }
    }
}
