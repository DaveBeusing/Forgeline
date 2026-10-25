using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class UnitProductionExitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionDoesNotOverlapOccupantsOrSpendReservedInputsWhileAllExitsAreBlocked(bool allBlocked)
    {
        var simulation = new SimulationCoordinator();
        var entities = simulation.Entities;
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        var units = DirectorateContent.CreateUnitCatalog();
        var factory = new UnitFactory(entities, inventories, cargo);
        simulation.RegisterSystem(new UnitProductionSystem(units, inventories, factory));
        var definition = units[UnitIds.ScoutVehicle];
        var input = inventories.CreateInventory(new InventorySpecification(2000));
        foreach (var cost in definition.Costs)
            Assert.True(inventories.Add(input, cost.ResourceId, cost.Quantity).Succeeded);
        var owner = new PlayerId(1);
        var facility = entities.CreateEntity();
        entities.AddComponent(facility, new WorldTransform(new(50, 0, 50), Quaternion.Identity, new(20, 10, 20)));
        entities.AddComponent(facility, new UnitProductionFacility(input, UnitProductionCapability.Vehicle,
            owner, new(0, 0, 18), SimulationTick.Zero));
        entities.AddComponent(facility, new PowerConsumer(40, allocatedPower: 40, state: PowerOperationalState.Powered));
        var parked = entities.CreateEntity();
        var occupied = new WorldTransform(new(50, 0, 68), Quaternion.Identity, allBlocked ? new(200, 10, 200) : new(4, 2, 6));
        entities.AddComponent(parked, occupied);
        entities.AddComponent(parked, new SpatialPresence(occupied.Scale * .5f,
            new SpatialEntryMetadata(2, 1, SpatialMobility.Mobile)));
        var command = new QueueUnitProductionCommand(owner, facility, definition.Id, SimulationTick.Zero);
        simulation.SubmitCommand(command, simulation.CurrentTick.Next());
        simulation.RunTicks((ulong)definition.ProductionTicks + 1, TestContext.Current.CancellationToken);
        Assert.True(command.Accepted);
        Assert.Equal(occupied, entities.GetComponent<WorldTransform>(parked));
        if (allBlocked)
        {
            Assert.Equal(0, entities.GetComponentCount<UnitIdentity>());
            Assert.True(entities.IsAlive(command.RequestEntity));
            var blocked = entities.GetComponent<UnitProductionFacility>(facility);
            Assert.Equal(UnitProductionStatus.OutputBlocked, blocked.Status);
            Assert.Equal(UnitProductionBlockReason.OutputBlocked, blocked.BlockReason);
            Assert.True(blocked.InputsReserved);
            foreach (var cost in definition.Costs)
            {
                Assert.Equal(cost.Quantity, inventories.GetQuantity(input, cost.ResourceId));
                Assert.Equal(0, inventories.GetAvailableQuantity(input, cost.ResourceId));
            }
            entities.DestroyEntity(parked);
            simulation.AdvanceOneTick();
        }
        Assert.Equal(1, entities.GetComponentCount<UnitIdentity>());
        EntityId produced = default;
        foreach (var entity in entities.Query<UnitIdentity>()) produced = entity;
        var pose = entities.GetComponent<WorldTransform>(produced);
        if (!allBlocked)
            Assert.True(MathF.Abs(pose.Position.X - occupied.Position.X) >
                (pose.Scale.X + occupied.Scale.X) * .5f ||
                MathF.Abs(pose.Position.Z - occupied.Position.Z) > (pose.Scale.Z + occupied.Scale.Z) * .5f);
        Assert.False(entities.IsAlive(command.RequestEntity));
        foreach (var cost in definition.Costs)
            Assert.Equal(0, inventories.GetQuantity(input, cost.ResourceId));
        Assert.Equal(1UL, entities.GetComponent<UnitProductionFacility>(facility).CompletedUnits);
    }
}
