namespace ForgeLine.Simulation;

public sealed class FixedTickClock
{
    public const int DefaultTicksPerSecond = 20;

    public FixedTickClock(int ticksPerSecond = DefaultTicksPerSecond)
    {
        if (ticksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerSecond),
                ticksPerSecond,
                "Tick rate must be greater than zero.");
        }

        TicksPerSecond = ticksPerSecond;
        TickDuration = TimeSpan.FromSeconds(1d / ticksPerSecond);
    }

    public int TicksPerSecond { get; }

    public TimeSpan TickDuration { get; }

    public SimulationTick CurrentTick { get; private set; }

    internal SimulationTick Advance()
    {
        CurrentTick = CurrentTick.Next();
        return CurrentTick;
    }
}
