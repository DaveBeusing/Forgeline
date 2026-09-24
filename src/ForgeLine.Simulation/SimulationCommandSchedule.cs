namespace ForgeLine.Simulation;

internal sealed class SimulationCommandSchedule
{
    private readonly SortedDictionary<ulong, List<SimulationCommandEnvelope>> _scheduled = new();
    private ulong _nextSequence = 1;
    private int _pendingCount;

    internal int PendingCount => _pendingCount;

    internal SimulationCommandEnvelope Enqueue(
        SimulationTick targetTick,
        SimulationCommandSource source,
        ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (targetTick == SimulationTick.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetTick),
                targetTick,
                "Commands must target a positive simulation tick.");
        }

        if (!_scheduled.TryGetValue(targetTick.Value, out List<SimulationCommandEnvelope>? commands))
        {
            commands = new List<SimulationCommandEnvelope>();
            _scheduled.Add(targetTick.Value, commands);
        }

        var envelope = new SimulationCommandEnvelope(
            targetTick,
            _nextSequence++,
            source,
            command);

        commands.Add(envelope);
        _pendingCount++;
        return envelope;
    }

    internal int ExecuteForTick(SimulationTick tick, SimulationContext context)
    {
        if (!_scheduled.TryGetValue(tick.Value, out List<SimulationCommandEnvelope>? commands))
        {
            return 0;
        }

        _scheduled.Remove(tick.Value);
        _pendingCount -= commands.Count;

        for (int index = 0; index < commands.Count; index++)
        {
            commands[index].Command.Execute(context);
        }

        return commands.Count;
    }
}
