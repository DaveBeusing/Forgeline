using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Benchmarks;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Logistics.Benchmarks;

internal static class PolicyTemplateBenchmark
{
    internal static void Run(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: --policy-templates <report.json> [samples]");
        int samples = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 32;
        var rows = new List<object>();
        foreach (int global in new[] { 256, 2048, 8192 })
        {
            using var fixture = new Fixture(global);
            foreach (int selected in new[] { 1, 8, 32 })
            {
                var selection = fixture.Facilities.Take(selected).ToArray();
                var before = fixture.Capture(selection);
                string digest = Digest(before);
                object measured = ScalabilityMeasurement.Measure("policy-template-selection",
                    "Actual PolicyTemplateSelectionCapture with stable completed-tick context; no renderer or GPU work",
                    () => fixture.Capture(selection).Targets.Count, samples, warmup: 16);
                if (digest != Digest(fixture.Capture(selection))) throw new InvalidOperationException("Capture mutated its facts.");
                rows.Add(new { Selected = selected, GlobalPolicies = global, GlobalRequests = global,
                    Facilities = fixture.Facilities.Length, FactsSha256 = digest, Measurement = measured });
                Console.WriteLine($"Templates: {selected} selected / {global} policies and requests");
            }
        }
        ScalabilityMeasurement.Write(args[0], "Windows CPU; actual selected policy-template capture", rows,
            [typeof(MatchRuntime).Assembly.Location, typeof(PresentationSnapshot).Assembly.Location, typeof(PolicyTemplateBenchmark).Assembly.Location]);
    }

    private static string Digest(PolicyTemplateSelectionSnapshot snapshot) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));

    private sealed class Fixture : IDisposable, ISimulationTickObserver
    {
        private readonly MatchRuntime _runtime;
        private readonly PresentationExtractionContext _extraction;
        private SimulationContext? _context;
        internal EntityId[] Facilities { get; } = new EntityId[256];
        internal Fixture(int count)
        {
            _runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 410) with
                { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) });
            var interaction = new PresentationInteractionState();
            var gateway = new PlayerCommandGateway(_runtime.Simulation, _runtime.Services.BuildingCommands,
                _runtime.BattlefieldRuntime.MatchStateEntity);
            _extraction = new(_runtime, new(1), interaction, gateway);
            _runtime.Simulation.RegisterTickObserver(this);
            _runtime.Simulation.AdvanceOneTick();
            var entities = _runtime.Simulation.Entities;
            for (int i = 0; i < Facilities.Length; i++)
            {
                var entity = entities.CreateEntity(); Facilities[i] = entity;
                var input = _runtime.Inventories.CreateInventory(new InventorySpecification(10000));
                var output = _runtime.Inventories.CreateInventory(new InventorySpecification(10000));
                entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
                entities.AddComponent(entity, new CompletedBuilding(BuildingIds.Smelter, new(1), SimulationTick.Zero));
                entities.AddComponent(entity, HealthState.Full(100));
                entities.AddComponent(entity, new WorldTransform(new(i, 0, 0), Quaternion.Identity, Vector3.One));
                entities.AddComponent(entity, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            }
            for (int i = 0; i < count; i++)
            {
                var facility = Facilities[i % Facilities.Length];
                var policy = entities.CreateEntity();
                entities.AddComponent(policy, new LogisticsStockPolicy(facility, ResourceIds.FerrousOre, 25, 50, 100));
                var request = entities.CreateEntity();
                entities.AddComponent(request, new ProductionRequest(facility, RecipeIds.Steel, ProductionPriority.Normal,
                    ProductionRequestMode.Repeat, SimulationTick.Zero));
            }
        }
        public void OnTickCompleted(SimulationContext context) => _context = context;
        internal PolicyTemplateSelectionSnapshot Capture(EntityId[] selection) =>
            PolicyTemplateSelectionCapture.Capture(_context ?? throw new InvalidOperationException("Missing completed tick"), _extraction, selection);
        public void Dispose() { _runtime.Dispose(); GC.SuppressFinalize(this); }
    }
}
