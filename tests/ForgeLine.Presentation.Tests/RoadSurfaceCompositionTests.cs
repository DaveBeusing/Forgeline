using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RoadSurfaceCompositionTests
{
    [Fact]
    public void GravelFollowsEveryAuthoredApproachAndLeavesBarrierCenterNatural()
    {
        var map = CentralDivideBattlefield.Create();
        var profile = TerrainPresentationProfile.CreateCentralDivide();
        foreach (var edge in map.RoadEdges.Where(x => x.Key != "north.bridge" && x.Key != "south.ford"))
        {
            var a = map.RoadNodes.Single(x => x.Key == edge.SourceNodeKey).Position;
            var b = map.RoadNodes.Single(x => x.Key == edge.DestinationNodeKey).Position;
            var midpoint = (a + b) / 2;
            Vector3 side = Vector3.Normalize(new Vector3(-(b.Z - a.Z), 0, b.X - a.X));
            Assert.True(Weight(profile, midpoint, TerrainMaterialSlot.Gravel) > .85f, edge.Key);
            Assert.True(Weight(profile, midpoint + side * 20, TerrainMaterialSlot.Mud) > .4f, edge.Key);
            Assert.Equal(0, Weight(profile, midpoint + side * 60, TerrainMaterialSlot.Gravel));
        }
        Assert.Equal(0, Weight(profile, new(1536, 0, 1536), TerrainMaterialSlot.Gravel));
    }

    [Fact]
    public void RotatedRegionsKeepTheSameSmoothFeatherAndRejectInvalidAngles()
    {
        var materials = TerrainPresentationProfile.CreateCentralDivide().Materials;
        var aligned = new TerrainPresentationProfile(materials, [new(TerrainMaterialSlot.Concrete, Vector2.Zero, new(80, 8), 20)]);
        var rotated = new TerrainPresentationProfile(materials, [new(TerrainMaterialSlot.Concrete, Vector2.Zero, new(80, 8), 20, MathF.PI / 4)]);
        for (int i = 0; i <= 32; i++)
        {
            Vector3 p = new(30, 0, i);
            Vector3 q = new((p.X - p.Z) / MathF.Sqrt(2), 0, (p.X + p.Z) / MathF.Sqrt(2));
            Assert.Equal(Weight(aligned, p, TerrainMaterialSlot.Concrete), Weight(rotated, q, TerrainMaterialSlot.Concrete), 5);
        }
        Assert.Throws<ArgumentException>(() => new TerrainPresentationProfile(materials,
            [new(TerrainMaterialSlot.Concrete, Vector2.Zero, Vector2.One, 4, float.NaN)]));
    }

    private static float Weight(TerrainPresentationProfile profile, Vector3 point, TerrainMaterialSlot slot)
    {
        Span<float> weights = stackalloc float[8];
        profile.CalculateMaterialWeights(point, Vector3.UnitY, weights);
        Assert.InRange(weights.ToArray().Sum(), .99999f, 1.00001f);
        return weights[(int)slot];
    }
}
