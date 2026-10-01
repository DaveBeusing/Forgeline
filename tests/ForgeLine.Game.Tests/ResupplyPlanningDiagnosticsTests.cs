using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ResupplyPlanningDiagnosticsTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void ForeignProvidersAreExcludedFromDiagnosticCounts()
    {
        var fixture = new Fixture();
        fixture.Truck(Vector3.Zero, 80.0, 40.0, new PlayerId(2));
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result = fixture.Result(recipient);
        Assert.Equal(0, result.FriendlyProviders);
        Assert.Equal(0, result.RejectedProviders);
        Assert.Equal(ResupplyProviderRejection.NoFriendlyProvider, result.Rejections);
        Assert.False(result.SelectedProvider.IsValid);
    }

    [Theory]
    [InlineData(0.0, 40.0, ResupplyProviderRejection.ProviderFuelUnavailable)]
    [InlineData(80.0, 0.0, ResupplyProviderRejection.FuelStockUnavailable)]
    public void DistinguishesPropulsionShortageFromCargoShortage(
        double propulsion, double cargo, ResupplyProviderRejection expected)
    {
        var fixture = new Fixture();
        fixture.Truck(Vector3.Zero, propulsion, cargo, Owner);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result = fixture.Result(recipient);
        Assert.Equal(1, result.FriendlyProviders);
        Assert.Equal(1, result.RejectedProviders);
        Assert.True(result.Rejections.HasFlag(expected));
        Assert.False(result.SelectedProvider.IsValid);
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
    }

    [Fact]
    public void AlternativeSelectionRetainsRejectionReasonsForOtherOwnedProviders()
    {
        var fixture = new Fixture();
        fixture.Truck(new Vector3(60.0f, 0.0f, 0.0f), 80.0, 0.0, Owner);
        EntityId feasible = fixture.Truck(Vector3.Zero, 80.0, 40.0, Owner);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result = fixture.Result(recipient);
        Assert.Equal(2, result.FriendlyProviders);
        Assert.Equal(1, result.RejectedProviders);
        Assert.Equal(ResupplyProviderRejection.FuelStockUnavailable, result.Rejections);
        Assert.Equal(feasible, result.SelectedProvider);
    }

    [Fact]
    public void NewAttemptReplacesStaleFailureAfterPhysicalStockBecomesAvailable()
    {
        var fixture = new Fixture();
        EntityId provider = fixture.Truck(Vector3.Zero, 80.0, 0.0, Owner);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();
        Assert.False(fixture.Result(recipient).SelectedProvider.IsValid);
        SupplyTruck truck = fixture.Simulation.Entities.GetComponent<SupplyTruck>(provider);
        Assert.True(fixture.Inventories.Add(truck.InventoryId, ResourceIds.Fuel, 40.0).Succeeded);
        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result = fixture.Result(recipient);
        Assert.Equal(2UL, result.EvaluatedAtTick.Value);
        Assert.Equal(provider, result.SelectedProvider);
        Assert.Equal(ResupplyProviderRejection.None, result.Rejections);
        Assert.Equal(0, result.RejectedProviders);
        Assert.Equal(40.0, fixture.Inventories.GetQuantity(truck.InventoryId, ResourceIds.Fuel));
    }

    [Fact]
    public void InRangeMobileProviderPreservesFormationMembership()
    {
        var fixture = new Fixture();
        EntityId provider =
            fixture.Truck(
                new Vector3(105.0f, 0.0f, 0.0f),
                80.0,
                40.0,
                Owner);
        EntityId recipient =
            fixture.Recipient();
        EntityId group =
            fixture.Simulation.Entities.CreateEntity();

        fixture.Simulation.Entities.AddComponent(
            recipient,
            new MovementGroupMember(group));
        fixture.Simulation.Entities.AddComponent(
            recipient,
            new FormationMovementConstraint(8.0f));

        var context =
            new SimulationContext(
                fixture.Simulation.Entities,
                fixture.Simulation.CurrentTick,
                fixture.Simulation.TickDuration);

        Assert.True(
            BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(
                context,
                fixture.Inventories,
                recipient,
                Owner,
                fixture.Simulation.CurrentTick,
                BattlefieldSupplyResource.Fuel,
                out EntityId selectedProvider));

        Assert.Equal(
            provider,
            selectedProvider);
        Assert.True(
            fixture.Simulation.Entities.HasComponent<MovementGroupMember>(
                recipient));
        Assert.True(
            fixture.Simulation.Entities.HasComponent<FormationMovementConstraint>(
                recipient));
        Assert.True(
            fixture.Simulation.Entities.HasComponent<ResupplyOrder>(
                recipient));
        Assert.False(
            fixture.Simulation.Entities.HasComponent<MovementOrder>(
                recipient));
    }

    [Fact]
    public void UnreachableStaticProviderReportsRecipientTravelFailure()
    {
        var fixture = new Fixture();
        InventoryId stock = fixture.Inventories.CreateInventory(new InventorySpecification(100.0));
        Assert.True(fixture.Inventories.Add(stock, ResourceIds.Fuel, 40.0).Succeeded);
        EntityId provider = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(provider, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        fixture.Simulation.Entities.AddComponent(provider, new SupplyProvider(stock, Owner, 12.0f));
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result = fixture.Result(recipient);
        Assert.Equal(ResupplyProviderRejection.RecipientTravelUnavailable | ResupplyProviderRejection.ProviderNotMobile,
            result.Rejections);
        Assert.False(result.SelectedProvider.IsValid);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Simulation.RegisterSystem(new AutomaticResupplyDecisionSystem(Inventories));
        }

        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();

        public EntityId Truck(Vector3 position, double movementFuel, double cargoFuel, PlayerId owner)
        {
            var cargo = new CargoTransportSystem(new LogisticsNetwork(), Inventories);
            EntityId entity = SupplyTruckFactory.Create(Simulation.Entities, Inventories, position, owner, cargo);
            UnitFuelState fuel = Simulation.Entities.GetComponent<UnitFuelState>(entity);
            Assert.True(Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity - movementFuel).Succeeded);
            SupplyTruck truck = Simulation.Entities.GetComponent<SupplyTruck>(entity);
            Assert.True(Inventories.Add(truck.InventoryId, ResourceIds.Fuel, cargoFuel).Succeeded);
            return entity;
        }

        public EntityId Recipient()
        {
            EntityId entity = Simulation.Entities.CreateEntity();
            InventoryId inventory = Inventories.CreateInventory(new InventorySpecification(20.0));
            Simulation.Entities.AddComponent(entity, new WorldTransform(new Vector3(100.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(entity, new ControllableEntity(Owner, ControllableEntityCategory.Unit));
            Simulation.Entities.AddComponent(entity, new UnitFuelState(inventory, 20.0, 0.1));
            Simulation.Entities.AddComponent(entity, new SupplyMovementConstraint(0.0f, canMove: false));
            Simulation.Entities.AddComponent(entity, new AutomaticResupplyPolicy(0.2, 0.2));
            return entity;
        }

        public ResupplyPlanningResult Result(EntityId recipient) =>
            Simulation.Entities.GetComponent<ResupplyPlanningResult>(recipient);
    }
}
