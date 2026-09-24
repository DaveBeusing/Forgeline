namespace ForgeLine.Simulation;

public readonly record struct SimulationTick(ulong Value) : IComparable<SimulationTick>
{
    public static SimulationTick Zero => default;

    public int CompareTo(SimulationTick other)
    {
        return Value.CompareTo(other.Value);
    }

    public static bool operator <(SimulationTick left, SimulationTick right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(SimulationTick left, SimulationTick right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(SimulationTick left, SimulationTick right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(SimulationTick left, SimulationTick right)
    {
        return left.CompareTo(right) >= 0;
    }

    public SimulationTick Next()
    {
        return new SimulationTick(checked(Value + 1));
    }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
