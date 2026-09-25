using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Intelligence;

public enum IntelligenceState : byte
{
    Unexplored = 0,
    Explored = 1,
    Visible = 2,
    Detected = 3,
    Identified = 4
}

public readonly record struct IntelligenceContactKey(ulong Value)
    : IComparable<IntelligenceContactKey>
{
    public static IntelligenceContactKey None => default;

    public bool IsSpecified => Value != 0;

    public int CompareTo(IntelligenceContactKey other) =>
        Value.CompareTo(other.Value);

    public static IntelligenceContactKey FromEntity(EntityId entity)
    {
        if (!entity.IsValid)
        {
            return None;
        }

        ulong value =
            ((ulong)entity.Generation << 32) |
            entity.Index;

        return new IntelligenceContactKey(value);
    }

    public static bool operator <(
        IntelligenceContactKey left,
        IntelligenceContactKey right) =>
        left.Value < right.Value;

    public static bool operator <=(
        IntelligenceContactKey left,
        IntelligenceContactKey right) =>
        left.Value <= right.Value;

    public static bool operator >(
        IntelligenceContactKey left,
        IntelligenceContactKey right) =>
        left.Value > right.Value;

    public static bool operator >=(
        IntelligenceContactKey left,
        IntelligenceContactKey right) =>
        left.Value >= right.Value;
}

public readonly record struct VisibilityCellCoordinate(
    int X,
    int Z) : IComparable<VisibilityCellCoordinate>
{
    public int CompareTo(VisibilityCellCoordinate other)
    {
        int zComparison = Z.CompareTo(other.Z);
        return zComparison != 0
            ? zComparison
            : X.CompareTo(other.X);
    }

    public static bool operator <(
        VisibilityCellCoordinate left,
        VisibilityCellCoordinate right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(
        VisibilityCellCoordinate left,
        VisibilityCellCoordinate right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(
        VisibilityCellCoordinate left,
        VisibilityCellCoordinate right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(
        VisibilityCellCoordinate left,
        VisibilityCellCoordinate right) =>
        left.CompareTo(right) >= 0;
}

public sealed record IntelligenceGridSettings
{
    public const float DefaultCellSizeMeters = 32.0f;

    public float CellSizeMeters { get; init; } =
        DefaultCellSizeMeters;

    public void Validate()
    {
        if (!float.IsFinite(CellSizeMeters) ||
            CellSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CellSizeMeters));
        }
    }
}

public readonly record struct VisualSensorState
{
    public VisualSensorState(
        FactionId faction,
        float rangeMeters,
        int updateIntervalTicks = 1)
    {
        ValidateFaction(faction);
        ValidateRange(rangeMeters, nameof(rangeMeters));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            updateIntervalTicks,
            1);

        Faction = faction;
        RangeMeters = rangeMeters;
        UpdateIntervalTicks = updateIntervalTicks;
    }

    public FactionId Faction { get; }

    public float RangeMeters { get; }

    public int UpdateIntervalTicks { get; }

    private static void ValidateFaction(FactionId faction)
    {
        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Sensors require a valid faction.",
                nameof(faction));
        }
    }

    private static void ValidateRange(
        float value,
        string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }
}

public readonly record struct RadarSensorState
{
    public RadarSensorState(
        FactionId faction,
        float detectionRangeMeters,
        float identificationRangeMeters = 0.0f,
        int updateIntervalTicks = 4)
    {
        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Sensors require a valid faction.",
                nameof(faction));
        }

        if (!float.IsFinite(detectionRangeMeters) ||
            detectionRangeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionRangeMeters));
        }

        if (!float.IsFinite(identificationRangeMeters) ||
            identificationRangeMeters < 0.0f ||
            identificationRangeMeters > detectionRangeMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identificationRangeMeters));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            updateIntervalTicks,
            1);

        Faction = faction;
        DetectionRangeMeters = detectionRangeMeters;
        IdentificationRangeMeters = identificationRangeMeters;
        UpdateIntervalTicks = updateIntervalTicks;
    }

    public FactionId Faction { get; }

    public float DetectionRangeMeters { get; }

    public float IdentificationRangeMeters { get; }

    public int UpdateIntervalTicks { get; }
}

public readonly record struct IntelligenceSignature
{
    public IntelligenceSignature(
        FactionId faction,
        uint identityKey,
        bool visualDetectable = true,
        bool radarDetectable = true)
    {
        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Intelligence signatures require a valid faction.",
                nameof(faction));
        }

        Faction = faction;
        IdentityKey = identityKey;
        VisualDetectable = visualDetectable;
        RadarDetectable = radarDetectable;
    }

    public FactionId Faction { get; }

    public uint IdentityKey { get; }

    public bool VisualDetectable { get; }

    public bool RadarDetectable { get; }
}

public readonly record struct IntelligenceContact(
    IntelligenceContactKey ContactKey,
    Vector3 LastKnownPosition,
    IntelligenceState State,
    SimulationTick LastSeenTick,
    uint IdentityKey,
    bool IsCurrent)
{
    public bool IsIdentified =>
        State == IntelligenceState.Identified;
}

public readonly record struct VisibilityCellSnapshot(
    VisibilityCellCoordinate Cell,
    IntelligenceState State);

public sealed class FactionIntelligenceSnapshot
{
    private readonly VisibilityCellSnapshot[] _cells;
    private readonly IntelligenceContact[] _contacts;

    internal FactionIntelligenceSnapshot(
        FactionId faction,
        SimulationTick tick,
        VisibilityCellSnapshot[] cells,
        IntelligenceContact[] contacts)
    {
        Faction = faction;
        Tick = tick;
        _cells = cells;
        _contacts = contacts;
    }

    public FactionId Faction { get; }

    public SimulationTick Tick { get; }

    public IReadOnlyList<VisibilityCellSnapshot> Cells =>
        _cells;

    public IReadOnlyList<IntelligenceContact> Contacts =>
        _contacts;
}
