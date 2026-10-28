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
    RandomStateMismatch = 7,
    DocumentTooLarge = 8
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
    public ScenarioIdentity? Identity { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeveloperInitialProfileData? DeveloperInitialProfile { get; init; }

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
            settings.Composition.Key) { Identity = settings.Composition.Identity };
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
        if (Identity is not null && settings.Composition.Identity != Identity)
            throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion,
                "Resolved map, terrain, content or rules differ from the saved scenario identity.");
        if (Identity is null && CompositionKey == CentralDivideScenario.CompositionKey &&
            settings.Composition.Identity != LegacyCentralDivideIdentity)
            throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion,
                "Legacy Central Divide requires its historical qualified map and content revision.");
        if (Identity is null && CompositionKey != CentralDivideScenario.CompositionKey &&
            ScenarioRegistry.Default.Descriptors.Any(d => d.Key == CompositionKey))
            throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion,
                "Registered scenarios require their saved map and content identity.");
        settings.Validate();
        return settings;
    }
    // Frozen compatibility target for payloads 1–6 with the current authoritative state schema.
    private static ScenarioIdentity LegacyCentralDivideIdentity => new(
        "prototype.vertical_slice", 1, "d60f7ae9d4d6b6771269bd53ccfada847615c1d3f261b87e6ba0223e55f276ef", 1,
        "directorate.v1", "192624b7d2011b4ae6d0858d74d2fdfa9dc0d361c029444bf663f785556ffbd9",
        "directorate.starting-base.v1", "command-core-elimination.v1");

    private static MatchComposition ResolveBuiltin(string key) =>
        ScenarioRegistry.Default.Resolve(key);
}

public sealed record MatchSaveData(
    int SchemaVersion,
    PersistedMatchConfiguration Configuration,
    ulong SavedTick,
    ulong RandomState,
    IReadOnlyList<RecordedSimulationCommand> Commands,
    MatchAuthoritativeSnapshot State,
    string StateSha256)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DeveloperModified { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PracticeSnapshot? PracticeProgress { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PlayerControlGroupSaveData? ControlGroups { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeveloperParameterAuditData[]? ParameterChanges { get; init; }
}

public sealed record MatchReplayData(
    int SchemaVersion,
    PersistedMatchConfiguration Configuration,
    ulong FinalTick,
    ulong FinalRandomState,
    IReadOnlyList<RecordedSimulationCommand> Commands,
    MatchState FinalMatchState,
    string FinalStateSha256,
    int AuthoritativeSchemaVersion = 1)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DeveloperModified { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeveloperParameterAuditData[]? ParameterChanges { get; init; }
}

internal sealed record MatchPersistenceEnvelope(
    string Magic,
    int FormatVersion,
    MatchPersistenceDocumentKind Kind,
    string Payload,
    string PayloadSha256);
