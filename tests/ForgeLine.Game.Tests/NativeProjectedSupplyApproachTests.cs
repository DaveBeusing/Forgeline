using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class NativeProjectedSupplyApproachTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReversingProjectedEndpointCanReachExistingDepotFuelWithoutLosingItsOriginalGoal(bool hasFuel)
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
        var stock = scene.Inventories.CreateInventory(new InventorySpecification(500));
        Assert.True(scene.Inventories.Add(stock, ResourceIds.Fuel, 200).Succeeded);
        entities.AddComponent(depot, new InventoryStorage(stock));
        entities.AddComponent(depot, new SupplyDepot(stock, owner));
        entities.AddComponent(depot, new SupplyProvider(stock, owner, 20));
        var initial = new WorldTransform(new(2239.385f, .5627384f, 773.0688f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI), new(3.4f, 2.5f, 6.8f));
        var unit = scene.UnitFactory.Create(DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck], initial.Position, owner);
        entities.SetComponent(unit, initial);
        entities.SetComponent(unit, GroundMovementState.Stationary(-MathF.PI));
        var automatic = new AutomaticResupplyPolicy(.3, .35, enabled: false);
        if (entities.HasComponent<AutomaticResupplyPolicy>(unit)) entities.SetComponent(unit, automatic);
        else entities.AddComponent(unit, automatic);
        var fuel = entities.GetComponent<UnitFuelState>(unit);
        double initialFuel = hasFuel ? 7.037848621659415 : 0;
        Assert.True(scene.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel,
            scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) - initialFuel).Succeeded);
        var goal = new Vector3(2230.9875f, 3.316606f, 770.2114f);
        entities.AddComponent(unit, new MovementOrder(owner, goal, SimulationTick.Zero, new(1)));
        scene.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        var position = entities.GetComponent<WorldTransform>(unit).Position;
        double remaining = scene.Inventories.GetQuantity(stock, ResourceIds.Fuel);
        double carried = scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        if (!hasFuel)
        {
            Assert.Equal(initial.Position, position);
            Assert.Equal(200, remaining);
            Assert.Equal(0, carried);
            return;
        }
        Assert.True(Vector2.Distance(new(position.X, position.Z), new(depotPose.Position.X, depotPose.Position.Z)) <= 20,
            $"position={position}; movement={entities.GetComponent<GroundMovementState>(unit)}");
        Assert.True(carried > initialFuel);
        Assert.True(remaining < 200);
        Assert.InRange(remaining + carried, 0, 200 + initialFuel);
        Assert.Equal(goal, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, actor => actor.Entity == unit).OriginalOrder.WorldTarget);
    }
}
