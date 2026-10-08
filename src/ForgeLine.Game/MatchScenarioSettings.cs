namespace ForgeLine.Game;

public enum MatchScenarioProfile : byte
{
    Gameplay = 1,
    Validation = 2
}

public sealed record MatchScenarioSettings
{
    public required MatchScenarioProfile Profile { get; init; }

    public required SkirmishStartingStock StartingStock { get; init; }

    public required IReadOnlyDictionary<ulong, SkirmishOpponentConfiguration> OpponentConfigurations { get; init; }

    public required float NavigationCellSizeMeters { get; init; }

    public required int NavigationSectorSizeCells { get; init; }

    public required ulong DistributionRetryDelayTicks { get; init; }

    public required uint DistributionMaximumTransportAttempts { get; init; }

    public required ulong DistributionFairnessAgingTicks { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Profile))
        {
            throw new InvalidOperationException("Match profile is invalid.");
        }
        ArgumentNullException.ThrowIfNull(OpponentConfigurations);
        foreach (var pair in OpponentConfigurations)
        {
            if (pair.Key == 0) throw new InvalidOperationException("Opponent player must be specified.");
            pair.Value.Validate();
        }

        if (!float.IsFinite(NavigationCellSizeMeters) ||
            NavigationCellSizeMeters <= 0.0f)
        {
            throw new InvalidOperationException(
                "Match navigation cell size must be finite and greater than zero.");
        }

        if (NavigationSectorSizeCells < 1)
        {
            throw new InvalidOperationException(
                "Match navigation sector size must be at least one cell.");
        }

        if (DistributionRetryDelayTicks == 0)
        {
            throw new InvalidOperationException(
                "Match distribution retry delay must be greater than zero.");
        }

        if (DistributionMaximumTransportAttempts == 0)
        {
            throw new InvalidOperationException(
                "Match distribution attempt count must be greater than zero.");
        }

        if (DistributionFairnessAgingTicks == 0)
        {
            throw new InvalidOperationException(
                "Match distribution fairness aging must be greater than zero.");
        }
    }

}
