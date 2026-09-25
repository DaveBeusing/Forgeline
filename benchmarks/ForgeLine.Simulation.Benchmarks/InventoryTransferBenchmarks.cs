using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class InventoryTransferBenchmarks
{
    private InventoryStore _store = null!;
    private InventoryId _source;
    private InventoryId _destination;

    [Params(100, 1_000, 10_000)]
    public int TransferCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _store = new InventoryStore();
        double capacity = TransferCount + 1.0;
        var specification =
            new InventorySpecification(
                capacity,
                [ResourceIds.FerrousOre]);

        _source = _store.CreateInventory(specification);
        _destination = _store.CreateInventory(specification);

        EnsureSucceeded(
            _store.Add(
                _source,
                ResourceIds.FerrousOre,
                TransferCount));
        EnsureSucceeded(
            _store.Add(
                _destination,
                ResourceIds.FerrousOre,
                1.0));
        EnsureSucceeded(
            _store.Transfer(
                _destination,
                _source,
                ResourceIds.FerrousOre,
                1.0));
    }

    [Benchmark]
    public void RoundTripTransfers()
    {
        for (int index = 0; index < TransferCount; index++)
        {
            EnsureSucceeded(
                _store.Transfer(
                    _source,
                    _destination,
                    ResourceIds.FerrousOre,
                    1.0));
        }

        for (int index = 0; index < TransferCount; index++)
        {
            EnsureSucceeded(
                _store.Transfer(
                    _destination,
                    _source,
                    ResourceIds.FerrousOre,
                    1.0));
        }
    }

    private static void EnsureSucceeded(InventoryOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Inventory benchmark operation failed: {result.Failure}.");
        }
    }
}
