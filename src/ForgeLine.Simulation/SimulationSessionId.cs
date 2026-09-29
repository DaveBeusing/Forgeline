namespace ForgeLine.Simulation;

public readonly record struct SimulationSessionId(ulong Value)
{
    public static SimulationSessionId None => default;

    public bool IsSpecified => Value != 0;

    internal static SimulationSessionId Allocate()
    {
        long next = Interlocked.Increment(ref SessionSequence.Next);

        if (next <= 0)
        {
            throw new OverflowException(
                "Simulation session identifier space has been exhausted.");
        }

        return new SimulationSessionId(
            checked((ulong)next));
    }

    private static class SessionSequence
    {
        internal static long Next;
    }
}
