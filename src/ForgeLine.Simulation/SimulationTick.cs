namespace ForgeLine.Simulation;

public readonly record struct SimulationTick(ulong Value) : IComparable<SimulationTick>
{
    public static SimulationTick Zero => default;

    public int CompareTo(SimulationTick other)
    {
        return Value.CompareTo(other.Value);
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
