namespace ForgeLine.Simulation;

public sealed class SimulationRandom
{
    private ulong _state;

    public SimulationRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong State => _state;

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;

        ulong value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public uint NextUInt32()
    {
        return (uint)(NextUInt64() >> 32);
    }

    public int NextInt32(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveMax),
                exclusiveMax,
                "Exclusive maximum must be greater than zero.");
        }

        return (int)(NextUInt64() % (uint)exclusiveMax);
    }
}
