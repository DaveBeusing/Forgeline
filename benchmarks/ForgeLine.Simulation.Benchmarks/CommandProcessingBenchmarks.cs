using BenchmarkDotNet.Attributes;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class CommandProcessingBenchmarks
{
    [Params(1_000, 10_000)]
    public int CommandCount { get; set; }

    [Benchmark]
    public int ScheduleAndExecuteCommands()
    {
        var simulation = new SimulationCoordinator();
        var counter = new Counter();

        for (int index = 0; index < CommandCount; index++)
        {
            simulation.SubmitCommand(
                new IncrementCommand(counter),
                new SimulationTick((ulong)index + 1));
        }

        simulation.RunTicks((ulong)CommandCount);
        return counter.Value;
    }

    private sealed class IncrementCommand : ISimulationCommand
    {
        private readonly Counter _counter;

        public IncrementCommand(Counter counter)
        {
            _counter = counter;
        }

        public void Execute(SimulationContext context)
        {
            _counter.Value++;
        }
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
