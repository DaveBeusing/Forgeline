using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed class PresentationSnapshot
{
    private readonly RenderInstance[] _instances;

    public PresentationSnapshot(
        SimulationTick tick,
        TimeSpan tickDuration,
        int simulationEntityCount,
        ReadOnlySpan<RenderInstance> instances,
        FactionIntelligenceSnapshot? intelligence = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            tickDuration,
            TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfNegative(simulationEntityCount);

        Tick = tick;
        TickDuration = tickDuration;
        SimulationEntityCount = simulationEntityCount;
        Intelligence = intelligence;
        _instances = instances.ToArray();
    }

    public SimulationTick Tick { get; }

    public TimeSpan TickDuration { get; }

    public int SimulationEntityCount { get; }

    public FactionIntelligenceSnapshot? Intelligence { get; }

    public int InstanceCount => _instances.Length;

    public ReadOnlySpan<RenderInstance> Instances => _instances;

    internal RenderInstance GetInstance(int index) => _instances[index];
}
