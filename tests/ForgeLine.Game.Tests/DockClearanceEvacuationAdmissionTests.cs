using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class DockClearanceEvacuationAdmissionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(10, true)]
    public void CapturedClearanceOccupantsMayOnlyEvacuateOutwardWithoutChangingTheBay(int control, bool blocked)
    {
        var entities = new EntityRegistry(); var player = new PlayerId(2);
        var building = entities.CreateEntity(); var precise = entities.CreateEntity(); var coarse = entities.CreateEntity(); var destination = entities.CreateEntity();
        var pose = new VehicleDockPose(new(2586.8f, 9.6144f, 1550.5f), new(2602, 9.493963f, 1550.5f), new(2617.2f, 9.359385f, 1550.5f), MathF.PI / 2, 3.2f, 1.6f, new(2571.4001f, 9.72162f, 1550.5f));
        entities.AddComponent(building, new CompletedBuilding(BuildingIds.CommandCore, player, SimulationTick.Zero));
        var slot = new VehicleDockSlot(precise, 30, pose, player);
        entities.AddComponent(building, slot);
        entities.AddComponent(precise, new VehicleDockVisit(building, control == 9 ? 29UL : 30UL, VehicleDockStatus.Exiting, VehicleDockBlockReason.None, pose, LaneEntered: true));
        entities.AddComponent(coarse, new VehicleDockVisit(destination, 0, VehicleDockStatus.Busy, VehicleDockBlockReason.Obstructed, pose));
        var current = new WorldTransform(new(2633.0186f, 9.180581f, 1545.8276f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6514438f), new(3.2f, 2.4f, 6.4f));
        var delta = new Vector3(-.0176f, 0, -.023f);
        if (control == 1) delta = new(-.02f, 0, .04f);
        if (control == 4) { current = current with { Position = current.Position + Vector3.UnitX }; delta = new(-1.1f, 0, -.01f); }
        var next = current with { Position = current.Position + delta };
        if (control == 7) next = next with { Scale = current.Scale * 1.1f };
        var clearance = control == 2 ? 0 : control == 3 ? .001f : .25f;
        var profile = new NavigationVehicleProfile(control == 5 ? Vector3.One : current.Scale * .5f, clearance, 8, 4, 4, 1);
        entities.AddComponent(coarse, current);
        var captured = new NavigationVehicleObservation(coarse, player, new(current.Position, current.Rotation), profile, GroundMovementState.Stationary(.6514438f), null, null, null, null, null, null);
        if (control == 10) captured = captured with { Pose = captured.Pose with { Position = captured.Pose.Position + Vector3.UnitX } };
        if (control == 8)
        {
            var predecessor = entities.CreateEntity(); var forward = Vector3.Normalize(delta); var heading = MathF.Atan2(forward.X, forward.Z);
            entities.AddComponent(predecessor, current with { Position = current.Position + forward * .1f });
            entities.AddComponent(coarse, new VehicleQueueEntry(building, player, 1, SimulationTick.Zero, VehicleQueueService.CargoPickup, 2, 2, current.Position + forward * 10, heading));
            entities.AddComponent(coarse, new VehicleQueueMotion(current.Position, current.Position + forward * 10, heading, predecessor, 3, 1));
        }
        var actual = VehicleDockingService.BlocksStagingStep(entities, coarse, current, next, control == 6 ? null : captured);
        Assert.Equal(blocked, actual);
        Assert.Equal(slot, entities.GetComponent<VehicleDockSlot>(building));
        if (control == 0)
        {
            var bounds = pose.CorridorBounds;
            var reservation = new NavigationTrafficReservation(1, precise, 30, 0, NavigationReservationKind.Dock, new(bounds.Center, Quaternion.Identity), bounds.Extents, SimulationTick.Zero, SimulationTick.Zero, true);
            Assert.False(PredictiveTrafficPlanner.BlocksServiceReservation(captured, reservation, [captured.Pose, new(next.Position, next.Rotation)]));
            Assert.True(VehicleDockingService.BlocksStagingStep(entities, coarse, current, next));
        }
    }
}
