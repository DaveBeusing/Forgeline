namespace ForgeLine.Economy;

public readonly record struct PowerNetworkId(uint Value) : IComparable<PowerNetworkId>
{
    public static PowerNetworkId None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(PowerNetworkId other) => Value.CompareTo(other.Value);

    public static bool operator <(PowerNetworkId left, PowerNetworkId right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PowerNetworkId left, PowerNetworkId right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PowerNetworkId left, PowerNetworkId right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PowerNetworkId left, PowerNetworkId right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum PowerPriority
{
    Critical = 0,
    Industrial = 1,
    Optional = 2
}

public enum PowerOperationalState
{
    Offline = 0,
    Brownout = 1,
    Powered = 2
}

public enum PowerGeneratorState
{
    Offline = 0,
    Generating = 1
}

public readonly record struct PowerNetworkMembership
{
    public PowerNetworkMembership(PowerNetworkId networkId)
    {
        if (!networkId.IsSpecified)
        {
            throw new ArgumentException(
                "Power network membership requires a valid network ID.",
                nameof(networkId));
        }

        NetworkId = networkId;
    }

    public PowerNetworkId NetworkId { get; }
}

public readonly record struct PowerGenerator
{
    public PowerGenerator(
        double maximumGeneration,
        bool enabled = true,
        PowerGeneratorState state = PowerGeneratorState.Generating)
    {
        if (!double.IsFinite(maximumGeneration) || maximumGeneration <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumGeneration));
        }

        MaximumGeneration = maximumGeneration;
        Enabled = enabled;
        State = enabled ? state : PowerGeneratorState.Offline;
    }

    public double MaximumGeneration { get; }

    public bool Enabled { get; }

    public PowerGeneratorState State { get; }

    public PowerGenerator WithEnabled(bool enabled) =>
        new(
            MaximumGeneration,
            enabled,
            enabled ? PowerGeneratorState.Generating : PowerGeneratorState.Offline);

    internal PowerGenerator WithState(PowerGeneratorState state) =>
        new(MaximumGeneration, Enabled, state);
}

public readonly record struct PowerConsumer
{
    public PowerConsumer(
        double demand,
        PowerPriority priority = PowerPriority.Industrial,
        bool enabled = true,
        double allocatedPower = 0.0,
        PowerOperationalState state = PowerOperationalState.Offline)
    {
        if (!double.IsFinite(demand) || demand <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(demand));
        }

        if (priority is not PowerPriority.Critical
            and not PowerPriority.Industrial
            and not PowerPriority.Optional)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (!double.IsFinite(allocatedPower)
            || allocatedPower < 0.0
            || allocatedPower > demand)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedPower));
        }

        Demand = demand;
        Priority = priority;
        Enabled = enabled;
        AllocatedPower = enabled ? allocatedPower : 0.0;
        State = enabled ? state : PowerOperationalState.Offline;
    }

    public double Demand { get; }

    public PowerPriority Priority { get; }

    public bool Enabled { get; }

    public double AllocatedPower { get; }

    public PowerOperationalState State { get; }

    public double SupplyFraction =>
        Enabled ? AllocatedPower / Demand : 0.0;

    public double OperationalScale => SupplyFraction;

    public PowerConsumer WithEnabled(bool enabled) =>
        enabled
            ? new(
                Demand,
                Priority,
                true,
                0.0,
                PowerOperationalState.Offline)
            : new(
                Demand,
                Priority,
                false,
                0.0,
                PowerOperationalState.Offline);

    internal PowerConsumer WithAllocation(double allocatedPower)
    {
        if (!Enabled)
        {
            return new(
                Demand,
                Priority,
                false,
                0.0,
                PowerOperationalState.Offline);
        }

        double clamped = Math.Clamp(allocatedPower, 0.0, Demand);
        PowerOperationalState state = clamped switch
        {
            <= 0.0 => PowerOperationalState.Offline,
            _ when clamped >= Demand => PowerOperationalState.Powered,
            _ => PowerOperationalState.Brownout
        };

        return new(
            Demand,
            Priority,
            true,
            clamped,
            state);
    }
}
