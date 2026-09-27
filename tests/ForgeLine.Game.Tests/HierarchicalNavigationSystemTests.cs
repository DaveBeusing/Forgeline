using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Jobs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class HierarchicalNavigationSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void ScheduledHierarchicalRouteFeedsGroundMovementWaypoints()
    {
        TerrainWorld terrain = CreateFlatWorld();
        NavigationWorld navigationWorld =
            CreateNavigationWorld(terrain);
        var pathfinder =
            new HierarchicalPathfinder(navigationWorld);

        using var scheduler = new JobScheduler(
            new JobSchedulerOptions
            {
                WorkerCount = 1
            });

        var simulation = new SimulationCoordinator(
            ticksPerSecond: 20,
            jobScheduler: scheduler);
        var navigationSystem =
            new HierarchicalNavigationSystem(pathfinder);
        var movementSystem =
            new GroundMovementSystem(terrain);

        EntityId entity = AddUnit(
            simulation,
            new Vector3(4.0f, 0.5f, 4.0f));

        Vector3 destination =
            new(60.0f, 0.0f, 28.0f);

        simulation.Entities.AddComponent(
            entity,
            new MovementOrder(
                LocalPlayer,
                destination,
                SimulationTick.Zero,
                new SimulationTick(1)));

        simulation.RegisterSystem(navigationSystem);
        simulation.RegisterSystem(movementSystem);

        simulation.AdvanceOneTick();

        Assert.True(
            simulation.Entities.HasComponent<
                NavigationPendingPath>(entity));
        Assert.False(
            simulation.Entities.HasComponent<
                MovementOrder>(entity));

        simulation.AdvanceOneTick();

        Assert.True(
            navigationSystem.LastDiagnostics.CompletedPathCount > 0);
        Assert.True(
            scheduler.GetMetrics().SubmittedJobs > 0);

        int safety = 0;
        while ((simulation.Entities.HasComponent<
                    NavigationRouteState>(entity) ||
                simulation.Entities.HasComponent<
                    MovementOrder>(entity)) &&
               safety < 400)
        {
            simulation.AdvanceOneTick();
            safety++;
        }

        Assert.True(safety < 400);

        WorldTransform transform =
            simulation.Entities.GetComponent<WorldTransform>(entity);

        Assert.InRange(
            Vector2.Distance(
                new Vector2(transform.Position.X, transform.Position.Z),
                new Vector2(destination.X, destination.Z)),
            0.0f,
            1.0f);
        Assert.False(
            simulation.Entities.HasComponent<
                NavigationFailureState>(entity));
    }

    [Fact]
    public void NewOrderRejectsCompletedResultFromSupersededRequest()
    {
        TerrainWorld terrain = CreateFlatWorld();
        NavigationWorld navigationWorld =
            CreateNavigationWorld(terrain);

        using var scheduler = new JobScheduler(
            new JobSchedulerOptions
            {
                WorkerCount = 1
            });

        var simulation = new SimulationCoordinator(
            ticksPerSecond: 20,
            jobScheduler: scheduler);
        var navigationSystem =
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(navigationWorld));

        EntityId entity = AddUnit(
            simulation,
            new Vector3(4.0f, 0.5f, 4.0f));

        simulation.Entities.AddComponent(
            entity,
            new MovementOrder(
                LocalPlayer,
                new Vector3(60.0f, 0.0f, 4.0f),
                SimulationTick.Zero,
                new SimulationTick(1)));

        simulation.RegisterSystem(navigationSystem);
        simulation.AdvanceOneTick();

        var newer = new MovementOrder(
            LocalPlayer,
            new Vector3(4.0f, 0.0f, 28.0f),
            new SimulationTick(1),
            new SimulationTick(2));
        simulation.Entities.AddComponent(entity, newer);

        simulation.AdvanceOneTick();

        Assert.True(
            navigationSystem.LastDiagnostics.StaleResultCount > 0);
        Assert.True(
            simulation.Entities.HasComponent<
                NavigationPendingPath>(entity));

        simulation.AdvanceOneTick();

        Assert.True(
            simulation.Entities.TryGetComponent(
                entity,
                out NavigationRouteState route));
        Assert.Equal(
            newer.WorldTarget,
            route.OriginalOrder.WorldTarget);
    }

    [Fact]
    public void NewStaticObstacleReplansActiveRouteAndRemovalReopensCells()
    {
        TerrainWorld terrain = CreateFlatWorld();
        var simulation = new SimulationCoordinator();
        var pathfinder = new HierarchicalPathfinder(CreateNavigationWorld(terrain));
        var navigation = new HierarchicalNavigationSystem(pathfinder);
        var infrastructure = new StrategicInfrastructureSystem(
            new LogisticsNetwork(),
            terrain,
            navigation,
            [],
            new NavigationGridSettings { CellSizeMeters = 4.0f },
            new NavigationSectorSettings { SectorSizeCells = 4 });
        simulation.RegisterSystem(infrastructure);
        simulation.RegisterSystem(navigation);
        EntityId unit = AddUnit(simulation, new Vector3(4.0f, 0.5f, 4.0f));
        simulation.Entities.AddComponent(
            unit,
            new MovementOrder(
                LocalPlayer,
                new Vector3(60.0f, 0.0f, 4.0f),
                SimulationTick.Zero,
                SimulationTick.Zero.Next()));
        simulation.RunTicks(2, TestContext.Current.CancellationToken);
        NavigationRouteState original =
            simulation.Entities.GetComponent<NavigationRouteState>(unit);

        EntityId building = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            building,
            new WorldTransform(
                new Vector3(28.0f, 0.0f, 4.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            building,
            new SpatialPresence(
                new Vector3(4.0f, 4.0f, 4.0f),
                new SpatialEntryMetadata(1, 0, SpatialMobility.Static)));
        simulation.RunTicks(2, TestContext.Current.CancellationToken);

        NavigationRouteState detour =
            simulation.Entities.GetComponent<NavigationRouteState>(unit);
        Assert.NotEqual(original.Path.Version, detour.Path.Version);
        Assert.Equal(navigation.World.Version, detour.Path.Version);
        Assert.True(detour.Path.Diagnostics.RouteLengthMeters > original.Path.Diagnostics.RouteLengthMeters);
        var blockedCell = new NavigationCellCoordinate(7, 1);
        NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Tracked);
        Assert.False(navigation.World.Grid.IsTraversable(blockedCell, capabilities));

        simulation.AdvanceOneTick();
        Assert.Equal(detour.Path.Version, navigation.World.Version);
        Assert.True(simulation.Entities.DestroyEntity(building));
        simulation.AdvanceOneTick();
        Assert.NotEqual(detour.Path.Version, navigation.World.Version);
        Assert.True(navigation.World.Grid.IsTraversable(blockedCell, capabilities));
    }

    [Fact]
    public void ProjectedDestinationFinishesWithLocalApproachToRequestedPoint()
    {
        TerrainWorld terrain = CreateFlatWorld();
        NavigationWorld world = NavigationWorld.Build(
            terrain,
            [new AxisAlignedBounds(new Vector3(28.0f, -1.0f, 12.0f), new Vector3(29.0f, 2.0f, 13.0f))],
            new NavigationGridSettings { CellSizeMeters = 4.0f },
            new NavigationSectorSettings { SectorSizeCells = 4 });
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(new HierarchicalNavigationSystem(new HierarchicalPathfinder(world)));
        simulation.RegisterSystem(new GroundMovementSystem(terrain));
        EntityId unit = AddUnit(simulation, new Vector3(4.0f, 0.5f, 14.0f));
        Vector3 destination = new(30.0f, 0.5f, 14.0f);
        simulation.Entities.AddComponent(unit,
            new MovementOrder(LocalPlayer, destination, SimulationTick.Zero, SimulationTick.Zero.Next()));

        simulation.RunTicks(400, TestContext.Current.CancellationToken);

        Assert.False(simulation.Entities.HasComponent<MovementOrder>(unit));
        Assert.False(simulation.Entities.HasComponent<NavigationRouteState>(unit));
        Assert.InRange(Vector3.Distance(destination,
            simulation.Entities.GetComponent<WorldTransform>(unit).Position), 0.0f, 1.0f);
    }

    private static EntityId AddUnit(
        SimulationCoordinator simulation,
        Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            GroundMovement.CreateDefault(heightOffset: 0.5f));
        simulation.Entities.AddComponent(
            entity,
            GroundMovementState.Stationary());
        simulation.Entities.AddComponent(
            entity,
            new NavigationAgent(
                NavigationMovementClass.Tracked));
        return entity;
    }

    private static NavigationWorld CreateNavigationWorld(
        TerrainWorld terrain)
    {
        return NavigationWorld.Build(
            terrain,
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });
    }

    private static TerrainWorld CreateFlatWorld()
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        var chunks = new List<TerrainChunk>();
        for (int x = 0; x < 2; x++)
        {
            int count =
                settings.HeightSamplesPerSide *
                settings.HeightSamplesPerSide;

            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(x, 0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[count])));
        }

        return new TerrainWorld(settings, chunks);
    }
}
