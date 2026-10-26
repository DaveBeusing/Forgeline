using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class MixedDockExitForwardClearanceTests
{
    [Fact]
    public void ACheckedForwardDepartureLetsTheBlockedDockOwnerExitWithoutReplacingEitherOrder()
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities;
        var owner = new PlayerId(2);
        var units = DirectorateContent.CreateUnitCatalog();
        var stopped = new WorldTransform(new(2619.9192f, 9.375535f, 1558.8759f), new(0, -.6993785f, 0, .7147515f), new(3.2f, 2.4f, 6.4f));
        var exiting = new WorldTransform(new(2622.6008f, 9.3245325f, 1553.726f), new(0, -.010089486f, 0, .9999491f), stopped.Scale);
        var coarse = scene.UnitFactory.Create(units[UnitIds.CargoTruck], stopped.Position, owner);
        var precise = scene.UnitFactory.Create(units[UnitIds.CargoTruck], exiting.Position, owner);
        entities.SetComponent(coarse, stopped);
        entities.SetComponent(precise, exiting);
        entities.SetComponent(coarse, GroundMovementState.Stationary(-1.5490552f));
        entities.SetComponent(precise, GroundMovementState.Stationary(-.020179315f));
        Assert.True(scene.Terrain.TrySampleHeight(2632, 1560, out float ground));
        Vector3 root = new(2632, ground + 1.1f, 1560);
        entities.AddComponent(coarse, new MovementOrder(owner, root, SimulationTick.Zero, new(1)));
        var bay = scene.GetBase(owner).CommandCore;
        var pose = new VehicleDockPose(new(2586.8f, 9.6144f, 1550.5f), new(2602, 9.493963f, 1550.5f),
            new(2617.2f, 9.359385f, 1550.5f), MathF.PI / 2, 3.2f, 1.6f, new(2571.4001f, 9.72162f, 1550.5f));
        var visit = new VehicleDockVisit(bay, 1, VehicleDockStatus.Exiting, VehicleDockBlockReason.None, pose,
            ExitTurning: true, LaneEntered: true, DepartureDistance: 5.2f);
        entities.AddComponent(bay, new VehicleDockSlot(precise, 1, pose, owner));
        entities.AddComponent(precise, visit);
        Assert.True(scene.Terrain.TrySampleHeight(2622.4f, 1563.6777f, out float targetHeight));
        entities.AddComponent(precise, new MovementOrder(owner, new(2622.4f, targetHeight + 1.1f, 1563.6777f),
            SimulationTick.Zero, new(1), MovementOrderKind.LocalApproach, 0));
        bool sawForwardClearance = false;
        for (int tick = 0; tick < 400; tick++)
        {
            scene.Simulation.AdvanceOneTick();
            sawForwardClearance |= entities.GetComponent<WorldTransform>(coarse).Position.X < stopped.Position.X - .1f;
            if (entities.TryGetComponent(bay, out VehicleDockSlot slot))
            {
                Assert.Equal(precise, slot.Truck);
                Assert.Equal(1UL, slot.Generation);
                Assert.Equal(pose, slot.Pose);
            }
            else Assert.False(pose.Contains(entities.GetComponent<WorldTransform>(precise).Position, pose.HalfLength));
        }
        Assert.True(sawForwardClearance, $"coarse={entities.GetComponent<WorldTransform>(coarse)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.True(entities.GetComponent<WorldTransform>(precise).Position.Z > exiting.Position.Z + 3,
            $"precise={entities.GetComponent<WorldTransform>(precise)}; veto={string.Join(';', scene.Services.GroundMovement.CaptureLegacyNavigationVetoes())}");
        Assert.False(entities.HasComponent<VehicleDockVisit>(precise));
        Assert.False(entities.HasComponent<VehicleDockSlot>(bay));
        Assert.Equal(root, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, actor => actor.Entity == coarse).OriginalOrder.WorldTarget);
    }
}
