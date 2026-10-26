namespace ForgeLine.Game;

public enum DeveloperValueKind : byte { Unspecified, Boolean, SignedNumber, UnsignedNumber, RealNumber }
public readonly record struct DeveloperValue
{
    private readonly double _number;
    private DeveloperValue(DeveloperValueKind kind, double number) { Kind = kind; _number = number; }
    public DeveloperValueKind Kind { get; }
    public bool IsValid => Kind is >= DeveloperValueKind.Boolean and <= DeveloperValueKind.RealNumber && double.IsFinite(_number);
    public static DeveloperValue FromBoolean(bool value) => new(DeveloperValueKind.Boolean, value ? 1 : 0);
    public static DeveloperValue FromInt32(int value) => new(DeveloperValueKind.SignedNumber, value);
    public static DeveloperValue FromUInt32(uint value) => new(DeveloperValueKind.UnsignedNumber, value);
    public static DeveloperValue FromDouble(double value) => double.IsFinite(value)
        ? new(DeveloperValueKind.RealNumber, value) : throw new ArgumentOutOfRangeException(nameof(value));
    public bool AsBoolean() => Require(DeveloperValueKind.Boolean) != 0;
    public int AsInt32() => checked((int)Require(DeveloperValueKind.SignedNumber));
    public uint AsUInt32() => checked((uint)Require(DeveloperValueKind.UnsignedNumber));
    public double AsDouble() => Require(DeveloperValueKind.RealNumber);
    internal double Number => IsValid ? _number : throw new InvalidOperationException("Invalid developer value.");
    private double Require(DeveloperValueKind kind) => Kind == kind && IsValid ? _number :
        throw new InvalidOperationException("Developer values require an explicit matching conversion.");
}

public enum DeveloperApplyMode : byte { Presentation = 1, NextTick, NextMatch }
[Flags]
public enum DeveloperTargetKinds : byte { None = 0, Global = 1, Player = 2, Entity = 4 }
public enum DeveloperParameterId : ushort
{
    None, ConveyorThroughputMultiplier,
    OpponentAggression, OpponentExpansionReadiness, OpponentOffensiveReadiness,
    OpponentRetreat, OpponentResupply, OpponentOffensiveFuel, OpponentReactionCadence,
    RaidEnabled, RaidMinimumUnits, RaidMaximumUnits, RaidHomeDefenders, RaidMaximumContacts,
    RaidActiveTicks, RaidCooldownTicks, RaidSearchTicks, RaidReadiness, RaidFuelMargin,
    RaidFuelReserve, RaidMinimumShots, RaidMaximumKnownThreat,
    RaidHomeRadius, RaidMaximumDistance, RaidPursuitLeash, RaidThreatRadius,
    DistributionRetryDelay, DistributionMaximumAttempts, DistributionFairnessAging
}

/// <summary>Metadata only. Providers additionally validate coupled domain configuration.</summary>
public sealed record DeveloperParameterDescriptor
{
    public DeveloperParameterDescriptor(DeveloperParameterId id, string key, string category, string unit,
        DeveloperValue defaultValue, DeveloperValue minimum, DeveloperValue maximum, DeveloperValue step,
        DeveloperTargetKinds targets, DeveloperApplyMode applyMode)
    {
        if (!Enum.IsDefined(id) || id == DeveloperParameterId.None || string.IsNullOrWhiteSpace(key) ||
            key.Length > 96 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_')) ||
            string.IsNullOrWhiteSpace(category) || category.Length > 48 || string.IsNullOrWhiteSpace(unit) || unit.Length > 32 ||
            !defaultValue.IsValid || minimum.Kind != defaultValue.Kind || maximum.Kind != defaultValue.Kind ||
            step.Kind != defaultValue.Kind || !minimum.IsValid || !maximum.IsValid || !step.IsValid ||
            minimum.Number > defaultValue.Number || maximum.Number < defaultValue.Number || step.Number <= 0 ||
            targets == DeveloperTargetKinds.None || (targets & ~(DeveloperTargetKinds.Global | DeveloperTargetKinds.Player | DeveloperTargetKinds.Entity)) != 0 ||
            !Enum.IsDefined(applyMode)) throw new ArgumentException("Invalid developer parameter descriptor.");
        Id = id; Key = key; Category = category; Unit = unit; Default = defaultValue;
        Minimum = minimum; Maximum = maximum; Step = step; Targets = targets; ApplyMode = applyMode;
    }
    public DeveloperParameterId Id { get; }
    public string Key { get; }
    public string Category { get; }
    public string Unit { get; }
    public DeveloperValueKind Kind => Default.Kind;
    public DeveloperValue Default { get; }
    public DeveloperValue Minimum { get; }
    public DeveloperValue Maximum { get; }
    public DeveloperValue Step { get; }
    public DeveloperTargetKinds Targets { get; }
    public DeveloperApplyMode ApplyMode { get; }
    public bool Accepts(DeveloperValue value, DeveloperTarget target) => value.IsValid && value.Kind == Kind &&
        value.Number >= Minimum.Number && value.Number <= Maximum.Number && target.IsValid && (Targets & target.Kind) != 0;
}
