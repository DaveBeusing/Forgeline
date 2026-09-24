namespace ForgeLine.Simulation;

public readonly record struct SimulationCommandSource(ulong Value)
{
    public static SimulationCommandSource None => default;

    public bool IsSpecified => Value != 0;

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
