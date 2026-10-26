using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class NativeReservedDockEvacuationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APhysicalLaneOccupantCanClearOnlyTheCurrentReservedRefuelingOwnersExit(bool currentGeneration)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities;
        var owner = new PlayerId(2);
        var depot = entities.CreateEntity();
        var depotPose = new WorldTransform(new(2220, 3.316606f, 760), Quaternion.Identity, new(16, 8, 16));
        entities.AddComponent(depot, depotPose);
        entities.AddComponent(depot, new SpatialPresence(new(8, 4, 8), new SpatialEntryMetadata(2, 2, SpatialMobility.Static)));
        entities.AddComponent(depot, new CompletedBuilding(BuildingIds.SupplyDepot, owner, SimulationTick.Zero));
        entities.AddComponent(depot, new BuildingLoadingDock(new(.96f, -4, 12.5f), Vector3.UnitX, 12));
        var stock = scene.Inventories.CreateInventory(new InventorySpecification(500));
        Assert.True(scene.Inventories.Add(stock, ResourceIds.Fuel, 200).Succeeded);
        entities.AddComponent(depot, new InventoryStorage(stock));
        entities.AddComponent(depot, new SupplyDepot(stock, owner));
        entities.AddComponent(depot, new SupplyProvider(stock, owner, 20));
        var units = DirectorateContent.CreateUnitCatalog();
        var supplyPose = new WorldTransform(new(2228.2603f, .3576014f, 777.92487f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -2.6299305f), new(3.4f, 2.5f, 6.8f));
        var supply = scene.UnitFactory.Create(units[UnitIds.SupplyTruck], supplyPose.Position, owner);
        entities.SetComponent(supply, supplyPose);
        entities.SetComponent(supply, GroundMovementState.Stationary(-2.6299305f));
        var automatic = new AutomaticResupplyPolicy(.3, .35, enabled: false);
        if (entities.HasComponent<AutomaticResupplyPolicy>(supply)) entities.SetComponent(supply, automatic);
        else entities.AddComponent(supply, automatic);
        var goal = new Vector3(2742.24f, 7.9861627f, 1634.1f);
        entities.AddComponent(supply, new MovementOrder(owner, goal, SimulationTick.Zero, new(1)));
        var supplyFuel = entities.GetComponent<UnitFuelState>(supply);
        double initialSupplyFuel = scene.Inventories.GetQuantity(supplyFuel.InventoryId, ResourceIds.Fuel);
        var cargoPose = new WorldTransform(new(2191.6208f, -.3067534f, 771.46906f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.4980085f), new(3.2f, 2.4f, 6.4f));
        var cargo = scene.UnitFactory.Create(units[UnitIds.CargoTruck], cargoPose.Position, owner);
        entities.SetComponent(cargo, cargoPose);
        entities.SetComponent(cargo, GroundMovementState.Stationary(1.4980085f));
        var fuel = entities.GetComponent<UnitFuelState>(cargo);
        const double initialFuel = 65.53002356373406;
        Assert.True(scene.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel,
            scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) - initialFuel).Succeeded);
        var pose = new VehicleDockPose(new(2205.76f, -.0418998f, 772.5f), new(2220.96f, .23740017f, 772.5f),
            new(2236.16f, .5077553f, 772.5f), MathF.PI / 2, 3.2f, 1.6f, new(2190.36f, -.33342433f, 772.5f));
        entities.AddComponent(cargo, new VehicleDockVisit(depot, currentGeneration ? 43UL : 42UL, VehicleDockStatus.Blocked,
            VehicleDockBlockReason.Obstructed, pose, DepartureDistance: 5.2f));
        entities.AddComponent(depot, new VehicleDockSlot(cargo, 43, pose, owner));
        entities.AddComponent(depot, new VehicleQueueLane(pose.ApproachStart!.Value, pose.Heading, 2));
        entities.AddComponent(cargo, new VehicleQueueEntry(depot, owner, 44, SimulationTick.Zero,
            VehicleQueueService.CargoRefueling, 1, 1, pose.ApproachStart.Value, pose.Heading));
        entities.AddComponent(cargo, new CargoFuelServiceState(depot, CargoFuelServicePhase.ToProvider,
            CargoFuelServiceReason.None, SimulationTick.Zero));
        entities.AddComponent(cargo, new CargoFuelServiceDemand(SimulationTick.Zero));
        scene.Simulation.RunTicks(1000, TestContext.Current.CancellationToken);
        if (!currentGeneration)
        {
            Assert.Equal(supplyPose, entities.GetComponent<WorldTransform>(supply));
            Assert.True(scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) <= initialFuel);
            var retained = entities.GetComponent<VehicleDockSlot>(depot);
            Assert.Equal(cargo, retained.Truck);
            Assert.Equal(43UL, retained.Generation);
            return;
        }
        Assert.InRange(scene.Inventories.GetQuantity(stock, ResourceIds.Fuel) +
            scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) +
            scene.Inventories.GetQuantity(supplyFuel.InventoryId, ResourceIds.Fuel), 0, 200 + initialFuel + initialSupplyFuel);
        Assert.True(Vector3.Distance(entities.GetComponent<WorldTransform>(supply).Position, supplyPose.Position) > 10,
            $"supply={entities.GetComponent<WorldTransform>(supply)}; waits={string.Join(';',scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles)}");
        Assert.True(scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) > initialFuel,
            $"cargo={entities.GetComponent<WorldTransform>(cargo)}");
        Assert.True(scene.Inventories.GetQuantity(stock, ResourceIds.Fuel) < 200);
        Assert.Equal(goal, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors,
            actor => actor.Entity == supply).OriginalOrder.WorldTarget);
    }
}
