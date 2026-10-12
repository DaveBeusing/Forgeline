using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class EnvironmentDressingTests
{
    [Fact]
    public void AuthoredGroupsAreStableAndKeepGameplayClearances()
    {
        var map = CentralDivideBattlefield.Create();
        var dressing = map.WorldObjects.Where(x => x.Key.StartsWith("dressing.", StringComparison.Ordinal)).ToArray();
        Assert.InRange(dressing.Length, 300, 450);
        Assert.Equal(map.WorldObjects.ToArray(), CentralDivideBattlefield.Create().WorldObjects.ToArray());
        Assert.Equal(map.WorldObjects.Count, map.WorldObjects.Select(x => x.Key).Distinct().Count());
        foreach (var item in dressing)
        {
            Assert.True(item.PresentationOnly);
            Assert.Contains(item.Kind, new[] { WorldPresentationKind.Prop, WorldPresentationKind.Vegetation, WorldPresentationKind.Decal });
            var p = new Vector2(item.Position.X, item.Position.Z);
            float radius = new Vector2(item.Scale.X, item.Scale.Z).Length() / 2;
            foreach (var area in map.Starts.Select(x => x.BuildArea).Concat(map.Sites.Select(x => x.BuildArea)))
                Assert.False(Intersects(area, p, radius + 12), item.Key);
            foreach (var crossing in map.Crossings)
                Assert.False(Intersects(crossing.PassageBounds, p, radius + 32), item.Key);
            foreach (var resource in map.Resources)
                Assert.True(Vector2.Distance(p, new(resource.Center.X, resource.Center.Z)) >= radius + new Vector2(resource.HalfExtents.X, resource.HalfExtents.Z).Length() + 24, item.Key);
            foreach (var road in map.RoadEdges)
            {
                Vector3 from = map.RoadNodes.Single(x => x.Key == road.SourceNodeKey).Position;
                Vector3 to = map.RoadNodes.Single(x => x.Key == road.DestinationNodeKey).Position;
                Vector2 a = new(from.X, from.Z), b = new(to.X, to.Z);
                float projection = Math.Clamp(Vector2.Dot(p - a, b - a) / Vector2.DistanceSquared(a, b), 0, 1);
                Assert.True(Vector2.Distance(p, Vector2.Lerp(a, b, projection)) >= radius + 28, item.Key);
            }
        }
    }

    [Fact]
    public void DressingDoesNotAddStrategicObstaclesOrChangeMapTopology()
    {
        var map = CentralDivideBattlefield.Create();
        Assert.Equal(3, map.StaticNavigationObstacles.Count);
        Assert.Equal(3, map.CreateNavigationObstacles().Count);
        Assert.Equal(8, map.RoadNodes.Count);
        Assert.Equal(8, map.RoadEdges.Count);
        Assert.Equal(15, map.Resources.Count);
        Assert.Equal(10, map.Sites.Count);
        Assert.Equal(2, map.Crossings.Count);
        Assert.All(map.WorldObjects.Where(x => x.Key.StartsWith("dressing.yard.", StringComparison.Ordinal)),
            x => Assert.NotEqual(WorldPresentationKind.ResourceDeposit, x.Kind));
    }

    [Fact]
    public void KitPlacementsAreBoundedPublicSceneryWithClearServiceAccess()
    {
        var map = CentralDivideBattlefield.Create();
        var kit = map.WorldObjects.Where(x => x.Key.StartsWith("kit.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(34, kit.Length);
        Assert.Equal(10, kit.Select(x => x.Visual).Distinct().Count());
        Assert.Equal(kit, CentralDivideBattlefield.Create().WorldObjects.Where(x => x.Key.StartsWith("kit.", StringComparison.Ordinal)).ToArray());
        foreach (var item in kit)
        {
            Assert.True(item.PresentationOnly);
            Assert.NotEqual(WorldPresentationKind.ResourceDeposit, item.Kind);
            Assert.Equal(0, item.Position.Y);
            var position = new Vector2(item.Position.X, item.Position.Z);
            float radius = new Vector2(item.Scale.X, item.Scale.Z).Length() / 2;
            foreach (var area in map.Starts.Select(x => x.BuildArea).Concat(map.Sites.Select(x => x.BuildArea)))
                Assert.False(Intersects(area, position, radius + 12), item.Key);
            foreach (var resource in map.Resources)
                Assert.True(Vector2.Distance(position, new(resource.Center.X, resource.Center.Z)) >= radius + new Vector2(resource.HalfExtents.X, resource.HalfExtents.Z).Length() + 24, item.Key);
            bool transition = item.Visual is WorldVisualId.PropRoadEdge or WorldVisualId.PropBridgeApron;
            if (transition) Assert.InRange(item.Scale.Y, 0, .45f);
            else
            {
                foreach (var crossing in map.Crossings)
                    Assert.False(Intersects(crossing.PassageBounds, position, radius + 32), item.Key);
                foreach (var road in map.RoadEdges)
                {
                    Vector3 from = map.RoadNodes.Single(x => x.Key == road.SourceNodeKey).Position;
                    Vector3 to = map.RoadNodes.Single(x => x.Key == road.DestinationNodeKey).Position;
                    Vector2 a = new(from.X, from.Z), b = new(to.X, to.Z);
                    float t = Math.Clamp(Vector2.Dot(position - a, b - a) / Vector2.DistanceSquared(a, b), 0, 1);
                    Assert.True(Vector2.Distance(position, Vector2.Lerp(a, b, t)) >= radius + 28, item.Key);
                }
            }
        }
        var west = Assert.Single(kit, x => x.Key == "kit.bridge-apron.west");
        var east = Assert.Single(kit, x => x.Key == "kit.bridge-apron.east");
        Assert.Equal(CentralDivideBattlefield.BarrierMinimumX, west.Position.X + west.Scale.Z * .5f);
        Assert.Equal(CentralDivideBattlefield.BarrierMaximumX, east.Position.X - east.Scale.Z * .5f);
        Assert.Equal(12, west.Scale.X);
        Assert.Equal(3, map.CreateNavigationObstacles().Count);
    }

    private static bool Intersects(AxisAlignedBounds bounds, Vector2 p, float radius) =>
        p.X + radius >= bounds.Minimum.X && p.X - radius <= bounds.Maximum.X &&
        p.Y + radius >= bounds.Minimum.Z && p.Y - radius <= bounds.Maximum.Z;
}
