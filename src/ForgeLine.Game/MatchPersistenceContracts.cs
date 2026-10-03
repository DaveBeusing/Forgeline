using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MatchPersistenceDocumentKind : byte
{
    Save = 1,
    Replay = 2
}

public enum MatchPersistenceFailureReason : byte
{
    None = 0,
    CorruptDocument = 1,
    IncompatibleVersion = 2,
    InvalidConfiguration = 3,
    IncompleteCommandHistory = 4,
    UnsupportedCommand = 5,
    StateMismatch = 6,
    RandomStateMismatch = 7
}

public sealed class MatchPersistenceException : Exception
{
    public MatchPersistenceException(
        MatchPersistenceFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public MatchPersistenceFailureReason Reason { get; }
}

public sealed record PersistedVerticalSliceConfiguration(
    VerticalSliceScenarioSettings Scenario,
    ulong Seed,
    IReadOnlyList<MatchParticipantConfiguration> Participants,
    bool EnableDiagnostics,
    bool EnableDebugCapture,
    bool EnableSpatialQueryTiming,
    int InitialEntityCapacity)
{
    public static PersistedVerticalSliceConfiguration Capture(
        VerticalSliceRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PersistedVerticalSliceConfiguration(
            settings.Scenario,
            settings.Seed,
            settings.Participants.ToArray(),
            settings.EnableDiagnostics,
            settings.EnableDebugCapture,
            settings.EnableSpatialQueryTiming,
            settings.InitialEntityCapacity);
    }

    public VerticalSliceRuntimeSettings CreateHeadlessRuntimeSettings()
    {
        ArgumentNullException.ThrowIfNull(Scenario);
        ArgumentNullException.ThrowIfNull(Participants);

        var settings =
            new VerticalSliceRuntimeSettings
            {
                Scenario = Scenario,
                Seed = Seed,
                Participants =
                    Participants.ToArray(),
                EnableDiagnostics =
                    EnableDiagnostics,
                EnableDebugCapture =
                    EnableDebugCapture,
                EnableSpatialQueryTiming =
                    EnableSpatialQueryTiming,
                InitialEntityCapacity =
                    InitialEntityCapacity,
                SchedulerOwnership =
                    VerticalSliceSchedulerOwnership.None
            };

        settings.Validate();
        return settings;
    }
}

public sealed record MatchSaveData(
    int SchemaVersion,
    PersistedVerticalSliceConfiguration Configuration,
    ulong SavedTick,
    ulong RandomState,
    IReadOnlyList<RecordedSimulationCommand> Commands,
    VerticalSliceAuthoritativeSnapshot State,
    string StateSha256);

public sealed record MatchReplayData(
    int SchemaVersion,
    PersistedVerticalSliceConfiguration Configuration,
    ulong FinalTick,
    ulong FinalRandomState,
    IReadOnlyList<RecordedSimulationCommand> Commands,
    MatchState FinalMatchState,
    string FinalStateSha256);

internal sealed record MatchPersistenceEnvelope(
    string Magic,
    int FormatVersion,
    MatchPersistenceDocumentKind Kind,
    string Payload,
    string PayloadSha256);
