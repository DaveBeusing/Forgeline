using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class MixedDockExitThreeVehicleTests
{
    [Fact]
    public void AThirdVehicleDoesNotTrapTheStraightDockDeparture()
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities;
        var owner = new PlayerId(2);
        var units = DirectorateContent.CreateUnitCatalog();
        var stopped = new WorldTransform(new(2629.454f, 9.268804f, 1556.1294f), new(0, .5101836f, 0, .8600656f), new(3.2f, 2.4f, 6.4f));
        var exiting = new WorldTransform(new(2634.2917f, 9.191558f, 1550.5f), new(0, .70710677f, 0, .70710677f), stopped.Scale);
        var coarse = scene.UnitFactory.Create(units[UnitIds.CargoTruck], stopped.Position, owner);
        var precise = scene.UnitFactory.Create(units[UnitIds.CargoTruck], exiting.Position, owner);
        entities.SetComponent(coarse, stopped);
        entities.SetComponent(precise, exiting);
        entities.SetComponent(coarse, GroundMovementState.Stationary(1.0707964f));
        entities.SetComponent(precise, GroundMovementState.Stationary(MathF.PI / 2));
        Assert.True(scene.Terrain.TrySampleHeight(2632, 1560, out float ground));
        Vector3 root = new(2632, ground + 1.1f, 1560);
        entities.AddComponent(coarse, new MovementOrder(owner, root, SimulationTick.Zero, new(1)));
        var bay = scene.GetBase(owner).CommandCore;
        var pose = new VehicleDockPose(new(2586.8f, 9.6144f, 1550.5f), new(2602, 9.493963f, 1550.5f),
            new(2617.2f, 9.359385f, 1550.5f), MathF.PI / 2, 3.2f, 1.6f, new(2571.4001f, 9.72162f, 1550.5f));
        var visit = new VehicleDockVisit(bay, 1, VehicleDockStatus.Exiting, VehicleDockBlockReason.None, pose,
            ExitTurning: false, LaneEntered: true, DepartureDistance: 32.4f);
        entities.AddComponent(bay, new VehicleDockSlot(precise, 1, pose, owner));
        entities.AddComponent(precise, visit);
        Assert.True(scene.Terrain.TrySampleHeight(2649.5999f, 1550.5f, out float targetHeight));
        entities.AddComponent(precise, new MovementOrder(owner, new(2649.5999f, targetHeight + 1.1f, 1550.5f),
            SimulationTick.Zero, new(1), MovementOrderKind.LocalApproach, MathF.PI / 2));
        var thirdPose = new WorldTransform(new(2638.5103f, 9.164272f, 1553.8772f), new(0, -.30569097f, 0, .9521308f), stopped.Scale);
        var third = scene.UnitFactory.Create(units[UnitIds.CargoTruck], thirdPose.Position, owner);
        entities.SetComponent(third, thirdPose);
        entities.SetComponent(third, GroundMovementState.Stationary(-.6213281f));
        entities.AddComponent(third, new MovementOrder(owner, new(2568, 8.693897f, 1560), SimulationTick.Zero, new(1)));
        bool sawForwardClearance = false;
        for (int tick = 0; tick < 600; tick++)
        {
            scene.Simulation.AdvanceOneTick();
            sawForwardClearance |= entities.GetComponent<WorldTransform>(coarse).Position.X > stopped.Position.X + .1f;
            if (entities.TryGetComponent(bay, out VehicleDockSlot slot))
            {
                Assert.Equal(precise, slot.Truck);
                Assert.Equal(1UL, slot.Generation);
                Assert.Equal(pose, slot.Pose);
            }
            else Assert.False(pose.Contains(entities.GetComponent<WorldTransform>(precise).Position, pose.HalfLength));
        }
        Assert.Equal(root, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, actor => actor.Entity == coarse).OriginalOrder.WorldTarget);
        Assert.True(sawForwardClearance, $"coarse={entities.GetComponent<WorldTransform>(coarse)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.True(entities.GetComponent<WorldTransform>(precise).Position.X > exiting.Position.X + 3,
            $"coarse={entities.GetComponent<WorldTransform>(coarse)}; third={entities.GetComponent<WorldTransform>(third)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}; precise={entities.GetComponent<WorldTransform>(precise)}; veto={string.Join(';', scene.Services.GroundMovement.CaptureLegacyNavigationVetoes())}");
        Assert.False(entities.HasComponent<VehicleDockVisit>(precise), $"precise={entities.GetComponent<WorldTransform>(precise)}; third={entities.GetComponent<WorldTransform>(third)}; veto={string.Join(';', scene.Services.GroundMovement.CaptureLegacyNavigationVetoes())}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.False(entities.HasComponent<VehicleDockSlot>(bay));
    }
}
