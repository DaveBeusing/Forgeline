using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum CombatOrderKind : byte
{
    Attack = 0,
    AttackMove = 1,
    Stop = 2,
    HoldPosition = 3,
    Retreat = 4
}

public enum CombatOrderStatus : byte
{
    Pending = 0,
    Advancing = 1,
    Engaging = 2,
    Pursuing = 3,
    Holding = 4,
    WaitingForIntelligence = 5,
    Retreating = 6,
    Resupplying = 7,
    Complete = 8,
    Cancelled = 9
}

public readonly record struct CombatOrderState
{
    public CombatOrderState(
        CombatOrderKind kind,
        PlayerId issuer,
        EntityId explicitTarget,
        Vector3 destination,
        bool hasDestination,
        Vector3 anchorPosition,
        float pursuitLeashMeters,
        FormationTemplate formation,
        SimulationTick submittedAtTick,
        SimulationTick acceptedAtTick)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!IsFinite(destination) ||
            !IsFinite(anchorPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (!float.IsFinite(pursuitLeashMeters) ||
            pursuitLeashMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pursuitLeashMeters));
        }

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        if (kind == CombatOrderKind.Attack &&
            !explicitTarget.IsValid)
        {
            throw new ArgumentException(
                "Attack orders require an explicit target.",
                nameof(explicitTarget));
        }

        if (kind is
                CombatOrderKind.AttackMove or
                CombatOrderKind.Retreat &&
            !hasDestination)
        {
            throw new ArgumentException(
                "Movement-oriented combat orders require a destination.",
                nameof(hasDestination));
        }

        Kind = kind;
        Issuer = issuer;
        ExplicitTarget = explicitTarget;
        Destination = destination;
        HasDestination = hasDestination;
        AnchorPosition = anchorPosition;
        PursuitLeashMeters = pursuitLeashMeters;
        Formation = formation;
        SubmittedAtTick = submittedAtTick;
        AcceptedAtTick = acceptedAtTick;
    }

    public CombatOrderKind Kind { get; }

    public PlayerId Issuer { get; }

    public EntityId ExplicitTarget { get; }

    public Vector3 Destination { get; }

    public bool HasDestination { get; }

    public Vector3 AnchorPosition { get; }

    public float PursuitLeashMeters { get; }

    public FormationTemplate Formation { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick AcceptedAtTick { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct TacticalCombatState(
    CombatOrderStatus Status,
    EntityId AssignedTarget,
    Vector3 LastLegitimateTargetPosition,
    bool HasLastLegitimateTargetPosition,
    bool MovementPausedForCombat,
    bool ResupplyRequested,
    SimulationTick UpdatedAtTick);

public readonly record struct TacticalMovementConstraint(bool CanMove);

public readonly record struct CombatGroupMember
{
    public CombatGroupMember(EntityId group)
    {
        if (!group.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }

        Group = group;
    }

    public EntityId Group { get; }
}

public readonly record struct CombatGroupIntent
{
    public CombatGroupIntent(
        CombatOrderKind kind,
        PlayerId issuer,
        Vector3 destination,
        bool hasDestination,
        EntityId explicitTarget,
        FormationTemplate formation,
        int initialMemberCount,
        float pursuitLeashMeters,
        SimulationTick acceptedAtTick)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!float.IsFinite(destination.X) ||
            !float.IsFinite(destination.Y) ||
            !float.IsFinite(destination.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (!Enum.IsDefined(formation))
        {
            throw new ArgumentOutOfRangeException(nameof(formation));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            initialMemberCount,
            1);

        if (!float.IsFinite(pursuitLeashMeters) ||
            pursuitLeashMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pursuitLeashMeters));
        }

        Kind = kind;
        Issuer = issuer;
        Destination = destination;
        HasDestination = hasDestination;
        ExplicitTarget = explicitTarget;
        Formation = formation;
        InitialMemberCount = initialMemberCount;
        PursuitLeashMeters = pursuitLeashMeters;
        AcceptedAtTick = acceptedAtTick;
    }

    public CombatOrderKind Kind { get; }

    public PlayerId Issuer { get; }

    public Vector3 Destination { get; }

    public bool HasDestination { get; }

    public EntityId ExplicitTarget { get; }

    public FormationTemplate Formation { get; }

    public int InitialMemberCount { get; }

    public float PursuitLeashMeters { get; }

    public SimulationTick AcceptedAtTick { get; }
}

public readonly record struct AutomaticResupplyPolicy
{
    public AutomaticResupplyPolicy(
        double ammunitionThreshold = 0.2,
        double fuelThreshold = 0.2,
        bool enabled = true)
    {
        ValidateThreshold(
            ammunitionThreshold,
            nameof(ammunitionThreshold));
        ValidateThreshold(
            fuelThreshold,
            nameof(fuelThreshold));

        AmmunitionThreshold = ammunitionThreshold;
        FuelThreshold = fuelThreshold;
        Enabled = enabled;
    }

    public double AmmunitionThreshold { get; }

    public double FuelThreshold { get; }

    public bool Enabled { get; }

    private static void ValidateThreshold(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0.0 ||
            value > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public readonly record struct UnitCombatReadiness(
    double Strength,
    double Health,
    double Fuel,
    double Ammunition,
    double Mobility,
    double WeaponAvailability,
    double SupplyCondition,
    double CombatCapability,
    double OverallReadiness,
    SimulationTick UpdatedAtTick);

public readonly record struct CombatGroupReadiness(
    double Strength,
    double Health,
    double Fuel,
    double Ammunition,
    double Mobility,
    double WeaponAvailability,
    double SupplyCondition,
    double CombatCapability,
    double OverallReadiness,
    int SurvivingMembers,
    int InitialMembers,
    SimulationTick UpdatedAtTick);

public readonly record struct TacticalTestOpponent
{
    public TacticalTestOpponent(
        double resupplyThreshold = 0.2,
        double retreatThreshold = 0.15,
        float engagementLeashMeters = 160.0f)
    {
        ValidateFraction(
            resupplyThreshold,
            nameof(resupplyThreshold));
        ValidateFraction(
            retreatThreshold,
            nameof(retreatThreshold));

        if (!float.IsFinite(engagementLeashMeters) ||
            engagementLeashMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(engagementLeashMeters));
        }

        ResupplyThreshold = resupplyThreshold;
        RetreatThreshold = retreatThreshold;
        EngagementLeashMeters = engagementLeashMeters;
    }

    public double ResupplyThreshold { get; }

    public double RetreatThreshold { get; }

    public float EngagementLeashMeters { get; }

    private static void ValidateFraction(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0.0 ||
            value > 1.0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
