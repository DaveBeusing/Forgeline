using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class ProductionBenchmarks
{
    private static readonly PowerNetworkId Network = new(1);

    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000, 5_000)]
    public int FacilityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var inventories = new InventoryStore();
        var production = new ProductionSystem(
            InitialProductionRecipes.CreateCatalog(),
            inventories);
        _simulation = new SimulationCoordinator(
            initialEntityCapacity: checked(FacilityCount * 2 + 16));

        _simulation.RegisterSystem(new PowerNetworkSystem());
        _simulation.RegisterSystem(production);

        EntityId generator = _simulation.Entities.CreateEntity();
        _simulation.Entities.AddComponent(
            generator,
            new PowerNetworkMembership(Network));
        _simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(FacilityCount * 10.0));

        for (int index = 0; index < FacilityCount; index++)
        {
            InventoryId input =
                inventories.CreateInventory(
                    new InventorySpecification(1_000_000_000.0));
            InventoryId output =
                inventories.CreateInventory(
                    new InventorySpecification(1_000_000_000.0));

            InventoryOperationResult add =
                inventories.Add(
                    input,
                    ResourceIds.FerrousOre,
                    100_000_000.0);
            if (!add.Succeeded)
            {
                throw new InvalidOperationException(
                    "Production benchmark input initialization failed.");
            }

            EntityId facility = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                facility,
                new PowerNetworkMembership(Network));
            _simulation.Entities.AddComponent(
                facility,
                new PowerConsumer(
                    demand: 10.0,
                    PowerPriority.Industrial));
            _simulation.Entities.AddComponent(
                facility,
                new ProductionFacility(
                    input,
                    output,
                    ProductionCapability.SteelProcessing,
                    SimulationTick.Zero));

            EntityId request = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                request,
                new ProductionRequest(
                    facility,
                    RecipeIds.Steel,
                    ProductionPriority.Normal,
                    ProductionRequestMode.Repeat,
                    SimulationTick.Zero));
        }
    }

    [Benchmark]
    public void AdvanceProductionTick()
    {
        _simulation.AdvanceOneTick();
    }
}
