using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed partial class PredictiveTrafficBatchTests
{
    [Fact]
    public void ExhaustedOptionalDetoursDoNotReportMissingGeometry()
    {
        var (source, intents) = Crossing();
        var vehicle = source.Vehicles[0] with { Movement = GroundMovementState.Stationary(MathF.PI / 2) };
        var terrain = new TerrainWorld(new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 17 },
            [new TerrainChunk(new(0, 0), new TerrainHeightfield(17, 64, new float[289]))]);
        var obstacle = new NavigationCollisionBody(new(NavigationObstacleSource.Entity, 99, 1), NavigationObstacleKind.Vehicle,
            new(new(18, .5f, 20), Quaternion.Identity), new(.5f), Vector3.Zero);
        var body = new NavigationCollisionBody(NavigationReadAdapter.GetObstacleIdentity(vehicle.Entity), NavigationObstacleKind.Vehicle,
            vehicle.Pose, vehicle.Profile.HalfExtents, Vector3.Zero);
        var collision = new NavigationCollisionFrame(source.Collision.World, 50,
            new AxisAlignedBounds(new(0, -10, 0), new(64, 10, 64)), true, [body, obstacle], 128, bodyPosesAreFixed: true);
        var frame = new NavigationTrafficFrame(new(50), collision, true, [vehicle], [], Limits());
        var geometry = new NavigationVehicleGeometry(collision.World, terrain, 128, .5f);
        for (int allowance = 128; allowance <= 4096; allowance++)
        {
            var directive = Assert.Single(new PredictiveTrafficPlanner().PlanBatch(frame, [intents[0]], geometry, 3, allowance, out int work));
            Assert.InRange(work, 0, allowance);
            Assert.True(directive.Reason != NavigationBlockReason.MissingGeometry, $"allowance={allowance}; work={work}; directive={directive}");
        }
    }
}
