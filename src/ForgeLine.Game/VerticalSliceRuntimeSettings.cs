using ForgeLine.Core;
using ForgeLine.Jobs;

namespace ForgeLine.Game;

public enum VerticalSliceSchedulerOwnership : byte
{
    None = 0,
    Host = 1,
    Runtime = 2
}

public sealed record VerticalSliceRuntimeSettings
{
    private static readonly PlayerId WestPlayer = new(1);
    private static readonly PlayerId EastPlayer = new(2);

    public required VerticalSliceScenarioSettings Scenario { get; init; }

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

    public VerticalSliceSchedulerOwnership SchedulerOwnership
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
                "Vertical-slice initial entity capacity must be greater than zero.");
        }

        if (Participants.Count != 2)
        {
            throw new InvalidOperationException(
                "The current vertical slice requires exactly two participants.");
        }

        bool hasWest = false;
        bool hasEast = false;
        var players = new HashSet<PlayerId>();
        var starts = new HashSet<int>();

        for (int index = 0; index < Participants.Count; index++)
        {
            MatchParticipantConfiguration participant =
                Participants[index];

            if (!players.Add(participant.Player))
            {
                throw new InvalidOperationException(
                    $"Player {participant.Player} is configured more than once.");
            }

            if (!starts.Add(participant.StartIndex))
            {
                throw new InvalidOperationException(
                    $"Start index {participant.StartIndex} is configured more than once.");
            }

            hasWest |= participant.Player == WestPlayer;
            hasEast |= participant.Player == EastPlayer;
        }

        if (!hasWest || !hasEast)
        {
            throw new InvalidOperationException(
                "The current vertical slice requires player 1 and player 2 assignments.");
        }

        switch (SchedulerOwnership)
        {
            case VerticalSliceSchedulerOwnership.None
                when Scheduler is not null:
                throw new InvalidOperationException(
                    "A scheduler cannot be supplied when runtime scheduler ownership is None.");

            case VerticalSliceSchedulerOwnership.Host
                when Scheduler is null:
                throw new InvalidOperationException(
                    "Host scheduler ownership requires a supplied scheduler.");

            case VerticalSliceSchedulerOwnership.Runtime
                when Scheduler is not null:
                throw new InvalidOperationException(
                    "Runtime scheduler ownership creates its own scheduler; do not supply one.");

            case VerticalSliceSchedulerOwnership.None:
            case VerticalSliceSchedulerOwnership.Host:
            case VerticalSliceSchedulerOwnership.Runtime:
                break;

            default:
                throw new InvalidOperationException(
                    "Vertical-slice scheduler ownership is invalid.");
        }
    }

    public MatchConfiguration CreateMatchConfiguration(
        PrototypeBattlefieldDefinition battlefield)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        Validate();

        var configuration =
            new MatchConfiguration(
                battlefield.Metadata.Key,
                Seed,
                Participants);

        configuration.ValidateAgainst(battlefield);
        return configuration;
    }

    public static VerticalSliceRuntimeSettings CreateHeadless(
        VerticalSliceScenarioProfile profile,
        ulong seed = 17,
        bool enableDiagnostics = false,
        bool enableDebugCapture = false) =>
        new()
        {
            Scenario =
                VerticalSliceScenarioSettings.Create(profile),
            Seed = seed,
            Participants =
                CreateDefaultParticipants(
                    westComputerControlled: true,
                    eastComputerControlled: true),
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = enableDebugCapture,
            EnableSpatialQueryTiming = false,
            SchedulerOwnership =
                VerticalSliceSchedulerOwnership.None
        };

    public static VerticalSliceRuntimeSettings CreateClient(
        JobScheduler scheduler,
        ulong seed = 17,
        bool enableDiagnostics = true) =>
        new()
        {
            Scenario =
                VerticalSliceScenarioSettings.Create(
                    VerticalSliceScenarioProfile.Gameplay),
            Seed = seed,
            Participants =
                CreateDefaultParticipants(
                    westComputerControlled: false,
                    eastComputerControlled: true),
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = false,
            EnableSpatialQueryTiming = true,
            Scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler)),
            SchedulerOwnership =
                VerticalSliceSchedulerOwnership.Host
        };

    public static VerticalSliceRuntimeSettings CreateOwned(
        VerticalSliceScenarioProfile profile,
        ulong seed,
        IReadOnlyList<MatchParticipantConfiguration> participants,
        bool enableDiagnostics = false,
        bool enableDebugCapture = false,
        bool enableSpatialQueryTiming = false) =>
        new()
        {
            Scenario =
                VerticalSliceScenarioSettings.Create(profile),
            Seed = seed,
            Participants = participants,
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = enableDebugCapture,
            EnableSpatialQueryTiming = enableSpatialQueryTiming,
            SchedulerOwnership =
                VerticalSliceSchedulerOwnership.Runtime
        };

    public static IReadOnlyList<MatchParticipantConfiguration>
        CreateDefaultParticipants(
            bool westComputerControlled,
            bool eastComputerControlled) =>
        [
            new MatchParticipantConfiguration(
                WestPlayer,
                new FactionId(1),
                startIndex: 0,
                westComputerControlled),
            new MatchParticipantConfiguration(
                EastPlayer,
                new FactionId(2),
                startIndex: 1,
                eastComputerControlled)
        ];
}
