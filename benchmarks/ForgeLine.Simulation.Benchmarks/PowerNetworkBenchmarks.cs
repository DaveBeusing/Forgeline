using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class PowerNetworkBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000, 10_000)]
    public int ConsumerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = new SimulationCoordinator(
            initialEntityCapacity: ConsumerCount + 16);

        var powerSystem = new PowerNetworkSystem();
        _simulation.RegisterSystem(powerSystem);

        PowerNetworkId network = new(1);

        EntityId generator = _simulation.Entities.CreateEntity();
        _simulation.Entities.AddComponent(
            generator,
            new PowerNetworkMembership(network));
        _simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(ConsumerCount * 0.75));

        for (int index = 0; index < ConsumerCount; index++)
        {
            EntityId consumer = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                consumer,
                new PowerNetworkMembership(network));
            _simulation.Entities.AddComponent(
                consumer,
                new PowerConsumer(
                    1.0,
                    PowerPriority.Industrial));
        }

        _simulation.AdvanceOneTick();
    }

    [Benchmark]
    public void AdvancePowerAllocationTick()
    {
        _simulation.AdvanceOneTick();
    }
}
