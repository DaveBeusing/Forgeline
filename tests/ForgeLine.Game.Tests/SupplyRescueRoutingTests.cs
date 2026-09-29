using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SupplyRescueRoutingTests
{
    private static readonly PlayerId Owner = new(1);
    private static readonly Vector3 Start = new(4.0f, 0.0f, 4.0f);
    private static readonly Vector3 RecipientPosition = new(60.0f, 0.0f, 4.0f);
    private static readonly AxisAlignedBounds Barrier = new(
        new Vector3(24.0f, -1.0f, 0.0f), new Vector3(32.0f, 4.0f, 24.0f));

    [Fact]
    public void DetourBeyondPropulsionBudgetIsRejectedBeforeAnyMovement()
    {
        var fixture = new Fixture([Barrier]);
        EntityId truck = fixture.Truck(Start, 4.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(3, TestContext.Current.CancellationToken);

        SupplyRescueRejection rejection = fixture.Simulation.Entities.GetComponent<SupplyRescueRejection>(truck);
        Assert.Equal("RouteFuelInsufficient", rejection.Reason);
        Assert.Equal(recipient, rejection.Recipient);
        Assert.True(rejection.RequiredFuel > rejection.AvailableFuel);
        Assert.True((56.0 - 12.0) * 0.08 < rejection.AvailableFuel);
        Assert.Equal(Start, fixture.Simulation.Entities.GetComponent<WorldTransform>(truck).Position);
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.Equal(1UL, fixture.Navigation.LastDiagnostics.QueuedPathCount);
        fixture.AssertStock(truck, 4.0, 40.0);

        fixture.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.Equal(1UL, fixture.Navigation.LastDiagnostics.QueuedPathCount);
    }

    [Fact]
    public void RejectedDetourAllowsAnotherProviderWithoutDuplicateAssignments()
    {
        var fixture = new Fixture([Barrier]);
        EntityId nearest = fixture.Truck(Start, 4.0);
        EntityId alternate = fixture.Truck(new Vector3(4.0f, 0.0f, 28.0f), 80.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(4, TestContext.Current.CancellationToken);

        Assert.Equal(alternate, fixture.Simulation.Entities.GetComponent<ResupplyOrder>(recipient).Provider);
        Assert.False(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(nearest));
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(nearest));
        Assert.Equal(recipient, fixture.Simulation.Entities.GetComponent<SupplyRescueAssignment>(alternate).Recipient);
        Assert.True(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(alternate));
        Assert.Equal(1, fixture.Simulation.Entities.GetComponentCount<SupplyRescueAssignment>());
        fixture.AssertStock(nearest, 4.0, 40.0);
        fixture.AssertStock(alternate, 80.0, 40.0);
    }

    [Fact]
    public void TopologyChangeRevalidatesReplacementRouteBeforeTravel()
    {
        var fixture = new Fixture([], dynamicInfrastructure: true);
        EntityId truck = fixture.Truck(Start, 4.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        NavigationVersion original = fixture.Simulation.Entities.GetComponent<NavigationRouteState>(truck).Path.Version;

        EntityId obstacle = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(obstacle,
            new WorldTransform(new Vector3(28.0f, 0.0f, 12.0f), Quaternion.Identity, Vector3.One));
        fixture.Simulation.Entities.AddComponent(obstacle,
            new SpatialPresence(new Vector3(4.0f, 4.0f, 12.0f),
                new SpatialEntryMetadata(1, 0, SpatialMobility.Static)));
        fixture.Simulation.RunTicks(3, TestContext.Current.CancellationToken);

        Assert.NotEqual(original, fixture.Navigation.World.Version);
        Assert.Equal("RouteFuelInsufficient",
            fixture.Simulation.Entities.GetComponent<SupplyRescueRejection>(truck).Reason);
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.False(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(truck));
        fixture.AssertStock(truck, 4.0, 40.0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedRecipientOrCanceledOrderReleasesRescueMovement(bool destroyRecipient)
    {
        var fixture = new Fixture([]);
        EntityId truck = fixture.Truck(Start, 80.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.True(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(truck));

        if (destroyRecipient)
        {
            Assert.True(fixture.Simulation.Entities.DestroyEntity(recipient));
        }
        else
        {
            fixture.Simulation.Entities.RemoveComponent<ResupplyOrder>(recipient);
            fixture.Simulation.Entities.RemoveComponent<AutomaticResupplyPolicy>(recipient);
        }

        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(truck));
        fixture.AssertStock(truck, 80.0, 40.0);
    }

    [Fact]
    public void NewMovementOrderSurvivesCleanupOfOldRescue()
    {
        var fixture = new Fixture([]);
        EntityId truck = fixture.Truck(Start, 80.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Vector3 destination = new(4.0f, 0.0f, 28.0f);
        fixture.Simulation.SubmitCommand(
            new MoveEntitiesCommand(Owner, [truck], destination, fixture.Simulation.CurrentTick),
            fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.Equal(destination,
            fixture.Simulation.Entities.GetComponent<NavigationPendingPath>(truck).OriginalOrder.WorldTarget);
        fixture.AssertStock(truck, 80.0, 40.0);
    }

    [Fact]
    public void LostProviderSelectsAnotherLiveProvider()
    {
        var fixture = new Fixture([]);
        EntityId first = fixture.Truck(Start, 80.0);
        EntityId alternate = fixture.Truck(new Vector3(4.0f, 0.0f, 28.0f), 80.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(first, fixture.Simulation.Entities.GetComponent<ResupplyOrder>(recipient).Provider);
        Assert.True(fixture.Simulation.Entities.DestroyEntity(first));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(alternate, fixture.Simulation.Entities.GetComponent<ResupplyOrder>(recipient).Provider);
        Assert.Equal(1, fixture.Simulation.Entities.GetComponentCount<SupplyRescueAssignment>());
        fixture.AssertStock(alternate, 80.0, 40.0);
    }

    [Fact]
    public void DepletedPropulsionFuelCancelsExistingTripWithoutDrainingCargo()
    {
        var fixture = new Fixture([]);
        EntityId truck = fixture.Truck(Start, 80.0);
        EntityId recipient = fixture.Recipient();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        UnitFuelState fuel = fixture.Simulation.Entities.GetComponent<UnitFuelState>(truck);
        Assert.True(fixture.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, 80.0).Succeeded);
        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.False(fixture.Simulation.Entities.HasComponent<SupplyRescueAssignment>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(truck));
        fixture.AssertStock(truck, 0.0, 40.0);
    }

    private sealed class Fixture
    {
        public Fixture(AxisAlignedBounds[] obstacles, bool dynamicInfrastructure = false)
        {
            var settings = new WorldGridSettings { ChunkSizeMeters = 32.0f, HeightSamplesPerSide = 9 };
            var chunks = new List<TerrainChunk>();
            for (int x = 0; x < 2; x++)
            {
                chunks.Add(new TerrainChunk(new ChunkCoordinate(x, 0),
                    new TerrainHeightfield(settings.HeightSamplesPerSide, settings.ChunkSizeMeters,
                        new float[settings.HeightSamplesPerSide * settings.HeightSamplesPerSide])));
            }

            var terrain = new TerrainWorld(settings, chunks);
            var grid = new NavigationGridSettings { CellSizeMeters = 4.0f };
            var sectors = new NavigationSectorSettings { SectorSizeCells = 4 };
            var network = new LogisticsNetwork();
            Navigation = new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(NavigationWorld.Build(terrain, obstacles, grid, sectors)));
            if (dynamicInfrastructure)
            {
                Simulation.RegisterSystem(new StrategicInfrastructureSystem(
                    network, terrain, Navigation, obstacles, grid, sectors));
            }

            Simulation.RegisterSystem(new AutomaticResupplyDecisionSystem(Inventories));
            Simulation.RegisterSystem(Navigation);
        }

        public SimulationCoordinator Simulation { get; } = new();
        public InventoryStore Inventories { get; } = new();
        public HierarchicalNavigationSystem Navigation { get; }

        public EntityId Truck(Vector3 position, double movementFuel)
        {
            var cargo = new CargoTransportSystem(new LogisticsNetwork(), Inventories);
            EntityId entity = SupplyTruckFactory.Create(Simulation.Entities, Inventories, position, Owner, cargo);
            UnitFuelState fuel = Simulation.Entities.GetComponent<UnitFuelState>(entity);
            Assert.True(Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity - movementFuel).Succeeded);
            SupplyTruck truck = Simulation.Entities.GetComponent<SupplyTruck>(entity);
            Assert.True(Inventories.Add(truck.InventoryId, ResourceIds.Fuel, 40.0).Succeeded);
            return entity;
        }

        public EntityId Recipient()
        {
            EntityId entity = Simulation.Entities.CreateEntity();
            InventoryId inventory = Inventories.CreateInventory(new InventorySpecification(20.0));
            Simulation.Entities.AddComponent(entity, new WorldTransform(RecipientPosition, Quaternion.Identity, Vector3.One));
            Simulation.Entities.AddComponent(entity, new ControllableEntity(Owner, ControllableEntityCategory.Unit));
            Simulation.Entities.AddComponent(entity, new UnitFuelState(inventory, 20.0, 0.1));
            Simulation.Entities.AddComponent(entity, new SupplyMovementConstraint(0.0f, canMove: false));
            Simulation.Entities.AddComponent(entity, new AutomaticResupplyPolicy(0.2, 0.2));
            return entity;
        }

        public void AssertStock(EntityId truck, double movementFuel, double cargoFuel)
        {
            UnitFuelState fuel = Simulation.Entities.GetComponent<UnitFuelState>(truck);
            SupplyTruck supply = Simulation.Entities.GetComponent<SupplyTruck>(truck);
            Assert.Equal(movementFuel, Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), precision: 6);
            Assert.Equal(cargoFuel, Inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), precision: 6);
        }
    }
}
