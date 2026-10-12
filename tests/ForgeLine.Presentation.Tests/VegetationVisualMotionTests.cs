using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VegetationVisualMotionTests
{
    [Theory]
    [InlineData(WorldVisualId.VegetationBroadleaf)] [InlineData(WorldVisualId.VegetationConifer)] [InlineData(WorldVisualId.VegetationScrub)] [InlineData(WorldVisualId.VegetationGrassClump)]
    public void GroundPivotBoundsAndPeriodicMotionPreserveCopiedState(WorldVisualId visual)
    {
        RenderInstance instance = new(new EntityId(1, 1), new(new Vector3(10, 5, 20), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f), new Vector3(2, 10, 3)),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default,
            WorldFeature: new(visual, WorldPresentationKind.Vegetation, default, false));
        RenderInstance copied = instance;
        Vector3 pivot = Vector3.Zero;
        Matrix4x4 original = instance.Transform.ToMatrix();
        var bounds = VegetationVisualMotion.CullingBounds(instance);
        bool changed = false;
        for (int sample = 0; sample < 32; sample++)
        {
            Matrix4x4 matrix = VegetationVisualMotion.Transform(instance, 100, sample * .25);
            changed |= matrix != original;
            Assert.InRange(Vector3.Distance(Vector3.Transform(pivot, original), Vector3.Transform(pivot, matrix)), 0, .00001f);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = Vector3.Transform(new Vector3((corner & 1) == 0 ? -.5f : .5f, (corner & 2) == 0 ? 0 : 1, (corner & 4) == 0 ? -.5f : .5f), matrix);
                Assert.InRange(p.X, bounds.Minimum.X, bounds.Maximum.X);
                Assert.InRange(p.Y, bounds.Minimum.Y, bounds.Maximum.Y);
                Assert.InRange(p.Z, bounds.Minimum.Z, bounds.Maximum.Z);
            }
        }
        Assert.True(changed);
        Assert.Equal(copied, instance);
        Assert.Equal(VegetationVisualMotion.Transform(instance, 100, 1), VegetationVisualMotion.Transform(instance, 100, 9));
        Assert.Equal(original, VegetationVisualMotion.Transform(instance, 18, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VegetationVisualMotion.Transform(instance, 100, double.NaN));
    }

    [Fact]
    public void OrdinaryPropsAndStrategicVegetationStayStill()
    {
        RenderInstance instance = new(default, RenderTransform.Identity, new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default,
            WorldFeature: new(WorldVisualId.PropRock, WorldPresentationKind.Prop, default, false));
        Assert.Equal(Matrix4x4.Identity, VegetationVisualMotion.Transform(instance, 100, 3));
        Assert.Equal(PresentationBounds.ResolveAxisAlignedBounds(instance).Minimum, VegetationVisualMotion.CullingBounds(instance).Minimum);
    }
}
