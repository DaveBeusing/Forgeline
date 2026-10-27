using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

/// <summary>Identical workloads can run against pre-queue and current Game binaries.</summary>
internal static class VehicleServiceMeasurements
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private readonly record struct Counters(ulong Ticks, ulong PathSearches, int ActivePaths, long CompletedDeliveries);
    private readonly record struct Measurement(string Workload, int Participants, int Sample, ulong Ticks,
        double Milliseconds, double MillisecondsPerTick, long AllocatedBytes, ulong PathSearches,
        int ActivePaths, long CompletedDeliveries);

    public static void Run(string output)
    {
        var results = new List<Measurement>();
        void Record(Measurement measurement)
        {
            results.Add(measurement);
            Console.WriteLine(measurement);
        }
        for (int sample = 0; sample < 3; sample++)
        {
            foreach (int count in new[] { 100, 500 })
            {
                using var original = new CargoTransportBenchmarks { TransportCount = count };
                original.Setup();
                Record(Measure("original-cargo", count, sample, () =>
                {
                    long completed = original.RunCargoTransportBatch();
                    var navigation = original.NavigationDiagnostics;
                    return new(original.MeasuredTicks, navigation.QueuedPathCount, navigation.ActiveRouteCount + navigation.PendingPathCount, completed);
                }));
                using var fleet = new Fleet(count);
                Record(Measure("shared-building-service", count, sample, fleet.Run));
            }
            var movement = new GroundMovementBenchmarks();
            movement.Setup();
            movement.ResetScenario();
            Record(Measure("original-movement", 1000, sample,
                () => new(movement.MoveOneThousandEntitiesForTenTicks(), 0, 0, 0)));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Schema = 1,
            GameVersion = typeof(MatchRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Runtime = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Description = "Three fresh samples; setup excluded; elapsed time and all-thread allocation; original bounded benchmarks retained; shared service observes 2000 ticks without a completion promise.",
            Measurements = results
        }, s_json));
    }

    private static Measurement Measure(string name, int count, int sample, Func<Counters> action)
    {
        long allocation = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        var counters = action();
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return new(name, count, sample, counters.Ticks, elapsed, elapsed / Math.Max(1UL, counters.Ticks),
            GC.GetTotalAllocatedBytes(precise: true) - allocation, counters.PathSearches, counters.ActivePaths, counters.CompletedDeliveries);
    }

    private sealed class Fleet : IDisposable
    {
        private readonly SimulationCoordinator _simulation = new(ticksPerSecond: 20);
        private readonly CargoTransportSystem _cargo;
        private readonly HierarchicalNavigationSystem _navigation;

        public Fleet(int count)
        {
            var terrain = new TerrainWorld(new WorldGridSettings { ChunkSizeMeters = 2048, HeightSamplesPerSide = 3 },
                [new TerrainChunk(new(0, 0), new TerrainHeightfield(3, 2048, new float[9]))]);
            var spatial = new SpatialGridIndex();
            var inventories = new InventoryStore();
            var network = new LogisticsNetwork();
            var obstacles = new List<AxisAlignedBounds>();
            var source = Building(new(1200, 0, 1024));
            var destination = Building(new(1600, 0, 1024));
            _navigation = new(new HierarchicalPathfinder(NavigationWorld.Build(terrain, obstacles,
                new NavigationGridSettings { CellSizeMeters = 16 }, new NavigationSectorSettings { SectorSizeCells = 8 })));
            try
            {
                _cargo = new(network, inventories, _navigation, new VehicleDockingService(terrain, spatial, _navigation));
                _simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
                _simulation.RegisterSystem(_navigation);
                _simulation.RegisterSystem(new GroundMovementSystem(terrain, spatial));
                _simulation.RegisterSystem(_cargo);
                if (!inventories.Add(source.Inventory, ResourceIds.Steel, count * 20).Succeeded)
                    throw new InvalidOperationException("Unable to seed finite fleet inventory.");
                var origin = network.AddNode(source.Entity, new(1200, 0, 1024), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
                var target = network.AddNode(destination.Entity, new(1600, 0, 1024), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
                network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 400, 400, count * 20);
                var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck];
                var profile = new CargoTruckDefinition(definition.Key, definition.CargoCapacity, definition.Movement, definition.VisualScale, definition.VisualId);
                for (int index = 0; index < count; index++)
                {
                    var truck = CargoTruckFactory.Create(_simulation.Entities, inventories,
                        new(1100 - index % 20 * 14, definition.Movement.HeightOffset, 1038.5f + index / 20 * 14), new(1), _cargo, profile);
                    if (!_cargo.TryAssignOrder(_simulation.Entities, truck, new(origin, target, ResourceIds.Steel, 20, SimulationTick.Zero), SimulationTick.Zero))
                        throw new InvalidOperationException("Unable to assign finite fleet delivery.");
                }

            }
            catch
            {
                _navigation.Dispose();
                throw;
            }

            (EntityId Entity, InventoryId Inventory) Building(Vector3 point)
            {
                var entities = _simulation.Entities;
                var entity = entities.CreateEntity();
                var inventory = inventories.CreateInventory(new InventorySpecification(count * 40));
                var transform = new WorldTransform(point, Quaternion.Identity, new(20, 8, 20));
                var presence = new SpatialPresence(new(10, 4, 10), new(1, 2, SpatialMobility.Static));
                entities.AddComponent(entity, transform);
                entities.AddComponent(entity, presence);
                entities.AddComponent(entity, new InventoryStorage(inventory));
                entities.AddComponent(entity, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
                entities.AddComponent(entity, new BuildingLoadingDock(new(0, 0, 14.5f), Vector3.UnitX, 12));
                obstacles.Add(presence.CreateEntry(entity, transform).Bounds);
                return (entity, inventory);
            }
        }

        public void Dispose()
        {
            _navigation.Dispose();
            GC.SuppressFinalize(this);
        }

        public Counters Run()
        {
            _simulation.RunTicks(2000);
            var navigation = _navigation.LastDiagnostics;
            return new(_simulation.CurrentTick.Value, navigation.QueuedPathCount,
                navigation.ActiveRouteCount + navigation.PendingPathCount, _cargo.Metrics.CompletedOrderCount);
        }
    }
}
