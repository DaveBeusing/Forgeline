using System.Numerics;
using ForgeLine.Benchmarks;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Logistics.Benchmarks;

internal static class Program
{
    private static readonly ResourceId[] Materials = [ResourceIds.FerrousOre, ResourceIds.Steel, ResourceIds.Electronics];

    public static void Main(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: <report.json> [samples, 16..65536]");
        int samples = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 128;
        var results = new List<object>();
        foreach (int count in new[] { 16, 64 })
        foreach (string mode in new[] { "open", "no-surplus", "no-truck", "no-fuel", "disconnected", "congested" })
        {
            using var fixture = new Fixture(count, mode);
            var before = fixture.Counters();
            object ticks = ScalabilityMeasurement.Measure("completed-tick",
                "Full completed simulation ticks, no renderer or presentation extraction; mixed retry and dispatch ticks",
                fixture.Tick, samples, warmup: 8, counters: fixture.Counters);
            object ui = ScalabilityMeasurement.Measure("selected-action-extraction",
                "Completed-tick action snapshot factory with one selected owned depot; includes construction, technology and logistics facts",
                fixture.Capture, samples, warmup: 8);
            var measured = fixture.Counters();
            if (mode == "open") fixture.VerifyPhysicalArrival();
            results.Add(new { Facilities = count, Sources = Math.Max(4, count / 4),
                ResourcesPerFacility = Materials.Length, Trucks = mode == "no-truck" ? 0 : mode == "open" ? 4 : count / 4,
                Mode = mode, Seed = 403UL, Before = before, Ticks = ticks, Ui = ui,
                Measured = measured, AfterArrivalCheck = fixture.Counters(),
                FinalStateSha256 = MatchAuthoritativeSnapshot.Capture(fixture.Runtime).ComputeSha256() });
            Console.WriteLine($"Refill {count} / {mode} complete.");
        }
        ScalabilityMeasurement.Write(args[0], "Windows CPU only; no graphics resources, waits, Present or GPU claims", results,
            [typeof(AutomatedDistributionSystem).Assembly.Location, typeof(PresentationSnapshot).Assembly.Location,
                typeof(Program).Assembly.Location]);
    }

    private sealed class Fixture : IDisposable, ISimulationTickObserver
    {
        private readonly List<InventoryId> _destinations = [];
        private readonly PresentationInteractionState _interaction = new();
        private readonly PresentationExtractionContext _extraction;
        private SimulationContext? _context;
        internal MatchRuntime Runtime { get; }

        internal Fixture(int count, string mode)
        {
            var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 403) with
                { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
            Runtime = MatchRuntime.Create(settings);
            try
            {
                var entities = Runtime.Simulation.Entities;
                // Isolate the explicit fleet and finite sources before the first simulation tick.
                var originalTrucks = new List<EntityId>();
                foreach (var truck in entities.Query<CargoTransport>(QueryIterationOrder.StableByEntityIndex))
                    originalTrucks.Add(truck);
                foreach (var truck in originalTrucks) entities.DestroyEntity(truck);
                foreach (var storage in entities.Query<InventoryStorage>())
                {
                    var inventory = entities.GetComponent<InventoryStorage>(storage).InventoryId;
                    foreach (var material in Materials)
                    {
                        double available = Runtime.Inventories.GetAvailableQuantity(inventory, material);
                        if (available > 0) Require(Runtime.Inventories.Remove(inventory, material, available).Succeeded);
                    }
                }
                Vector3 home = entities.GetComponent<WorldTransform>(Runtime.GetBase(new PlayerId(1)).CommandCore).Position;
                var sources = new List<(LogisticsNodeId Node, Vector3 Position)>();
                for (int i = 0; i < Math.Max(4, count / 4); i++)
                {
                    var position = At(home + new Vector3(20 + i * 4, 0, 30));
                    var source = Store(position, 1000000, true, out var inventory, out var node);
                    _ = source;
                    foreach (var material in Materials)
                        if (mode != "no-surplus") Require(Runtime.Inventories.Add(inventory, material, 10000).Succeeded);
                    sources.Add((node, position));
                }
                EntityId selected = default;
                for (int i = 0; i < count; i++)
                {
                    var position = At(home + new Vector3(80 + i % 8 * 6, 0, 40 + i / 8 * 6));
                    var target = Store(position, 300, false, out var inventory, out var node);
                    if (i == 0) selected = target;
                    _destinations.Add(inventory);
                    foreach (var material in Materials)
                    {
                        var policy = entities.CreateEntity();
                        entities.AddComponent(policy, new LogisticsStockPolicy(target, material, 25, 50, 100));
                    }
                    if (mode == "disconnected") continue;
                    foreach (var origin in sources)
                    {
                        double distance = Vector3.Distance(origin.Position, position);
                        Runtime.Logistics.AddEdge(origin.Node, node, LogisticsTransportMode.GroundRoad,
                            distance, distance, mode == "congested" ? 1 : 10000);
                    }
                }
                if (mode != "no-truck")
                    for (int i = 0; i < (mode == "open" ? 4 : count / 4); i++)
                    {
                        var truck = CargoTruckFactory.Create(entities, Runtime.Inventories, sources[i % sources.Count].Position,
                            new PlayerId(1), Runtime.CargoTransport);
                        if (mode == "no-fuel")
                        {
                            var fuel = Runtime.Inventories.CreateInventory(new InventorySpecification(20));
                            entities.AddComponent(truck, new UnitFuelState(fuel, 20, .02));
                        }
                    }
                _interaction.SetSelection([selected]);
                var commands = new PlayerCommandGateway(Runtime.Simulation, Runtime.Services.BuildingCommands,
                    Runtime.BattlefieldRuntime.MatchStateEntity);
                _extraction = new(Runtime, new(1), _interaction, commands);
                Runtime.Simulation.RegisterTickObserver(this);
            }
            catch { Runtime.Dispose(); throw; }
        }

