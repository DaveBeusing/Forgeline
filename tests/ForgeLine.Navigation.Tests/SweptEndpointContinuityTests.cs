using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Navigation.Tests;

public sealed class SweptEndpointContinuityTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(.002f, true)]
    public void RoundedSweepCannotAdmitAnEndpointThatTheNextFixedPoseQueryBlocks(float extraGap, bool clear)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(8, 24), new TerrainHeightfield(17, 64, new float[289]))]);
        var world = NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 });
        var caps = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        var snapshot = new NavigationWorldSnapshot(world, world.GetSectorGraph(caps), caps, terrain, 0, 1,
            NavigationModuleAvailability.NotConnected, [], []);
        var geometry = new NavigationVehicleGeometry(snapshot, terrain, 128, .25f);
        var scout = new NavigationPose(new(521.1346f, 1.2f, 1579.2606f), new(0, .78636897f, 0, .61775714f));
        Vector3 scoutHalf = new(1.5f, 1.2f, 2.6f), supplyHalf = new(1.7f, 1.25f, 3.4f);
        var start = new NavigationPose(new(520.63745f, 1.1f, 1583.9272f), new(0, .87213475f, 0, .4892658f));
        var end = start with { Position = new Vector3(520.6513f, 1.1f, 1583.9188f) -
            Vector3.Transform(Vector3.UnitX, scout.Rotation) * extraGap };
        var profile = new NavigationVehicleProfile(supplyHalf, .25f, 13, 6.5f, 10, 2.35f);
        var coverage = new AxisAlignedBounds(new(512, -10, 1536), new(576, 10, 1600));
        var obstacle = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 1, 1), NavigationObstacleKind.Vehicle,
            scout, scoutHalf, Vector3.Zero);
        var frame = new NavigationCollisionFrame(snapshot, 1, coverage, true, [obstacle], 4, bodyPosesAreFixed: true);
        var sweep = new NavigationSweep(new(NavigationObstacleSource.Entity, 2, 1), start, end, profile, .05f);
        Assert.Equal(NavigationGeometryStatus.Clear, geometry.Sweep(frame, sweep with { End = start }, 10000).Status);
        var result = geometry.Sweep(frame, sweep, 10000);
        Assert.Equal(clear ? NavigationGeometryStatus.Clear : NavigationGeometryStatus.Blocked, result.Status);
        var next = geometry.Sweep(frame, sweep with { Start = end, DurationSeconds = 0 }, 10000);
        var opposite = new NavigationCollisionFrame(snapshot, 1, coverage, true,
            [obstacle with { Identity = new(NavigationObstacleSource.Entity, 2, 1), Pose = end, HalfExtents = supplyHalf }],
            4, bodyPosesAreFixed: true);
        var peer = geometry.Sweep(opposite, new(new(NavigationObstacleSource.Entity, 1, 1), scout, scout,
            profile with { HalfExtents = scoutHalf }, 0), 10000);
        Assert.Equal(clear ? NavigationGeometryStatus.Clear : NavigationGeometryStatus.Blocked, next.Status);
        Assert.Equal(next.Status, peer.Status);
    }
}
