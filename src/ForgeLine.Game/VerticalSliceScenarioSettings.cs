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
            EastOpponent = new SkirmishOpponentConfiguration()
        };

    private static VerticalSliceScenarioSettings CreateValidation()
    {
        var opponent =
            new SkirmishOpponentConfiguration
            {
                ReactionCadenceTicks = 10,
                Aggression = 0.82,
                ExpansionReadinessThreshold = 0.42,
                OffensiveReadinessThreshold = 0.56,
                RetreatThreshold = 0.22,
                ResupplyThreshold = 0.24,
                MinimumAttackUnits = 3,
                MaximumAttackUnits = 8,
                MaximumQueuedUnitsPerFacility = 2,
                DefensiveRadiusMeters = 600.0f,
                ObjectivePressureLeashMeters = 260.0f,
                ArtilleryCadenceTicks = 60
            };

        return new VerticalSliceScenarioSettings
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
            WestOpponent = opponent,
            EastOpponent = opponent
        };
    }
}