        internal ulong Tick()
        {
            Runtime.Simulation.AdvanceOneTick();
            return Runtime.Simulation.CurrentTick.Value;
        }
        public void OnTickCompleted(SimulationContext context) => _context = context;
        internal int Capture() => PlayerActionSnapshotFactory.Capture(
            _context ?? throw new InvalidOperationException("Complete a tick first."), _extraction,
            _interaction.Capture()).Logistics?.Policies.Count ?? 0;
        internal object Counters() => new
        {
            Tick = Runtime.Simulation.CurrentTick.Value,
            Delivered = _destinations.Sum(Runtime.Inventories.GetTotalQuantity),
            Runtime.AutomatedDistribution.Metrics,
            Failures = Runtime.AutomatedDistribution.LastDebugSnapshot.Requests
                .GroupBy(r => r.FailureReason).ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
        internal void VerifyPhysicalArrival()
        {
            for (int i = 0; i < 2000 && _destinations.Sum(Runtime.Inventories.GetTotalQuantity) == 0; i++) Tick();
            if (_destinations.Sum(Runtime.Inventories.GetTotalQuantity) <= 0)
                throw new InvalidOperationException("Open workload never delivered physical cargo: " + System.Text.Json.JsonSerializer.Serialize(Counters()));
        }
        private EntityId Store(Vector3 position, double capacity, bool source, out InventoryId inventory, out LogisticsNodeId node)
        {
            inventory = Runtime.Inventories.CreateInventory(new InventorySpecification(capacity));
            var entity = Runtime.Simulation.Entities.CreateEntity();
            Runtime.Simulation.Entities.AddComponent(entity, new InventoryStorage(inventory));
            Runtime.Simulation.Entities.AddComponent(entity, new StorageDepot(inventory, new FactionId(1)));
            Runtime.Simulation.Entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            Runtime.Simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
            node = Runtime.Logistics.AddNode(entity, position, LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination | LogisticsNodeCapabilities.Storage |
                (source ? LogisticsNodeCapabilities.CargoSource : 0));
            return entity;
        }
        private Vector3 At(Vector3 position)
        {
            if (!Runtime.Terrain.TrySampleHeight(position.X, position.Z, out float height))
                throw new InvalidOperationException("Fixture position outside terrain.");
            return new(position.X, height, position.Z);
        }
        private static void Require(bool success)
        {
            if (!success) throw new InvalidOperationException("Finite fixture inventory operation failed.");
        }
        public void Dispose() { Runtime.Dispose(); GC.SuppressFinalize(this); }
    }
}
