using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class NativeDockClearanceEvacuationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AVerifiedReverseCanClearTheRecordedDockExitWithoutReleasingTheBayEarly(bool currentGeneration)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        scene.Simulation.RunTicks(40, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities; var player = new PlayerId(2); var units = DirectorateContent.CreateUnitCatalog();
        var coarsePose = new WorldTransform(new(2633.0186f, 9.180581f, 1545.8276f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6514438f), new(3.2f, 2.4f, 6.4f));
        var precisePose = new WorldTransform(new(2629.8079f, 9.237237f, 1550.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), coarsePose.Scale);
        var coarse = scene.UnitFactory.Create(units[UnitIds.CargoTruck], coarsePose.Position, player);
        var precise = scene.UnitFactory.Create(units[UnitIds.CargoTruck], precisePose.Position, player);
        entities.SetComponent(coarse, coarsePose); entities.SetComponent(precise, precisePose);
        entities.SetComponent(coarse, GroundMovementState.Stationary(.6514438f));
        entities.SetComponent(precise, GroundMovementState.Stationary(MathF.PI / 2));
        var bay = scene.GetBase(player).CommandCore;
        var destination = entities.CreateEntity();
        entities.AddComponent(destination, new CompletedBuilding(BuildingIds.Refinery, player, SimulationTick.Zero));
        var pose = new VehicleDockPose(new(2586.8f, 9.6144f, 1550.5f), new(2602, 9.493963f, 1550.5f), new(2617.2f, 9.359385f, 1550.5f), MathF.PI / 2, 3.2f, 1.6f, new(2571.4001f, 9.72162f, 1550.5f));
        entities.AddComponent(bay, new VehicleDockSlot(precise, 30, pose, player));
        entities.AddComponent(precise, new VehicleDockVisit(bay, currentGeneration ? 30UL : 29UL, VehicleDockStatus.Exiting, VehicleDockBlockReason.None, pose, LaneEntered: true, DepartureDistance: 18.8f));
        entities.AddComponent(coarse, new VehicleDockVisit(destination, 0, VehicleDockStatus.Busy, VehicleDockBlockReason.Obstructed, pose));
        var goal = new Vector3(2667.4001f, 8.809401f, 1548.5f);
        entities.AddComponent(coarse, new MovementOrder(player, goal, SimulationTick.Zero, new(1)));
        Assert.True(scene.Terrain.TrySampleHeight(2636, 1550.5f, out float height));
        entities.AddComponent(precise, new MovementOrder(player, new(2636, height + 1.1f, 1550.5f), SimulationTick.Zero, new(1), MovementOrderKind.LocalApproach, MathF.PI / 2));
        var field = typeof(GroundMovementSystem).GetField("_navigationControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var coordinator = Assert.IsType<LogisticsNavigationCoordinator>(field!.GetValue(scene.Services.GroundMovement));
        var target = new Vector3(2631.2612f, 9.18623f, 1543.5232f);
        coordinator.RestoreState(new(1, 40, [], new(3, [new(coarse, 1, 40, 0, 40, NavigationBlockReason.ReservedCorridor, default, target)], [])));
        var fuel = entities.GetComponent<UnitFuelState>(coarse); double initialFuel = scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        for (int tick = 0; tick < 400; tick++)
        {
            scene.Simulation.AdvanceOneTick();
            if (entities.TryGetComponent(bay, out VehicleDockSlot slot)) { Assert.Equal(precise, slot.Truck); Assert.Equal(30UL, slot.Generation); Assert.Equal(pose, slot.Pose); }
            else Assert.False(pose.Contains(entities.GetComponent<WorldTransform>(precise).Position, pose.HalfLength));
        }
        Assert.InRange(scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), 0, initialFuel);
        if (!currentGeneration) { Assert.Equal(coarsePose, entities.GetComponent<WorldTransform>(coarse)); Assert.True(entities.HasComponent<VehicleDockSlot>(bay)); return; }
        Assert.True(Vector3.Distance(coarsePose.Position, entities.GetComponent<WorldTransform>(coarse).Position) > 1, $"coarse={entities.GetComponent<WorldTransform>(coarse)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.False(entities.HasComponent<VehicleDockSlot>(bay), $"coarse={entities.GetComponent<WorldTransform>(coarse)}; precise={entities.GetComponent<WorldTransform>(precise)}; waits={string.Join(';', scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.Equal(goal, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, a => a.Entity == coarse).OriginalOrder.WorldTarget);
    }
}
