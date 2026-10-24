using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record SkirmishRaidConfiguration
{
    public bool Enabled { get; init; } = true;
    public int MinimumUnits { get; init; } = 2;
    public int MaximumUnits { get; init; } = 3;
    public int HomeDefenders { get; init; } = 2;
    public int MaximumContacts { get; init; } = 32;
    public uint ActiveTicks { get; init; } = 1_200;
    public uint CooldownTicks { get; init; } = 600;
    public uint SearchTicks { get; init; } = 80;
    public float MaximumDistanceMeters { get; init; } = 1_000;
    public float PursuitLeashMeters { get; init; } = 120;
    public float ThreatRadiusMeters { get; init; } = 160;
    public int MaximumKnownThreat { get; init; } = 1;
    public double ReadinessThreshold { get; init; } = 0.65;
    public double FuelMargin { get; init; } = 1.25;
    public double FuelReserveFraction { get; init; } = 0.15;
    public int MinimumShots { get; init; } = 6;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumUnits, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumUnits, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumUnits, MinimumUnits);
        ArgumentOutOfRangeException.ThrowIfLessThan(HomeDefenders, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumContacts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumContacts, 32);
        ArgumentOutOfRangeException.ThrowIfZero(ActiveTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ActiveTicks, 1_200U);
        ArgumentOutOfRangeException.ThrowIfZero(CooldownTicks);
        ArgumentOutOfRangeException.ThrowIfZero(SearchTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumKnownThreat);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumShots, 3);
        if (!float.IsFinite(MaximumDistanceMeters) || MaximumDistanceMeters <= 0 ||
            !float.IsFinite(PursuitLeashMeters) || PursuitLeashMeters <= 0 ||
            !float.IsFinite(ThreatRadiusMeters) || ThreatRadiusMeters <= 0 ||
            !double.IsFinite(ReadinessThreshold) || ReadinessThreshold is < 0 or > 1 ||
            !double.IsFinite(FuelMargin) || FuelMargin < 1 ||
            !double.IsFinite(FuelReserveFraction) || FuelReserveFraction is < 0 or > 1)
            throw new InvalidOperationException("Invalid raid range or supply reserve.");
    }
}

public enum SkirmishRaidPhase : byte
{
    None, Approaching, Attacking, Searching, Returning, Resupplying, Complete
}

public enum SkirmishRaidReason : byte
{
    None, NoKnownTarget, NoEligibleForce, Cooldown, Priority, UnknownRoute,
    FuelReserve, Ammunition, KnownDefense, ContactLost, TargetEscaped,
    DurationExpired, UnitsLost, OrdersReplaced, Returned, RouteLost
}

/// <summary>Bounded simulation intent. All identifiers include their full entity generation.</summary>
public readonly record struct SkirmishRaidState(
    SkirmishRaidPhase Phase,
    SkirmishRaidReason Reason,
    IntelligenceContactKey Contact,
    EntityId Target,
    Vector3 LastKnownPosition,
    Vector3 TargetAnchor,
    Vector3 ReturnPoint,
    SimulationTick StartedAtTick,
    SimulationTick LastSeenTick,
    SimulationTick NextAllowedTick,
    EntityId Group,
    EntityId First,
    EntityId Second,
    EntityId Third)
{
    public Vector3 OrderedPosition { get; init; }

    public int ContactCursor { get; init; }

    public bool IsActive => Phase is not (SkirmishRaidPhase.None or SkirmishRaidPhase.Complete);
    public bool Contains(EntityId entity) => IsActive &&
        (entity == First || entity == Second || entity == Third);
}

public readonly record struct SkirmishRaidReadModel(
    SkirmishRaidState State, SkirmishRaidReason AdmissionReason, int ContactsConsidered);
