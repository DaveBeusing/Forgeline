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
    public void SelectedCornerMarkersRespectOrientedFootprintsAndSlopedTerrain(
        ControllableEntityCategory category, float width, float depth)
    {
        RenderInstance instance = Instance(category, new(width, 10f, depth));
        var draw = new DebugDraw { Enabled = true };
        RtsWorldMarkerVisualization.DrawSelected(draw, instance, Vector4.One, new SlopedTerrain());
        Assert.Equal(8, draw.Lines.Length);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 expected = TacticalFootprint.Corner(instance, corner, terrain: new SlopedTerrain());
            Assert.Equal(expected, draw.Lines[corner * 2].Start);
            Assert.Equal(expected, draw.Lines[corner * 2 + 1].Start);
        }
        foreach (DebugLine line in draw.Lines)
        {
            foreach (Vector3 point in new[] { line.Start, line.End })
            {
                Assert.InRange(point.X, -width * .55f - .01f, width * .55f + .01f);
                Assert.InRange(point.Z, -depth * .55f - .01f, depth * .55f + .01f);
                Assert.InRange(point.Y, point.X * .1f + point.Z * .2f + .079f,
                    point.X * .1f + point.Z * .2f + .081f);
            }
        }
    }

    [Fact]
    public void HiddenObjectsEmitNoMarkerAndHoverUsesFourDiagonalNotches()
    {
        var draw = new DebugDraw { Enabled = true };
        RenderInstance instance = Instance(ControllableEntityCategory.Unit, new(8f));
        RtsWorldMarkerVisualization.DrawHover(draw, instance, Vector4.One);
        Assert.Equal(4, draw.Lines.Length);
        draw.Clear();
        RtsWorldMarkerVisualization.DrawSelected(draw, instance with { Visibility = RenderVisibilityMask.None }, Vector4.One);
        Assert.Empty(draw.Lines.ToArray());
    }

    [Fact]
    public void ForeignHoverUsesExtraOutwardTicksWithoutAnEllipse()
    {
        var draw = new DebugDraw { Enabled = true };
        RenderInstance instance = Instance(ControllableEntityCategory.Unit, new(1f));
        RtsWorldMarkerVisualization.DrawHover(draw, instance, Vector4.One, minimumRadius: 8f, foreignOwned: true);
        Assert.Equal(8, draw.Lines.Length);
        Assert.All(draw.Lines.ToArray(), line => Assert.True(Vector3.Distance(line.Start, line.End) > 0));
        for (int corner = 0; corner < 4; corner++)
        {
            Assert.Equal(draw.Lines[corner * 2].Start, draw.Lines[corner * 2 + 1].Start);
            Assert.True(new Vector2(draw.Lines[corner * 2 + 1].End.X,
                draw.Lines[corner * 2 + 1].End.Z).LengthSquared() >
                new Vector2(draw.Lines[corner * 2 + 1].Start.X,
                    draw.Lines[corner * 2 + 1].Start.Z).LengthSquared());
        }
    }

    [Fact]
    public void ThousandSelectedObjectsFitExistingSingleBatchLineBudget()
    {
        var draw = new DebugDraw { Enabled = true };
        for (int index = 0; index < 1000; index++)
        {
            RtsWorldMarkerVisualization.DrawSelected(draw, Instance(ControllableEntityCategory.Unit, new(8f)), Vector4.One);
        }
        Assert.Equal(8_000, draw.Lines.Length);
        Assert.True(draw.Lines.Length < 32_768);
    }

    [Fact]
    public void LongRotatedBuildingRetainsItsNarrowAxisAndCornerOrientation()
    {
        var instance = Instance(ControllableEntityCategory.Building, new(12,10,80));
        instance = instance with { Transform = instance.Transform with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) } };
        var draw = new DebugDraw { Enabled = true };
        RtsWorldMarkerVisualization.DrawSelected(draw, instance, Vector4.One);
        Assert.All(draw.Lines.ToArray(), line => Assert.InRange(MathF.Abs(line.Start.Z), 0, 6.601f));
        Assert.Contains(draw.Lines.ToArray(), line => MathF.Abs(line.Start.X) > 43.9f);
        var a = TacticalFootprint.Corner(instance,0);
        var b = TacticalFootprint.Corner(instance,1);
        Assert.InRange(Vector3.Distance(a,b),13.199f,13.201f);
        var slope = TacticalFootprint.Corner(instance,0,terrain:new SlopedTerrain());
        Assert.InRange(slope.Y,slope.X*.1f+slope.Z*.2f+.079f,slope.X*.1f+slope.Z*.2f+.081f);
    }

    [Fact]
    public void TechnicalRangeCirclesAndTargetFeedbackRemainAvailable()
    {
        var draw = new DebugDraw { Enabled = true };
        draw.Circle(new Vector3(2, 0, 3), 10, Vector4.One, 24);
        Assert.Equal(24, draw.Lines.Length);
        draw.Clear();
        RtsWorldMarkerVisualization.DrawTarget(draw, Vector3.Zero, true, Vector4.One, Vector4.One);
        Assert.Equal(22, draw.Lines.Length);
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
