using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class LogisticsNavigationRecoveryTests
{
    [Fact]
    public void WorldChangeDuringPendingSearchRetainsLoadedTruckIntent()
    {
        var fixture = Create();
        fixture.Simulation.AdvanceOneTick();
        fixture.Navigation.UpdateWorld(CreateWorld(2));
        fixture.Simulation.AdvanceOneTick();

        NavigationPendingPath pending = fixture.Simulation.Entities.GetComponent<NavigationPendingPath>(fixture.Truck);
        Assert.Equal(new NavigationVersion(2), pending.Request.NavigationVersion);
        Assert.Equal(fixture.Order.WorldTarget, pending.OriginalOrder.WorldTarget);
        Assert.Equal(fixture.Order.SubmittedAtTick, pending.OriginalOrder.SubmittedAtTick);
        Assert.Equal(2UL, fixture.Navigation.LastDiagnostics.QueuedPathCount);
        fixture.Simulation.AdvanceOneTick();
        NavigationRouteState route = fixture.Simulation.Entities.GetComponent<NavigationRouteState>(fixture.Truck);
        Assert.Equal(new NavigationVersion(2), route.Path.Version);
        Assert.Equal(fixture.Order.WorldTarget, route.OriginalOrder.WorldTarget);
        Assert.Equal(40, fixture.Inventories.GetQuantity(fixture.Cargo, ResourceIds.Steel));
    }

    [Fact]
    public void RepeatedWorldChangesScheduleAtMostOneReplacementPerTick()
    {
        var fixture = Create();
        fixture.Simulation.AdvanceOneTick();
        for (ulong version = 2; version <= 6; version++)
        {
            fixture.Navigation.UpdateWorld(CreateWorld(version));
            fixture.Simulation.AdvanceOneTick();
            Assert.Equal(version, fixture.Navigation.LastDiagnostics.QueuedPathCount);
            Assert.Equal(1, fixture.Navigation.LastDiagnostics.PendingPathCount);
        }
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(new NavigationVersion(6), fixture.Simulation.Entities
            .GetComponent<NavigationRouteState>(fixture.Truck).Path.Version);
    }

    [Fact]
    public void SameTickReplacementWinsOverAnEarlierPendingRequest()
    {
        var fixture = Create();
        fixture.Simulation.AdvanceOneTick();
        var replacement = fixture.Order with { WorldTarget = new Vector3(10, 0, 106) };
        fixture.Simulation.Entities.AddComponent(fixture.Truck, replacement);
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(replacement, fixture.Simulation.Entities
            .GetComponent<NavigationPendingPath>(fixture.Truck).OriginalOrder);
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(replacement, fixture.Simulation.Entities
            .GetComponent<NavigationRouteState>(fixture.Truck).OriginalOrder);
    }

    [Theory]
    [InlineData(MovementOrderKind.Strategic)]
    [InlineData(MovementOrderKind.LocalApproach)]
    public void WorldChangeCannotResurrectAReplacedActiveRoute(MovementOrderKind kind)
    {
        var fixture = Create();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        var replacement = fixture.Order with
        {
            WorldTarget = new Vector3(10, 0, 106),
            AcceptedAtTick = new SimulationTick(3),
            Kind = kind
        };
        fixture.Simulation.Entities.SetComponent(fixture.Truck, replacement);
        fixture.Navigation.UpdateWorld(CreateWorld(2));
        fixture.Simulation.AdvanceOneTick();
        if (kind == MovementOrderKind.LocalApproach)
        {
            Assert.Equal(replacement, fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck));
            Assert.False(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(fixture.Truck));
        }
        else
        {
            Assert.Equal(replacement, fixture.Simulation.Entities
                .GetComponent<NavigationPendingPath>(fixture.Truck).OriginalOrder);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleResultsNeverResurrectCanceledOrDestroyedTrucks(bool destroy)
    {
        var fixture = Create();
        fixture.Simulation.AdvanceOneTick();
        if (destroy) Assert.True(fixture.Simulation.Entities.DestroyEntity(fixture.Truck));
        else fixture.Simulation.Entities.RemoveComponent<NavigationPendingPath>(fixture.Truck);
        fixture.Navigation.UpdateWorld(CreateWorld(2));
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(1UL, fixture.Navigation.LastDiagnostics.QueuedPathCount);
        Assert.Equal(0, fixture.Navigation.LastDiagnostics.PendingPathCount);
        Assert.Equal(0, fixture.Navigation.LastDiagnostics.ActiveRouteCount);
        if (!destroy) Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(fixture.Truck));
    }

    [Fact]
    public void NearbyStuckFinalWaypointDoesNotCountAsArrival()
    {
        var fixture = Create();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        NavigationRouteState route = fixture.Simulation.Entities.GetComponent<NavigationRouteState>(fixture.Truck);
        Assert.Single(route.Path.Waypoints);
        GroundMovement movement = fixture.Simulation.Entities.GetComponent<GroundMovement>(fixture.Truck);
        WorldTransform transform = fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Truck);
        fixture.Simulation.Entities.SetComponent(fixture.Truck, transform with
        {
            Position = fixture.Order.WorldTarget - Vector3.UnitX * (movement.StopRadius + movement.Radius)
        });
        SetStuck(fixture);
        fixture.Simulation.AdvanceOneTick();
        Assert.True(fixture.Simulation.Entities.HasComponent<NavigationPendingPath>(fixture.Truck));
        Assert.Equal(fixture.Order.WorldTarget, fixture.Simulation.Entities
            .GetComponent<NavigationPendingPath>(fixture.Truck).OriginalOrder.WorldTarget);
        Assert.Equal(40, fixture.Inventories.GetQuantity(fixture.Cargo, ResourceIds.Steel));
    }

    [Fact]
    public void StuckRouteReplacementKeepsRescueOrderOwnershipAndFuelBudget()
    {
        var fixture = Create();
        fixture.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        var assignment = new SupplyRescueAssignment(fixture.Simulation.Entities.CreateEntity(),
            fixture.Order, true, 17, 0.12, fixture.Simulation.CurrentTick);
        fixture.Simulation.Entities.AddComponent(fixture.Truck, assignment);
        SetStuck(fixture);
        fixture.Simulation.AdvanceOneTick();
        var pending = fixture.Simulation.Entities.GetComponent<NavigationPendingPath>(fixture.Truck);
        var updated = fixture.Simulation.Entities.GetComponent<SupplyRescueAssignment>(fixture.Truck);
        Assert.Equal(assignment with { Movement = pending.OriginalOrder }, updated);
        Assert.NotEqual(fixture.Order.AcceptedAtTick, updated.Movement.AcceptedAtTick);
    }

    [Fact]
    public void StalePendingReplacementKeepsRescueOrderOwnershipWithoutCreatingFuel()
    {
        var fixture = Create();
        var assignment = new SupplyRescueAssignment(fixture.Simulation.Entities.CreateEntity(),
            fixture.Order, true, 17, 0.12, fixture.Simulation.CurrentTick);
        fixture.Simulation.Entities.AddComponent(fixture.Truck, assignment);
        fixture.Simulation.AdvanceOneTick();
        fixture.Navigation.UpdateWorld(CreateWorld(2));
        fixture.Simulation.AdvanceOneTick();
        var pending = fixture.Simulation.Entities.GetComponent<NavigationPendingPath>(fixture.Truck);
        Assert.Equal(assignment with { Movement = pending.OriginalOrder },
            fixture.Simulation.Entities.GetComponent<SupplyRescueAssignment>(fixture.Truck));
    }

    private static void SetStuck(Fixture fixture)
    {
        MovementOrder waypoint = fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck);
        var state = fixture.Simulation.Entities.GetComponent<GroundMovementState>(fixture.Truck);
        fixture.Simulation.Entities.SetComponent(fixture.Truck, state with
        {
            Status = GroundMovementStatus.Stuck,
            ObservedOrderTick = waypoint.AcceptedAtTick,
            StalledTicks = GroundMovementSystemOptions.DefaultStuckTickThreshold
        });
    }

    private static Fixture Create()
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(CreateWorld(1)));
        simulation.RegisterSystem(navigation);
        var inventories = new InventoryStore();
        var transport = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories,
            new Vector3(10, 1, 10), new PlayerId(1), transport);
        InventoryId cargo = simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory;
        Assert.True(inventories.Add(cargo, ResourceIds.Steel, 40).Succeeded);
        var order = new MovementOrder(new PlayerId(1), new Vector3(106, 0, 10),
            SimulationTick.Zero, new SimulationTick(1));
        simulation.Entities.AddComponent(truck, order);
        return new Fixture(simulation, navigation, inventories, truck, cargo, order);
    }

    private static NavigationWorld CreateWorld(ulong version)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 128, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 128, new float[17 * 17]))]);
        return NavigationWorld.Build(terrain, gridSettings: new NavigationGridSettings { CellSizeMeters = 4 },
            sectorSettings: new NavigationSectorSettings { SectorSizeCells = 8 },
            version: new NavigationVersion(version));
    }

    private sealed record Fixture(SimulationCoordinator Simulation, HierarchicalNavigationSystem Navigation,
        InventoryStore Inventories, EntityId Truck, InventoryId Cargo, MovementOrder Order);
}
