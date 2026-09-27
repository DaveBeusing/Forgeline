using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SupplyRescueFeasibilityTests
{
    private static readonly PlayerId Owner = new(1);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void CargoFuelDoesNotMakeAnUnaffordableRescueFeasible(double movementFuel)
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, movementFuel, cargoFuel: 40.0);
        EntityId recipient = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));
        PlanCommand plan = fixture.Plan(recipient);

        Assert.False(plan.Accepted);
        Assert.False(plan.Provider.IsValid);
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        fixture.AssertStock(truck, movementFuel, 40.0);
    }

    [Fact]
    public void SkipsNearestUnaffordableTruckAndSelectsFeasibleAlternative()
    {
        var fixture = new Fixture();
        EntityId nearest = fixture.Truck(new Vector3(60.0f, 0.0f, 0.0f), 1.0, 40.0);
        EntityId farther = fixture.Truck(Vector3.Zero, 80.0, 40.0);
        EntityId recipient = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));
        PlanCommand plan = fixture.Plan(recipient);

        Assert.True(plan.Accepted);
        Assert.Equal(farther, plan.Provider);
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(nearest));
        Assert.True(fixture.Simulation.Entities.HasComponent<MovementOrder>(farther));
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(recipient));
        fixture.AssertStock(nearest, 1.0, 40.0);
        fixture.AssertStock(farther, 80.0, 40.0);
    }

    [Fact]
    public void ReservedPropulsionFuelCannotFundARescueTrip()
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, 80.0, 40.0);
        UnitFuelState fuel = fixture.Simulation.Entities.GetComponent<UnitFuelState>(truck);
        Assert.True(fixture.Inventories.Reserve(fuel.InventoryId, ResourceIds.Fuel, 79.0).Succeeded);
        EntityId recipient = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));

        Assert.False(fixture.Plan(recipient).Accepted);
        Assert.Equal(79.0, fixture.Inventories.GetReservedQuantity(fuel.InventoryId, ResourceIds.Fuel));
        fixture.AssertStock(truck, 80.0, 40.0);
    }

    [Fact]
    public void PropulsionFuelCannotReplaceMissingResupplyCargo()
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, 80.0, 0.0);
        EntityId recipient = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));

        Assert.False(fixture.Plan(recipient).Accepted);
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(truck));
        fixture.AssertStock(truck, 80.0, 0.0);
    }

    [Fact]
    public void InRangeTransferDoesNotRequireProviderPropulsionFuel()
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, 0.0, 40.0);
        EntityId recipient = fixture.Recipient(new Vector3(8.0f, 0.0f, 0.0f));
        PlanCommand plan = fixture.Plan(recipient);
        Assert.True(plan.Accepted);
        Assert.Equal(truck, plan.Provider);
        fixture.Simulation.RegisterSystem(new BattlefieldSupplySystem(fixture.Inventories));
        fixture.Simulation.AdvanceOneTick();

        UnitFuelState fuel = fixture.Simulation.Entities.GetComponent<UnitFuelState>(recipient);
        Assert.Equal(20.0, fixture.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel));
        fixture.AssertStock(truck, 0.0, 20.0);
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
    }

    [Fact]
    public void ASecondImmobileRecipientDoesNotRedirectTheAssignedTruck()
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, 80.0, 40.0);
        EntityId first = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));
        EntityId second = fixture.Recipient(new Vector3(-100.0f, 0.0f, 0.0f));
        Assert.True(fixture.Plan(first).Accepted);
        MovementOrder original = fixture.Simulation.Entities.GetComponent<MovementOrder>(truck);

        Assert.False(fixture.Plan(second).Accepted);
        Assert.Equal(original, fixture.Simulation.Entities.GetComponent<MovementOrder>(truck));
        Assert.Equal(truck, fixture.Simulation.Entities.GetComponent<ResupplyOrder>(first).Provider);
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(second));
    }

    [Fact]
    public void MissingPropulsionInventoryRejectsRescueWithoutChangingCargo()
    {
        var fixture = new Fixture();
        EntityId truck = fixture.Truck(Vector3.Zero, 80.0, 40.0);
        UnitFuelState fuel = fixture.Simulation.Entities.GetComponent<UnitFuelState>(truck);
        Assert.True(fixture.Inventories.DestroyInventory(fuel.InventoryId));
        EntityId recipient = fixture.Recipient(new Vector3(100.0f, 0.0f, 0.0f));

        Assert.False(fixture.Plan(recipient).Accepted);
        SupplyTruck supply = fixture.Simulation.Entities.GetComponent<SupplyTruck>(truck);
        Assert.Equal(40.0, fixture.Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
    }

    private sealed class Fixture
    {
        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();

        public EntityId Truck(Vector3 position, double movementFuel, double cargoFuel)
        {
            var cargo = new CargoTransportSystem(new LogisticsNetwork(), Inventories);
            EntityId entity = SupplyTruckFactory.Create(Simulation.Entities, Inventories, position, Owner, cargo);
            UnitFuelState fuel = Simulation.Entities.GetComponent<UnitFuelState>(entity);
            Assert.True(Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity - movementFuel).Succeeded);
            SupplyTruck truck = Simulation.Entities.GetComponent<SupplyTruck>(entity);
            Assert.True(Inventories.Add(truck.InventoryId, ResourceIds.Fuel, cargoFuel).Succeeded);
            return entity;
        }

        public EntityId Recipient(Vector3 position)
        {
            EntityId entity = Simulation.Entities.CreateEntity();
            InventoryId inventory = Inventories.CreateInventory(new InventorySpecification(20.0));
            Simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(entity, new ControllableEntity(Owner, ControllableEntityCategory.Unit));
            Simulation.Entities.AddComponent(entity, new UnitFuelState(inventory, 20.0, 0.1));
            Simulation.Entities.AddComponent(entity, new SupplyMovementConstraint(0.0f, canMove: false));
            return entity;
        }

        public PlanCommand Plan(EntityId recipient)
        {
            var command = new PlanCommand(Inventories, recipient);
            Simulation.SubmitCommand(command, Simulation.CurrentTick.Next());
            Simulation.AdvanceOneTick();
            return command;
        }

        public void AssertStock(EntityId truck, double movementFuel, double cargoFuel)
        {
            UnitFuelState fuel = Simulation.Entities.GetComponent<UnitFuelState>(truck);
            SupplyTruck supply = Simulation.Entities.GetComponent<SupplyTruck>(truck);
            Assert.Equal(movementFuel, Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), precision: 6);
            Assert.Equal(cargoFuel, Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), precision: 6);
        }
    }

    private sealed class PlanCommand : ISimulationCommand
    {
        private readonly InventoryStore _inventories;
        private readonly EntityId _recipient;

        public PlanCommand(InventoryStore inventories, EntityId recipient)
        {
            _inventories = inventories;
            _recipient = recipient;
        }

        public bool Accepted { get; private set; }
        public EntityId Provider { get; private set; }

        public void Execute(SimulationContext context)
        {
            Accepted = BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(
                context, _inventories, _recipient, Owner, context.Tick,
                BattlefieldSupplyResource.Fuel, out EntityId provider);
            Provider = provider;
        }
    }
}
