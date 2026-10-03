using System.Numerics;
using ForgeLine.Combat;
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
    public void BusyMobileProviderDoesNotAttractAdditionalReachableRecipient()
    {
        var fixture =
            new Fixture();
        EntityId nearest =
            fixture.Truck(
                new Vector3(
                    60.0f,
                    0.0f,
                    0.0f),
                movementFuel: 80.0,
                cargoFuel: 40.0,
                Owner);
        EntityId alternate =
            fixture.Truck(
                new Vector3(
                    40.0f,
                    0.0f,
                    0.0f),
                movementFuel: 80.0,
                cargoFuel: 40.0,
                Owner);
        EntityId first =
            fixture.MobileRecipient(
                new Vector3(
                    100.0f,
                    0.0f,
                    0.0f));
        EntityId second =
            fixture.MobileRecipient(
                new Vector3(
                    100.0f,
                    0.0f,
                    1.0f));

        var firstCommand =
            new ResupplyCommand(
                Owner,
                [first],
                SimulationTick.Zero);
        fixture.Simulation.SubmitCommand(
            firstCommand,
            new SimulationTick(1),
            new SimulationCommandSource(
                Owner.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            nearest,
            fixture.Simulation.Entities
                .GetComponent<ResupplyOrder>(
                    first).Provider);

        var secondCommand =
            new ResupplyCommand(
                Owner,
                [second],
                fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(
            secondCommand,
            new SimulationTick(2),
            new SimulationCommandSource(
                Owner.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            alternate,
            fixture.Simulation.Entities
                .GetComponent<ResupplyOrder>(
                    second).Provider);

        ResupplyPlanningResult result =
            fixture.Result(
                second);
        Assert.Equal(
            2,
            result.FriendlyProviders);
        Assert.Equal(
            1,
            result.RejectedProviders);
        Assert.True(
            result.Rejections.HasFlag(
                ResupplyProviderRejection.ProviderBusy));
    }

    [Fact]
    public void HighPriorityCombatRecipientPrefersMobileProviderOverCloserStaticProvider()
    {
        var fixture =
            new Fixture();
        EntityId mobile =
            fixture.Truck(
                new Vector3(
                    20.0f,
                    0.0f,
                    0.0f),
                movementFuel: 80.0,
                cargoFuel: 40.0,
                Owner);
        fixture.StaticProvider(
            new Vector3(
                80.0f,
                0.0f,
                0.0f),
            cargoFuel: 40.0);
        EntityId recipient =
            fixture.CombatRecipient(
                new Vector3(
                    100.0f,
                    0.0f,
                    0.0f),
                BattlefieldSupplyPriority.High);

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            mobile,
            fixture.Result(
                recipient).SelectedProvider);
        Assert.Equal(
            mobile,
            fixture.Simulation.Entities
                .GetComponent<ResupplyOrder>(
                    recipient).Provider);
        Assert.False(
            fixture.Simulation.Entities.HasComponent<MovementOrder>(
                recipient));
        Assert.True(
            fixture.Simulation.Entities.HasComponent<MovementOrder>(
                mobile));
        Assert.Equal(
            recipient,
            fixture.Simulation.Entities
                .GetComponent<SupplyRescueAssignment>(
                    mobile).Recipient);
    }

    [Fact]
    public void NormalPriorityCombatRecipientKeepsNearestProviderSelection()
    {
        var fixture =
            new Fixture();
        fixture.Truck(
            new Vector3(
                20.0f,
                0.0f,
                0.0f),
            movementFuel: 80.0,
            cargoFuel: 40.0,
            Owner);
        EntityId nearest =
            fixture.StaticProvider(
                new Vector3(
                    80.0f,
                    0.0f,
                    0.0f),
                cargoFuel: 40.0);
        EntityId recipient =
            fixture.CombatRecipient(
                new Vector3(
                    100.0f,
                    0.0f,
                    0.0f),
                BattlefieldSupplyPriority.Normal);

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(
            nearest,
            fixture.Result(
                recipient).SelectedProvider);
        Assert.Equal(
            nearest,
            fixture.Simulation.Entities
                .GetComponent<ResupplyOrder>(
                    recipient).Provider);
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

        fixture.Simulation.AdvanceOneTick();

        ResupplyPlanningResult result =
            fixture.Result(recipient);

        Assert.Equal(
            provider,
            result.SelectedProvider);
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

        public EntityId StaticProvider(
            Vector3 position,
            double cargoFuel)
        {
            InventoryId inventory =
                Inventories.CreateInventory(
                    new InventorySpecification(
                        100.0));

            Assert.True(
                Inventories.Add(
                    inventory,
                    ResourceIds.Fuel,
                    cargoFuel).Succeeded);

            EntityId entity =
                Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            Simulation.Entities.AddComponent(
                entity,
                new SupplyProvider(
                    inventory,
                    Owner,
                    resupplyRangeMeters: 12.0f));

            return entity;
        }

        public EntityId CombatRecipient(
            Vector3 position,
            BattlefieldSupplyPriority priority)
        {
            EntityId entity =
                MobileRecipient(
                    position);
            UnitFuelState fuel =
                Simulation.Entities.GetComponent<UnitFuelState>(
                    entity);

            Assert.True(
                Inventories.Add(
                    fuel.InventoryId,
                    ResourceIds.Fuel,
                    4.0).Succeeded);

            Simulation.Entities.AddComponent(
                entity,
                new Combatant(
                    new FactionId(
                        checked((uint)Owner.Value))));
            Simulation.Entities.AddComponent(
                entity,
                HealthState.Full(
                    100.0));
            Simulation.Entities.AddComponent(
                entity,
                new UnitSupplyPriority(
                    priority));
            Simulation.Entities.AddComponent(
                entity,
                new AutomaticResupplyPolicy(
                    ammunitionThreshold: 0.2,
                    fuelThreshold: 0.55,
                    enabled: true));

            return entity;
        }

        public EntityId MobileRecipient(
            Vector3 position)
        {
            EntityId entity =
                Simulation.Entities.CreateEntity();
            InventoryId inventory =
                Inventories.CreateInventory(
                    new InventorySpecification(
                        20.0));

            Assert.True(
                Inventories.Add(
                    inventory,
                    ResourceIds.Fuel,
                    6.0).Succeeded);

            Simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            Simulation.Entities.AddComponent(
                entity,
                new ControllableEntity(
                    Owner,
                    ControllableEntityCategory.Unit));
            Simulation.Entities.AddComponent(
                entity,
                new UnitFuelState(
                    inventory,
                    20.0,
                    0.1));
            Simulation.Entities.AddComponent(
                entity,
                GroundMovement.CreateDefault());
            Simulation.Entities.AddComponent(
                entity,
                GroundMovementState.Stationary());
            Simulation.Entities.AddComponent(
                entity,
                new SupplyMovementConstraint(
                    1.0f,
                    canMove: true));

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
