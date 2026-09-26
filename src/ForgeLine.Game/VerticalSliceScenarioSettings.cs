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
            NavigationSectorSizeCells = 8
        };

    private static VerticalSliceScenarioSettings CreateValidation() =>
        new()
        {
            Profile = VerticalSliceScenarioProfile.Validation,
            StartingStock =
                new SkirmishStartingStock(
                    FerrousOre: 1_200.0,
                    Volatiles: 800.0,
                    Silicates: 800.0,
                    Steel: 3_000.0,
                    Fuel: 2_000.0,
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
                    MinimumAttackUnits = 3,
                    MaximumAttackUnits = 12,
                    MaximumQueuedUnitsPerFacility = 3,
                    DefensiveRadiusMeters = 450.0f,
                    ObjectivePressureLeashMeters = 320.0f,
                    ArtilleryCadenceTicks = 50
                },
            EastOpponent =
                new SkirmishOpponentConfiguration
                {
                    ReactionCadenceTicks = 20,
                    Aggression = 0.0,
                    ExpansionReadinessThreshold = 0.70,
                    OffensiveReadinessThreshold = 0.95,
                    RetreatThreshold = 0.35,
                    ResupplyThreshold = 0.40,
                    MinimumAttackUnits = 24,
                    MaximumAttackUnits = 24,
                    MaximumQueuedUnitsPerFacility = 2,
                    DefensiveRadiusMeters = 260.0f,
                    ObjectivePressureLeashMeters = 180.0f,
                    ArtilleryCadenceTicks = 120
                },
            NavigationCellSizeMeters = 32.0f,
            NavigationSectorSizeCells = 4
        };
}
