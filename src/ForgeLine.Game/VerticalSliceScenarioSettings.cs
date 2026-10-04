namespace ForgeLine.Game;

public enum VerticalSliceScenarioProfile : byte
{
    Gameplay = 1,
    Validation = 2
}

public sealed record VerticalSliceScenarioSettings
{
    public required VerticalSliceScenarioProfile Profile { get; init; }

    public required SkirmishStartingStock StartingStock { get; init; }

    public required SkirmishOpponentConfiguration WestOpponent { get; init; }

    public required SkirmishOpponentConfiguration EastOpponent { get; init; }

    public required float NavigationCellSizeMeters { get; init; }

    public required int NavigationSectorSizeCells { get; init; }

    public required ulong DistributionRetryDelayTicks { get; init; }

    public required uint DistributionMaximumTransportAttempts { get; init; }

    public required ulong DistributionFairnessAgingTicks { get; init; }

    public void Validate()
    {
        WestOpponent.Validate();
        EastOpponent.Validate();

        if (!float.IsFinite(NavigationCellSizeMeters) ||
            NavigationCellSizeMeters <= 0.0f)
        {
            throw new InvalidOperationException(
                "Vertical-slice navigation cell size must be finite and greater than zero.");
        }

        if (NavigationSectorSizeCells < 1)
        {
            throw new InvalidOperationException(
                "Vertical-slice navigation sector size must be at least one cell.");
        }

        if (DistributionRetryDelayTicks == 0)
        {
            throw new InvalidOperationException(
                "Vertical-slice distribution retry delay must be greater than zero.");
        }

        if (DistributionMaximumTransportAttempts == 0)
        {
            throw new InvalidOperationException(
                "Vertical-slice distribution attempt count must be greater than zero.");
        }

        if (DistributionFairnessAgingTicks == 0)
        {
            throw new InvalidOperationException(
                "Vertical-slice distribution fairness aging must be greater than zero.");
        }
    }

    public static VerticalSliceScenarioSettings Create(
        VerticalSliceScenarioProfile profile) =>
        profile switch
        {
            VerticalSliceScenarioProfile.Gameplay =>
                CreateGameplay(),
            VerticalSliceScenarioProfile.Validation =>
                CreateValidation(),
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(profile))
        };

    private static VerticalSliceScenarioSettings CreateGameplay() =>
        new()
        {
            Profile = VerticalSliceScenarioProfile.Gameplay,
            StartingStock = SkirmishStartingStock.Standard,
            WestOpponent = new SkirmishOpponentConfiguration(),
            EastOpponent = new SkirmishOpponentConfiguration(),
            NavigationCellSizeMeters = 16.0f,
            NavigationSectorSizeCells = 8,
            DistributionRetryDelayTicks = 20,
            DistributionMaximumTransportAttempts = 4,
            DistributionFairnessAgingTicks = 200
        };

    private static VerticalSliceScenarioSettings CreateValidation() =>
        new()
        {
            Profile = VerticalSliceScenarioProfile.Validation,
            StartingStock =
                new SkirmishStartingStock(
                    FerrousOre: 1_200.0,
                    Volatiles: 6_000.0,
                    Silicates: 800.0,
                    Steel: 3_000.0,
                    Fuel: 18_000.0,
                    Electronics: 1_500.0,
                    Ammunition: 1_500.0),
            WestOpponent =
                new SkirmishOpponentConfiguration
                {
                    ReactionCadenceTicks = 10,
                    Aggression = 1.0,
                    ExpansionReadinessThreshold = 0.40,
                    OffensiveReadinessThreshold = 0.45,
                    RetreatThreshold = 0.15,
                    ResupplyThreshold = 0.18,
                    OffensiveFuelThreshold = 0.40,
                    MinimumAttackUnits = 3,
                    MaximumAttackUnits = 12,
                    MinimumObjectivePressureUnits = 1,
                    MaximumQueuedUnitsPerFacility = 3,
                    MinimumCargoTrucks = 2,
                    MinimumSupplyTrucks = 2,
                    DefensiveRadiusMeters = 450.0f,
                    ObjectivePressureLeashMeters = 120.0f,
                    ArtilleryCadenceTicks = 50
                },
            EastOpponent =
                new SkirmishOpponentConfiguration
                {
                    ReactionCadenceTicks = 10,
                    Aggression = 0.55,
                    ExpansionReadinessThreshold = 0.42,
                    OffensiveReadinessThreshold = 0.58,
                    RetreatThreshold = 0.22,
                    ResupplyThreshold = 0.22,
                    OffensiveFuelThreshold = 0.46,
                    MinimumAttackUnits = 3,
                    MaximumAttackUnits = 8,
                    MinimumObjectivePressureUnits = 2,
                    MaximumQueuedUnitsPerFacility = 2,
                    MinimumCargoTrucks = 2,
                    MinimumSupplyTrucks = 2,
                    DefensiveRadiusMeters = 600.0f,
                    ObjectivePressureLeashMeters = 240.0f,
                    ArtilleryCadenceTicks = 60
                },
            NavigationCellSizeMeters = 32.0f,
            NavigationSectorSizeCells = 4,
            DistributionRetryDelayTicks = 10,
            DistributionMaximumTransportAttempts = 8,
            DistributionFairnessAgingTicks = 100
        };
}
