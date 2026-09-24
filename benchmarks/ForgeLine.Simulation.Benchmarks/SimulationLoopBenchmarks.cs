using BenchmarkDotNet.Attributes;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class SimulationLoopBenchmarks
{
    private SimulationCoordinator _empty = null!;
    private SimulationCoordinator _light = null!;
    private SimulationCoordinator _multiSystem = null!;
    private SimulationCoordinator _thousandEntities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _empty = new SimulationCoordinator();

        _light = new SimulationCoordinator();
        _light.RegisterSystem(new LightSystem());

        _multiSystem = SimulationPerformanceScenario
            .CreateLightweightEntities(entityCount: 1_000, systemCount: 4)
            .Simulation;

        _thousandEntities = SimulationPerformanceScenario
            .CreateLightweightEntities(entityCount: 1_000, systemCount: 1)
            .Simulation;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = 1_000)]
    public ulong EmptyTicks()
    {
        return _empty.RunTicks(1_000);
    }

    [Benchmark(OperationsPerInvoke = 1_000)]
    public ulong LightTicks()
    {
        return _light.RunTicks(1_000);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public ulong ThousandEntityTicks()
    {
        return _thousandEntities.RunTicks(100);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public ulong MultiSystemTicks()
    {
        return _multiSystem.RunTicks(100);
    }

    private sealed class LightSystem : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.Movement;

        public ulong ExecutionCount { get; private set; }

        public void Execute(SimulationContext context)
        {
            ExecutionCount++;
        }
    }
}
