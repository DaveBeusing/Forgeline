using System.Numerics;
using ForgeLine.Navigation;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class NavigationSweptOccupancyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(.14f)]
    [InlineData(1.57f)]
    [InlineData(3.14f)]
    public void OrientedOccupancyContainsEveryCornerThroughoutAcceptedMotion(float angle)
    {
        var start = new NavigationPose(new(3000, 2, 3000), Quaternion.Identity);
        var end = new NavigationPose(new(3002, 3, 2999), Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(.2f, 1, .3f)), angle));
        var profile = new NavigationVehicleProfile(new(1.7f, 1.25f, 3.4f), .25f, 13, 6.5f, 10, 2.35f);
        Assert.True(NavigationMotionArbitrator.TryBuildSweptOccupancy(start, end, profile, out var occupancy, out var half));
        for (int step = 0; step <= 200; step++)
        {
            float t = step / 200f;
            Vector3 center = Vector3.Lerp(start.Position, end.Position, t);
            Quaternion rotation = Quaternion.Slerp(start.Rotation, end.Rotation, t);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = profile.HalfExtents * new Vector3((corner & 1) == 0 ? -1 : 1,
                    (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 world = center + Vector3.Transform(local, rotation);
                Vector3 enclosed = Vector3.Abs(Vector3.Transform(world - occupancy.Position, Quaternion.Conjugate(occupancy.Rotation)));
                Assert.True(enclosed.X <= half.X && enclosed.Y <= half.Y && enclosed.Z <= half.Z,
                    $"step={step}; corner={corner}; enclosed={enclosed}; half={half}");
            }
        }
    }
}
