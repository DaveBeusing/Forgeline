using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public readonly record struct ResourceExtractionResult(
    SimulationTick Tick,
    EntityId Extractor,
    EntityId Deposit,
    ResourceId ResourceId,
    double Quantity);

public interface IResourceExtractionSink
{
    void OnExtracted(in ResourceExtractionResult result);
}

public sealed class NullResourceExtractionSink : IResourceExtractionSink
{
    public static NullResourceExtractionSink Instance { get; } = new();

    private NullResourceExtractionSink()
    {
    }

    public void OnExtracted(in ResourceExtractionResult result)
    {
    }
}
