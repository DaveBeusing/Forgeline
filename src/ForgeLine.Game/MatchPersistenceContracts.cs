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

public sealed record PersistedMatchConfiguration(
    MatchScenarioSettings Scenario,
    ulong Seed,
    IReadOnlyList<MatchParticipantConfiguration> Participants,
    bool EnableDiagnostics,
    bool EnableDebugCapture,
    bool EnableSpatialQueryTiming,
    int InitialEntityCapacity,
    string CompositionKey)
{
    public static PersistedMatchConfiguration Capture(
        MatchRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PersistedMatchConfiguration(
            settings.Scenario,
            settings.Seed,
            settings.Participants.ToArray(),
            settings.EnableDiagnostics,
            settings.EnableDebugCapture,
            settings.EnableSpatialQueryTiming,
            settings.InitialEntityCapacity,
            settings.Composition.Key);
    }

    public MatchRuntimeSettings CreateHeadlessRuntimeSettings(
        Func<string, MatchComposition>? resolveComposition = null)
    {
        ArgumentNullException.ThrowIfNull(Scenario);
        ArgumentNullException.ThrowIfNull(Participants);

        var settings =
            new MatchRuntimeSettings
            {
                Composition = (resolveComposition ?? ResolveBuiltin)(CompositionKey),
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
                    MatchSchedulerOwnership.None
            };

        if (!string.Equals(settings.Composition.Key, CompositionKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Resolved composition does not match saved identity.");
        settings.Validate();
        return settings;
    }
    private static MatchComposition ResolveBuiltin(string key) =>
        key == CentralDivideScenario.CompositionKey
            ? CentralDivideScenario.CreateComposition()
            : throw new InvalidOperationException($"Unknown match composition: {key}. Supply its content resolver.");
}

public sealed record MatchSaveData(
    int SchemaVersion,
    PersistedMatchConfiguration Configuration,
    ulong SavedTick,
    ulong RandomState,
    IReadOnlyList<RecordedSimulationCommand> Commands,
    MatchAuthoritativeSnapshot State,
    string StateSha256);

public sealed record MatchReplayData(
    int SchemaVersion,
    PersistedMatchConfiguration Configuration,
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
