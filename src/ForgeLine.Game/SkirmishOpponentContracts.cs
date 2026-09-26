using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum SkirmishStrategicState : byte
{
    Bootstrap = 1,
    Recovering = 2,
    Expanding = 3,
    Mobilizing = 4,
    Defending = 5,
    Attacking = 6,
    Resupplying = 7
}

public enum SkirmishStrategicGoal : byte
{
    EstablishPower = 1,
    SecureResources = 2,
    EstablishStorage = 3,
    EstablishIndustry = 4,
    EstablishProduction = 5,
    Expand = 6,
    Scout = 7,
    Defend = 8,
    PrepareOffensive = 9,
    AttackObjective = 10,
    RecoverEconomy = 11,
    RecoverSupply = 12
}

public sealed record SkirmishOpponentConfiguration
{
    public uint ReactionCadenceTicks { get; init; } = 20;

    public double Aggression { get; init; } = 0.65;

    public double ExpansionReadinessThreshold { get; init; } = 0.55;

    public double OffensiveReadinessThreshold { get; init; } = 0.68;

    public double RetreatThreshold { get; init; } = 0.28;

    public double ResupplyThreshold { get; init; } = 0.30;

    public int MinimumAttackUnits { get; init; } = 5;

    public int MaximumAttackUnits { get; init; } = 12;

    public int MaximumQueuedUnitsPerFacility { get; init; } = 2;

    public float DefensiveRadiusMeters { get; init; } = 520.0f;

    public float ObjectivePressureLeashMeters { get; init; } = 220.0f;

    public uint ArtilleryCadenceTicks { get; init; } = 80;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfZero(ReactionCadenceTicks);
        ValidateFraction(Aggression, nameof(Aggression));
        ValidateFraction(
            ExpansionReadinessThreshold,
            nameof(ExpansionReadinessThreshold));
        ValidateFraction(
            OffensiveReadinessThreshold,
            nameof(OffensiveReadinessThreshold));
        ValidateFraction(
            RetreatThreshold,
            nameof(RetreatThreshold));
        ValidateFraction(
            ResupplyThreshold,
            nameof(ResupplyThreshold));

        if (RetreatThreshold >= OffensiveReadinessThreshold)
        {
            throw new InvalidOperationException(
                "Retreat threshold must remain below offensive readiness.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            MinimumAttackUnits,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumAttackUnits,
            MinimumAttackUnits);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumQueuedUnitsPerFacility,
            1);
        ArgumentOutOfRangeException.ThrowIfZero(
            ArtilleryCadenceTicks);

        if (!float.IsFinite(DefensiveRadiusMeters) ||
            DefensiveRadiusMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefensiveRadiusMeters));
        }

        if (!float.IsFinite(ObjectivePressureLeashMeters) ||
            ObjectivePressureLeashMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ObjectivePressureLeashMeters));
        }
    }

    private static void ValidateFraction(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0.0 ||
            value > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }
}

public readonly record struct SkirmishOpponentController
{
    public SkirmishOpponentController(
        PlayerId player,
        FactionId faction,
        EntityId preferredConstructionSource,
        Vector3 homePosition)
    {
        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        if (!faction.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(faction));
        }

        if (!preferredConstructionSource.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredConstructionSource));
        }

        if (!IsFinite(homePosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(homePosition));
        }

        Player = player;
        Faction = faction;
        PreferredConstructionSource =
            preferredConstructionSource;
        HomePosition = homePosition;
    }

    public PlayerId Player { get; }

    public FactionId Faction { get; }

    public EntityId PreferredConstructionSource { get; }

    public Vector3 HomePosition { get; }


    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct SkirmishOpponentState(
    SkirmishStrategicState StrategicState,
    SkirmishStrategicGoal ActiveGoal,
    SimulationTick LastDecisionTick,
    SimulationTick LastArtilleryTick,
    int ExpansionSiteCursor,
    int ScoutSiteCursor,
    int DecisionsTaken)
{
    public static SkirmishOpponentState Initial =>
        new(
            SkirmishStrategicState.Bootstrap,
            SkirmishStrategicGoal.EstablishPower,
            SimulationTick.Zero,
            SimulationTick.Zero,
            ExpansionSiteCursor: 0,
            ScoutSiteCursor: 0,
            DecisionsTaken: 0);
}

public readonly record struct SkirmishEconomyAssessment(
    double FerrousOre,
    double Volatiles,
    double Silicates,
    double Steel,
    double Fuel,
    double Electronics,
    double Ammunition,
    double PowerGeneration,
    double PowerDemand,
    int OfflineConsumers,
    int ActiveConstructionSites,
    int ProductionFacilities,
    int UnitProductionFacilities,
    double HealthScore)
{
    public bool PowerConstrained =>
        PowerDemand > 0.0 &&
        PowerGeneration < PowerDemand;

    public bool RawResourceConstrained =>
        FerrousOre < 120.0 ||
        Volatiles < 80.0 ||
        Silicates < 80.0;
}

public readonly record struct SkirmishForceAssessment(
    int TotalUnits,
    int CombatUnits,
    int Scouts,
    int Tanks,
    int Artillery,
    int CargoTrucks,
    int SupplyTrucks,
    double AverageReadiness,
    double MinimumSupply,
    int KnownHostileContacts,
    int CurrentHostileContacts);

public readonly record struct SkirmishOpponentDebugReadModel(
    EntityId Controller,
    PlayerId Player,
    SkirmishStrategicState StrategicState,
    SkirmishStrategicGoal ActiveGoal,
    SkirmishEconomyAssessment Economy,
    SkirmishForceAssessment Force,
    Vector3 HomePosition,
    Vector3 ChosenObjective,
    bool HasChosenObjective,
    SimulationTick LastDecisionTick,
    int DecisionsTaken);
