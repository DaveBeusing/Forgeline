using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class ResourceExtractionBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000, 10_000)]
    public int ExtractorCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = new SimulationCoordinator(
            initialEntityCapacity: checked(ExtractorCount * 2 + 16));
        _simulation.RegisterSystem(new ResourceExtractionSystem());

        for (int index = 0; index < ExtractorCount; index++)
        {
            EntityId deposit = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                deposit,
                new ResourceDeposit(
                    ResourceIds.FerrousOre,
                    new AxisAlignedBounds(
                        new Vector3(index * 2.0f, 0.0f, 0.0f),
                        new Vector3(index * 2.0f + 1.0f, 1.0f, 1.0f)),
                    totalQuantity: 1_000_000_000_000.0,
                    baseExtractionRatePerSecond: 10.0));

            EntityId extractor = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                extractor,
                new ResourceExtractor(
                    deposit,
                    ResourceIds.FerrousOre,
                    maximumExtractionRatePerSecond: 10.0));
        }
    }

    [Benchmark]
    public void AdvanceExtractionTick()
    {
        _simulation.AdvanceOneTick();
    }
}
