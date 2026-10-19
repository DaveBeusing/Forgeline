using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class LogisticsZoneOverlayTests
{
    private static readonly EntityId Building = new(100, 1);
    private static readonly EntityId Truck = new(200, 1);
    private static readonly PlaneTerrain Terrain = new();

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void LoadingHatchFollowsTrueRotatedVehiclePose(BuildingOrientation orientation)
    {
        var building = new WorldTransform(new(30, 0, 40), BuildingFootprint.GetRotation(orientation), Vector3.One);
        var truck = new WorldTransform(Vector3.Zero, Quaternion.Identity, new(3.2f, 2.4f, 6.4f));
        Assert.True(VehicleDockPose.TryCreate(new(new(0, 0, 14), Vector3.UnitX, 18),
            building, truck, out var pose));
        Vector3 center = (pose.Entry + pose.Exit) * .5f;
        float halfLength = Vector3.Distance(pose.Entry, pose.Exit) * .5f + pose.HalfLength;
        float halfWidth = pose.HalfWidth + 1;
        var strokes = new List<LogisticsZoneStroke>();
        Assert.True(LogisticsZoneSnapshot.AddHatching(strokes, Terrain, Building, Truck, LogisticsZoneKind.Loading,
            center, pose.Heading, halfLength, halfWidth, false));
        Assert.True(strokes.Count >= 8);
        Vector3 forward = new(MathF.Sin(pose.Heading), 0, MathF.Cos(pose.Heading));
        Vector3 side = new(forward.Z, 0, -forward.X);
        foreach (var s in strokes)
        {
            foreach (Vector3 point in new[] { s.Start, s.End })
            {
                Assert.InRange(MathF.Abs(Vector3.Dot(point - center, forward)), 0, halfLength + .001f);
                Assert.InRange(MathF.Abs(Vector3.Dot(point - center, side)), 0, halfWidth + .001f);
                Assert.Equal(2.08f + .02f * (point.X + point.Z), point.Y, 3);
            }
        }
        Assert.All(strokes, s => Assert.Equal(LogisticsZoneKind.Loading, s.Kind));
    }

    [Fact]
    public void HoldingHatchUsesOppositeDiagonalAndExactNonSquareBounds()
    {
        var a = new List<LogisticsZoneStroke>();
        var b = new List<LogisticsZoneStroke>();
        Vector3 center = new(20, 0, 10);
        Assert.True(LogisticsZoneSnapshot.AddHatching(a, Terrain, Building, Truck,
            LogisticsZoneKind.Loading, center, 0, 6, 1.6f, false));
        Assert.True(LogisticsZoneSnapshot.AddHatching(b, Terrain, Building, Truck,
            LogisticsZoneKind.Holding, center, 0, 6, 1.6f, true));
        // Perimeter lines are first; the fifth stroke is the first non-degenerate hatch.
        var loadLine = a[4];
        var holdLine = b[4];
        Assert.True((loadLine.End.X - loadLine.Start.X) *
                    (loadLine.End.Z - loadLine.Start.Z) < 0);
        Assert.True((holdLine.End.X - holdLine.Start.X) *
                    (holdLine.End.Z - holdLine.Start.Z) > 0);
        Assert.All(b, s => Assert.True(s.Unavailable));
    }

    [Fact]
    public void WorldOverlayRequiresSelectionOrExplicitLogisticsModeAndRespectsBudget()
    {
        var strokes = new List<LogisticsZoneStroke>();
        Assert.True(LogisticsZoneSnapshot.AddHatching(strokes, Terrain, Building, Truck,
            LogisticsZoneKind.Loading, new(20, 0, 10), MathF.PI / 2, 6, 1.6f, false));
        var experience = default(PlayerExperienceSnapshot) with
        { Tick = new(4), Player = new(1), MatchStatus = PlayerMatchStatus.Active };
        var snapshot = new PresentationSnapshot(new SimulationTick(4), TimeSpan.FromMilliseconds(50), 2,
            ReadOnlySpan<RenderInstance>.Empty, sessionId: new(1), playerExperience: experience)
        { DockingZones = new(strokes.ToArray()) };
        var draw = new DebugDraw { Enabled = true };
        Assert.Equal(0, LogisticsZoneOverlay.Draw(draw, snapshot, []));
        Assert.Empty(draw.Lines.ToArray());
        int count = LogisticsZoneOverlay.Draw(draw, snapshot, [Building], maximumStrokes: 7);
        Assert.Equal(7, count);
        Assert.Equal(7, draw.Lines.Length);
        draw.Clear();
        Assert.Equal(strokes.Count, LogisticsZoneOverlay.Draw(draw, snapshot, [], logisticsMode: true));
        draw.Enabled = false;
        Assert.Equal(0, LogisticsZoneOverlay.Draw(draw, snapshot, [Truck]));
    }

    [Fact]
    public void InvalidGeometryAndUnavailableTerrainNeverProduceSpeculativeSegments()
    {
        var strokes = new List<LogisticsZoneStroke>();
        Assert.False(LogisticsZoneSnapshot.AddHatching(strokes, Terrain, Building, Truck,
            LogisticsZoneKind.Holding, new(float.NaN, 0, 0), 0, 4, 2, false));
        Assert.False(LogisticsZoneSnapshot.AddHatching(strokes, new MissingTerrain(), Building, Truck,
            LogisticsZoneKind.Loading, new(0, 0, 0), 0, 4, 2, false));
        Assert.Empty(strokes);
    }

    private sealed class PlaneTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-1000, -10, -1000), new(1000, 100, 1000));
        public bool TrySampleHeight(float x, float z, out float height)
        { height = 2 + .02f * (x + z); return MathF.Abs(x) < 1000 && MathF.Abs(z) < 1000; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal)
        { normal = Vector3.Normalize(new(-.02f, 1, -.02f)); return true; }
    }

    private sealed class MissingTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => default;
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return false; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return false; }
    }
}
