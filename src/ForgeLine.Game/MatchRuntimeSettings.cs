using ForgeLine.Core;
using ForgeLine.Jobs;

namespace ForgeLine.Game;

public enum MatchSchedulerOwnership : byte
{
    None = 0,
    Host = 1,
    Runtime = 2
}

public sealed record MatchRuntimeSettings
{
    public required MatchComposition Composition { get; init; }

    public required MatchScenarioSettings Scenario { get; init; }

    public required ulong Seed { get; init; }

    public required IReadOnlyList<MatchParticipantConfiguration> Participants
    {
        get;
        init;
    }

    public bool EnableDiagnostics { get; init; }

    public bool EnableDebugCapture { get; init; }

    public bool EnableSpatialQueryTiming { get; init; }

    public int InitialEntityCapacity { get; init; } = 8_192;

    public JobScheduler? Scheduler { get; init; }

    public MatchSchedulerOwnership SchedulerOwnership
    {
        get;
        init;
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scenario);
        ArgumentNullException.ThrowIfNull(Participants);

        Scenario.Validate();

        if (InitialEntityCapacity <= 0)
        {
            throw new InvalidOperationException(
                "Match initial entity capacity must be greater than zero.");
        }

        if (Participants.Count < 2)
        {
            throw new InvalidOperationException("A match requires at least two participants.");
        }
        ArgumentNullException.ThrowIfNull(Composition);
        Composition.Validate();
        foreach (var participant in Participants)
            if (participant.IsComputerControlled && !Scenario.OpponentConfigurations.ContainsKey(participant.Player.Value))
                throw new InvalidOperationException($"No opponent policy for player {participant.Player}.");
        CreateMatchConfiguration(Composition.Battlefield).ValidateAgainst(Composition.Battlefield);

        switch (SchedulerOwnership)
        {
            case MatchSchedulerOwnership.None
                when Scheduler is not null:
                throw new InvalidOperationException(
                    "A scheduler cannot be supplied when runtime scheduler ownership is None.");

            case MatchSchedulerOwnership.Host
                when Scheduler is null:
                throw new InvalidOperationException(
                    "Host scheduler ownership requires a supplied scheduler.");

            case MatchSchedulerOwnership.Runtime
                when Scheduler is not null:
                throw new InvalidOperationException(
                    "Runtime scheduler ownership creates its own scheduler; do not supply one.");

            case MatchSchedulerOwnership.None:
            case MatchSchedulerOwnership.Host:
            case MatchSchedulerOwnership.Runtime:
                break;

            default:
                throw new InvalidOperationException(
                    "Match scheduler ownership is invalid.");
        }
    }

    public MatchConfiguration CreateMatchConfiguration(
        BattlefieldDefinition battlefield)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        var configuration =
            new MatchConfiguration(
                battlefield.Metadata.Key,
                Seed,
                Participants);

        configuration.ValidateAgainst(battlefield);
        return configuration;
    }

}
