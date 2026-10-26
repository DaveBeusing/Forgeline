using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class MixedDockExitPassageClearanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOccupiedCrossingAllowsOnlyAnOutwardCheckedDeparture(bool outward)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities;
        var owner = new PlayerId(2);
        var units = DirectorateContent.CreateUnitCatalog();
        var stopped = new WorldTransform(new(2619.7385f, 9.376687f, 1558.7617f), new(0, -.6985983f, 0, .71551406f), new(3.2f, 2.4f, 6.4f));
        var exiting = new WorldTransform(new(2612.5168f, 9.402391f, 1550.5f), new(0, .70710677f, 0, .70710677f), stopped.Scale);
        var coarse = scene.UnitFactory.Create(units[UnitIds.CargoTruck], stopped.Position, owner);
        var precise = scene.UnitFactory.Create(units[UnitIds.CargoTruck], exiting.Position, owner);
        entities.SetComponent(coarse, stopped);
        entities.SetComponent(precise, exiting);
        entities.SetComponent(coarse, GroundMovementState.Stationary(-1.5468732f));
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
        // Restore the already-entered crossing from the recorded traffic checkpoint.
        // The same poses without this occupied lease do not reproduce the deadlock.
        var field = typeof(GroundMovementSystem).GetField("_navigationControl",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var coordinator = Assert.IsType<LogisticsNavigationCoordinator>(field!.GetValue(scene.Services.GroundMovement));
        var passage = new NavigationTrafficPassage(7, new(new(outward ? 2622 : 2618, 9.348057f, 1558), Quaternion.Identity), new(6.0235925f, 1, 6.0235925f));
        coordinator.RestoreState(new(1, 0, [], new(3, [], [new(passage, coarse, 1, 1, 0, 0, true)])));
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
        Assert.Equal(root, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, actor => actor.Entity == coarse).OriginalOrder.WorldTarget);
        if (!outward)
        {
            Assert.Equal(stopped, entities.GetComponent<WorldTransform>(coarse));
            Assert.Equal(exiting, entities.GetComponent<WorldTransform>(precise));
            Assert.True(entities.HasComponent<VehicleDockVisit>(precise));
            Assert.True(entities.HasComponent<VehicleDockSlot>(bay));
            Assert.Equal(coarse, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Traffic.Leases).Owner);
            return;
        }
        Assert.True(sawForwardClearance, $"coarse={entities.GetComponent<WorldTransform>(coarse)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.True(entities.GetComponent<WorldTransform>(precise).Position.X > exiting.Position.X + 3,
            $"precise={entities.GetComponent<WorldTransform>(precise)}; veto={string.Join(';', scene.Services.GroundMovement.CaptureLegacyNavigationVetoes())}");
        Assert.False(entities.HasComponent<VehicleDockVisit>(precise));
        Assert.False(entities.HasComponent<VehicleDockSlot>(bay));
    }
}
