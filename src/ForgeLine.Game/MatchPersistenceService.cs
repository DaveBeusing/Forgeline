using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record MatchCompatibilityReport(
    bool Compatible,
    string ExpectedSha256,
    string ActualSha256,
    IReadOnlyList<string> Differences);

public static class MatchPersistenceService
{
    public static MatchSaveData CaptureSave(
        MatchRuntime scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireCompleteCommandHistory(
            scenario);

        MatchAuthoritativeSnapshot state =
            MatchAuthoritativeSnapshot.Capture(
                scenario);
        string stateHash =
            state.ComputeSha256();

        return new MatchSaveData(
            MatchPersistenceSerializer.CurrentSchemaVersion,
            CaptureConfiguration(scenario),
            scenario.Simulation.CurrentTick.Value,
            scenario.Simulation.Random.State,
            scenario.Replay.CaptureCommands(),
            state,
            stateHash) { DeveloperModified = CaptureModified(scenario), ParameterChanges = CaptureParameterChanges(scenario) };
    }

    public static MatchReplayData CaptureReplay(
        MatchRuntime scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireCompleteCommandHistory(
            scenario);

        MatchAuthoritativeSnapshot state =
            MatchAuthoritativeSnapshot.Capture(
                scenario);

        return new MatchReplayData(
            MatchPersistenceSerializer.CurrentSchemaVersion,
            CaptureConfiguration(scenario),
            scenario.Simulation.CurrentTick.Value,
            scenario.Simulation.Random.State,
            scenario.Replay.CaptureCommands(),
            scenario.GetMatchState(),
            state.ComputeSha256(), MatchAuthoritativeSnapshot.CurrentSchemaVersion)
            { DeveloperModified = CaptureModified(scenario), ParameterChanges = CaptureParameterChanges(scenario) };
    }

    public static MatchRuntime Restore(
        MatchSaveData save,
        Func<string, MatchComposition>? resolveComposition = null) =>
        RestoreCancellable(save, CancellationToken.None, resolveComposition: resolveComposition);

    public static MatchRuntime RestoreCancellable(MatchSaveData save, CancellationToken cancellationToken,
        Action<MatchRestorationProgress>? reportProgress = null, Func<string, MatchComposition>? resolveComposition = null) =>
        RestoreCore(save, reportProgress, resolveComposition, null, cancellationToken);

    public static MatchRuntime RestoreDeveloper(MatchSaveData save, DeveloperToolsCapability capability,
        Func<string, MatchComposition>? resolveComposition = null, Action<MatchRestorationProgress>? reportProgress = null,
        CancellationToken cancellationToken = default) => RestoreCore(save, reportProgress, resolveComposition, capability, cancellationToken);

    private static MatchRuntime RestoreCore(MatchSaveData save, Action<MatchRestorationProgress>? reportProgress,
        Func<string, MatchComposition>? resolveComposition, DeveloperToolsCapability? capability, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(new(MatchRestorationPhase.Configuration));
        ValidateSave(
            save);
        RequireRecoveryCapability(save.Configuration, save.Commands, save.ParameterChanges, save.State.Domains, capability);
        cancellationToken.ThrowIfCancellationRequested();

        return Reconstruct(
            save.Configuration,
            save.SavedTick,
            save.RandomState,
            save.Commands,
            save.StateSha256,
            save.State,
            resolveComposition, reportProgress, capability, save.ParameterChanges, cancellationToken);
    }

    public static MatchRuntime PlayReplay(MatchReplayData replay, Func<string, MatchComposition>? resolveComposition = null) =>
        PlayReplayCore(replay, resolveComposition, null);

    public static MatchRuntime PlayDeveloperReplay(MatchReplayData replay, DeveloperToolsCapability capability,
        Func<string, MatchComposition>? resolveComposition = null) => PlayReplayCore(replay, resolveComposition, capability);

    public static MatchRuntime PlayReplayCancellable(MatchReplayData replay, CancellationToken cancellationToken,
        Action<MatchRestorationProgress>? reportProgress = null, Func<string, MatchComposition>? resolveComposition = null,
        IReadOnlyList<MatchReplayCheckpoint>? checkpoints = null) =>
        PlayReplayCore(replay, resolveComposition, null, reportProgress, checkpoints, cancellationToken);

    public static MatchRuntime PlayDeveloperReplayCancellable(MatchReplayData replay, DeveloperToolsCapability capability,
        CancellationToken cancellationToken, Action<MatchRestorationProgress>? reportProgress = null,
        Func<string, MatchComposition>? resolveComposition = null, IReadOnlyList<MatchReplayCheckpoint>? checkpoints = null) =>
        PlayReplayCore(replay, resolveComposition, capability, reportProgress, checkpoints, cancellationToken);

    private static MatchRuntime PlayReplayCore(MatchReplayData replay, Func<string, MatchComposition>? resolveComposition,
        DeveloperToolsCapability? capability, Action<MatchRestorationProgress>? reportProgress = null,
        IReadOnlyList<MatchReplayCheckpoint>? checkpoints = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(new(MatchRestorationPhase.Configuration));
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReplay(
            replay);
        RequireRecoveryCapability(replay.Configuration, replay.Commands, replay.ParameterChanges, null, capability);
        MatchReplayCheckpoint[] expected = ValidateCheckpoints(checkpoints, replay.FinalTick);

        MatchRuntime? scenario = null;

        try
        {
            reportProgress?.Invoke(new(MatchRestorationPhase.ScenarioAssembly));
            cancellationToken.ThrowIfCancellationRequested();
            scenario =
                CreateAndSchedule(
                    replay.Configuration,
                    replay.Commands,
                    resolveComposition, developerCapability: capability, cancellationToken: cancellationToken);
            RunToTickWithControls(
                scenario,
                replay.FinalTick,
                replay.Commands, reportProgress, expected, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new(MatchRestorationPhase.HashVerification, replay.FinalTick, replay.FinalTick));
            cancellationToken.ThrowIfCancellationRequested();

            VerifyParameterChanges(scenario, replay.ParameterChanges);
            if (scenario.Simulation.Random.State !=
                replay.FinalRandomState)
            {
                throw Failure(
                    MatchPersistenceFailureReason.RandomStateMismatch,
                    $"Replay random state diverged at tick {replay.FinalTick}: expected {replay.FinalRandomState}, actual {scenario.Simulation.Random.State}.");
            }

            MatchState matchState =
                scenario.GetMatchState();

            if (matchState !=
                replay.FinalMatchState)
            {
                throw Failure(
                    MatchPersistenceFailureReason.StateMismatch,
                    $"Replay match lifecycle diverged at tick {replay.FinalTick}.");
            }

            MatchAuthoritativeSnapshot actual =
                MatchAuthoritativeSnapshot.Capture(
                    scenario);
            string actualHash =
                actual.ComputeSha256();

            if (!string.Equals(
                    actualHash,
                    replay.FinalStateSha256,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    MatchPersistenceFailureReason.StateMismatch,
                    $"Replay authoritative state diverged at tick {replay.FinalTick}: expected {replay.FinalStateSha256}, actual {actualHash}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            MatchRuntime result =
                scenario;
            scenario = null;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MatchPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(
                MatchPersistenceFailureReason.InvalidConfiguration,
                "Replay reconstruction failed before a compatible state could be established.",
                exception);
        }
        finally
        {
            scenario?.Dispose();
        }
    }

    public static MatchCompatibilityReport Compare(
        MatchAuthoritativeSnapshot expected,
        MatchAuthoritativeSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        string expectedHash =
            expected.ComputeSha256();
        string actualHash =
            actual.ComputeSha256();
        var differences =
            new List<string>();

        if (expected.SchemaVersion !=
            actual.SchemaVersion)
        {
            differences.Add(
                $"schema expected={expected.SchemaVersion} actual={actual.SchemaVersion}");
        }

        if (!string.Equals(
                expected.BattlefieldKey,
                actual.BattlefieldKey,
                StringComparison.Ordinal))
        {
            differences.Add(
                $"battlefield expected={expected.BattlefieldKey} actual={actual.BattlefieldKey}");
        }

        if (expected.Tick !=
            actual.Tick)
        {
            differences.Add(
                $"tick expected={expected.Tick} actual={actual.Tick}");
        }

        if (expected.RandomState !=
            actual.RandomState)
        {
            differences.Add(
                $"rng expected={expected.RandomState} actual={actual.RandomState}");
        }

        if (expected.MatchState !=
            actual.MatchState)
        {
            differences.Add(
                "match lifecycle state differs");
        }

        if (expected.Entities.EntityCount !=
            actual.Entities.EntityCount)
        {
            differences.Add(
                $"entities expected={expected.Entities.EntityCount} actual={actual.Entities.EntityCount}");
        }

        if (expected.Entities.ComponentStores.Count !=
            actual.Entities.ComponentStores.Count)
        {
            differences.Add(
                $"component stores expected={expected.Entities.ComponentStores.Count} actual={actual.Entities.ComponentStores.Count}");
        }

        if (expected.Inventories.Inventories.Count !=
            actual.Inventories.Inventories.Count)
        {
            differences.Add(
                $"inventories expected={expected.Inventories.Inventories.Count} actual={actual.Inventories.Inventories.Count}");
        }

        if (differences.Count == 0 &&
            !string.Equals(
                expectedHash,
                actualHash,
                StringComparison.Ordinal))
        {
            differences.Add(
                "authoritative component, inventory, logistics, intelligence, or subsystem state differs");
        }

        return new MatchCompatibilityReport(
            differences.Count == 0 &&
            string.Equals(
                expectedHash,
                actualHash,
                StringComparison.Ordinal),
            expectedHash,
            actualHash,
            differences);
    }

    private static MatchRuntime Reconstruct(
        PersistedMatchConfiguration configuration,
        ulong targetTick,
        ulong expectedRandomState,
        IReadOnlyList<RecordedSimulationCommand> commands,
        string expectedStateHash,
        MatchAuthoritativeSnapshot expectedState,
        Func<string, MatchComposition>? resolveComposition,
        Action<MatchRestorationProgress>? reportProgress,
        DeveloperToolsCapability? developerCapability, IReadOnlyList<DeveloperParameterAuditData>? expectedChanges,
        CancellationToken cancellationToken)
    {
        MatchRuntime? scenario = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new(MatchRestorationPhase.ScenarioAssembly));
            scenario =
                CreateAndSchedule(
                    configuration,
                    commands,
                    resolveComposition, developerCapability, cancellationToken);
            RunToTickWithControls(
                scenario,
                targetTick,
                commands, reportProgress, cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke(new(MatchRestorationPhase.HashVerification, targetTick, targetTick));
            VerifyParameterChanges(scenario, expectedChanges);

            if (scenario.Simulation.Random.State !=
                expectedRandomState)
            {
                throw Failure(
                    MatchPersistenceFailureReason.RandomStateMismatch,
                    $"Loaded random state diverged at tick {targetTick}: expected {expectedRandomState}, actual {scenario.Simulation.Random.State}.");
            }

            MatchAuthoritativeSnapshot actualState =
                MatchAuthoritativeSnapshot.Capture(
                    scenario);
            MatchCompatibilityReport compatibility =
                Compare(
                    expectedState,
                    actualState);

            if (!compatibility.Compatible ||
                !string.Equals(
                    compatibility.ExpectedSha256,
                    expectedStateHash,
                    StringComparison.Ordinal))
            {
                string detail =
                    compatibility.Differences.Count == 0
                        ? "stored state checksum does not match the reconstructed checkpoint"
                        : string.Join(
                            "; ",
                            compatibility.Differences);

                throw Failure(
                    MatchPersistenceFailureReason.StateMismatch,
                    $"Loaded match state is incompatible: {detail}. Expected {expectedStateHash}; reconstructed {compatibility.ActualSha256}.");
            }

            MatchRuntime result =
                scenario;
            cancellationToken.ThrowIfCancellationRequested();
            scenario = null;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MatchPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(
                MatchPersistenceFailureReason.InvalidConfiguration,
                "Save reconstruction failed before a compatible state could be established.",
                exception);
        }
        finally
        {
            scenario?.Dispose();
        }
    }

    private static MatchRuntime CreateAndSchedule(
        PersistedMatchConfiguration configuration,
        IReadOnlyList<RecordedSimulationCommand> commands,
        Func<string, MatchComposition>? resolveComposition,
        DeveloperToolsCapability? developerCapability = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);

        RequireRecoveryCapability(configuration, commands, null, null, developerCapability);
        MatchRuntimeSettings runtime = configuration.CreateHeadlessRuntimeSettings(resolveComposition);
        MatchRuntime scenario = configuration.DeveloperInitialProfile is { Parameters.Length: > 0 } initial
            ? MatchRuntime.CreateDeveloper(runtime, initial.ToProfile(), developerCapability!, cancellationToken)
            : MatchRuntime.Create(runtime, cancellationToken);

        try
        {
#if FORGELINE_DEVELOPER_TOOLS
            if (DeveloperPersistenceGuard.Requires(configuration, commands))
                scenario.Services.DeveloperActions.BindAccess(
                    developerCapability!.BindSession(scenario.Simulation.SessionId, true));
#endif
            RecordedSimulationCommand[] scheduled =
                commands
                    .Where(
                        static command =>
                            !command.IsControl)
                    .OrderBy(
                        static command =>
                            command.Sequence)
                    .ToArray();

            ulong expectedSequence = 1;

            for (int index = 0;
                 index < scheduled.Length;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RecordedSimulationCommand entry =
                    scheduled[index];

                if (entry.Sequence !=
                        expectedSequence ||
                    entry.TargetTick == 0)
                {
                    throw Failure(
                        MatchPersistenceFailureReason.CorruptDocument,
                        $"Replay command sequence is invalid at scheduled entry {index}.");
                }

                ISimulationCommand command =
                    ReplayCommandCodec.Decode(
                        entry,
                        scenario);
                SimulationCommandEnvelope submitted =
                    scenario.Simulation.SubmitCommand(
                        command,
                        new SimulationTick(
                            entry.TargetTick),
                        new SimulationCommandSource(
                            entry.Source));

                if (submitted.Sequence !=
                        entry.Sequence ||
                    submitted.TargetTick.Value !=
                        entry.TargetTick)
                {
                    throw Failure(
                        MatchPersistenceFailureReason.StateMismatch,
                        $"Replay command scheduling diverged at sequence {entry.Sequence}.");
                }

                expectedSequence++;
            }

            return scenario;
        }
        catch
        {
            scenario.Dispose();
            throw;
        }
    }

    private static void RunToTickWithControls(
        MatchRuntime scenario,
        ulong targetTick,
        IReadOnlyList<RecordedSimulationCommand> commands,
        Action<MatchRestorationProgress>? reportProgress = null,
        MatchReplayCheckpoint[]? checkpoints = null, CancellationToken cancellationToken = default)
    {
        RecordedSimulationCommand[] controls =
            commands
                .Where(
                    static command =>
                        command.IsControl)
                .OrderBy(
                    static command =>
                        command.RecordingOrder)
                .ToArray();
        int controlIndex = 0;
        int checkpointIndex = 0;
        void VerifyCheckpoint()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checkpoints is null || checkpointIndex >= checkpoints.Length ||
                checkpoints[checkpointIndex].Tick != scenario.Simulation.CurrentTick.Value) return;
            MatchReplayCheckpoint expected = checkpoints[checkpointIndex++];
            MatchReplayCheckpoint actual = MatchReplayCheckpoint.Capture(scenario);
            cancellationToken.ThrowIfCancellationRequested();
            if (expected.RandomState != actual.RandomState)
                throw Failure(MatchPersistenceFailureReason.RandomStateMismatch,
                    $"Replay checkpoint RNG diverged at tick {actual.Tick}.");
            if (!string.Equals(expected.StateSha256, actual.StateSha256, StringComparison.Ordinal))
                throw Failure(MatchPersistenceFailureReason.StateMismatch,
                    $"Replay checkpoint state diverged at tick {actual.Tick}: expected {expected.StateSha256}, actual {actual.StateSha256}.");
        }
        reportProgress?.Invoke(new(MatchRestorationPhase.Replay, scenario.Simulation.CurrentTick.Value, targetTick));

        VerifyCheckpoint();
        while (scenario.Simulation.CurrentTick.Value <
               targetTick)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scenario.Simulation.AdvanceOneTick();
            ulong currentTick =
                scenario.Simulation.CurrentTick.Value;

            while (controlIndex <
                       controls.Length &&
                   controls[controlIndex].TargetTick ==
                       currentTick)
            {
                ISimulationCommand control =
                    ReplayCommandCodec.Decode(
                        controls[controlIndex],
                        scenario);
                scenario.Simulation.ExecuteControlCommand(
                    control);
                controlIndex++;
            }
            VerifyCheckpoint();
            // Report only complete tick/control boundaries; observers never alter replay scheduling.
            if (currentTick % 64 == 0 || currentTick == targetTick)
                reportProgress?.Invoke(new(MatchRestorationPhase.Replay, currentTick, targetTick));
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (checkpoints is not null && checkpointIndex != checkpoints.Length)
            throw Failure(MatchPersistenceFailureReason.CorruptDocument, "Replay checkpoints were not reached.");

        if (controlIndex !=
            controls.Length)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "Replay contains a control command outside the captured tick range.");
        }
    }

    private static MatchReplayCheckpoint[] ValidateCheckpoints(IReadOnlyList<MatchReplayCheckpoint>? checkpoints, ulong finalTick)
    {
        if (checkpoints is null) return [];
        if (checkpoints.Count > MatchReplayCheckpoint.MaximumCount)
            throw Failure(MatchPersistenceFailureReason.CorruptDocument, "Replay checkpoint count exceeds its limit.");
        MatchReplayCheckpoint[] copied = checkpoints.ToArray();
        ulong previous = 0;
        for (int index = 0; index < copied.Length; index++)
        {
            MatchReplayCheckpoint checkpoint = copied[index];
            if (checkpoint is not null && checkpoint.SchemaVersion != 1)
                throw Failure(MatchPersistenceFailureReason.IncompatibleVersion, "Replay checkpoint schema is incompatible.");
            if (checkpoint is null || checkpoint.Tick > finalTick ||
                (index > 0 && checkpoint.Tick <= previous) || !MatchPersistenceValidation.Hash(checkpoint.StateSha256))
                throw Failure(MatchPersistenceFailureReason.CorruptDocument, "Replay checkpoints require increasing in-range ticks and complete checksums.");
            previous = checkpoint.Tick;
        }
        return copied;
    }

    private static void ValidateCommandHistory(
        IReadOnlyList<RecordedSimulationCommand> commands,
        ulong targetTick)
    {
        RecordedSimulationCommand[] recorded =
            commands
                .OrderBy(
                    static command =>
                        command.RecordingOrder)
                .ToArray();
        ulong expectedRecordingOrder = 1;
        ulong expectedScheduledSequence = 1;

        for (int index = 0;
             index < recorded.Length;
             index++)
        {
            RecordedSimulationCommand command =
                recorded[index];

            if (command.RecordingOrder !=
                expectedRecordingOrder)
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    $"Replay recording order is invalid at entry {index}.");
            }

            expectedRecordingOrder++;

            if (command.IsControl)
            {
                if (command.Sequence != 0 ||
                    command.TargetTick == 0 ||
                    command.TargetTick >
                        targetTick)
                {
                    throw Failure(
                        MatchPersistenceFailureReason.CorruptDocument,
                        $"Control command at entry {index} has invalid tick or sequence metadata.");
                }

                continue;
            }

            if (command.Sequence !=
                    expectedScheduledSequence ||
                command.TargetTick == 0)
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    $"Scheduled command at entry {index} has invalid sequence metadata.");
            }

            expectedScheduledSequence++;
        }
    }

    internal static void ValidateSave(
        MatchSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);

        ValidateSchema(
            save.SchemaVersion);
        MatchPersistenceValidation.Configuration(save.Configuration);
        ValidateScenarioIdentity(save.SchemaVersion, save.Configuration);
        MatchPersistenceValidation.Commands(save.Commands, save.SchemaVersion);
        MatchPersistenceValidation.Require(save.State is not null, "Missing authoritative checkpoint.");

        ValidateCommandHistory(
            save.Commands,
            save.SavedTick);

        if (save.State.SchemaVersion !=
            MatchAuthoritativeSnapshot.CurrentSchemaVersion)
        {
            throw Failure(
                MatchPersistenceFailureReason.IncompatibleVersion,
                $"Save state schema {save.State.SchemaVersion} is incompatible with state schema {MatchAuthoritativeSnapshot.CurrentSchemaVersion}.");
        }

        MatchPersistenceValidation.State(save.State);
        DeveloperPersistenceGuard.Validate(save.SchemaVersion, save.Configuration, save.Commands, save.ParameterChanges, save.State.Domains, save.DeveloperModified);
        MatchPersistenceValidation.Require(MatchPersistenceValidation.Hash(save.StateSha256), "Invalid checkpoint checksum.");

        if (save.SavedTick !=
                save.State.Tick ||
            save.RandomState !=
                save.State.RandomState)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "Save checkpoint metadata does not match its authoritative state.");
        }

        string stateHash =
            save.State.ComputeSha256();

        if (!string.Equals(
                stateHash,
                save.StateSha256,
                StringComparison.Ordinal))
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "Save authoritative-state checksum is invalid.");
        }
    }

    internal static void ValidateReplay(
        MatchReplayData replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        ValidateSchema(
            replay.SchemaVersion);
        if (replay.AuthoritativeSchemaVersion != MatchAuthoritativeSnapshot.CurrentSchemaVersion)
            throw Failure(MatchPersistenceFailureReason.IncompatibleVersion,
                $"Replay state schema {replay.AuthoritativeSchemaVersion} is incompatible with state schema {MatchAuthoritativeSnapshot.CurrentSchemaVersion}.");
        MatchPersistenceValidation.Configuration(replay.Configuration);
        ValidateScenarioIdentity(replay.SchemaVersion, replay.Configuration);
        MatchPersistenceValidation.Commands(replay.Commands, replay.SchemaVersion);
        MatchPersistenceValidation.Lifecycle(replay.FinalMatchState);
        DeveloperPersistenceGuard.Validate(replay.SchemaVersion, replay.Configuration, replay.Commands, replay.ParameterChanges, developerModified: replay.DeveloperModified);

        ValidateCommandHistory(
            replay.Commands,
            replay.FinalTick);

        if (!MatchPersistenceValidation.Hash(replay.FinalStateSha256))
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "Replay final-state checksum is missing.");
        }
    }

    private static PersistedMatchConfiguration CaptureConfiguration(MatchRuntime runtime) =>
        PersistedMatchConfiguration.Capture(runtime.PersistenceBaseSettings) with
        { DeveloperInitialProfile = runtime.InitialDeveloperProfile is null ? null : DeveloperInitialProfileData.Capture(runtime.InitialDeveloperProfile) };
    private static bool CaptureModified(MatchRuntime runtime)
    {
#if FORGELINE_DEVELOPER_TOOLS
        return runtime.Services.DeveloperActions is DeveloperCommandGateway gateway && gateway.DeveloperModified;
#else
        return false;
#endif
    }
    private static DeveloperParameterAuditData[]? CaptureParameterChanges(MatchRuntime runtime)
    {
#if FORGELINE_DEVELOPER_TOOLS
        if (runtime.Services.DeveloperActions is DeveloperCommandGateway gateway && gateway.CaptureParameterChanges() is { Count: > 0 } changes)
            return changes.ToArray();
#endif
        return null;
    }
    private static void RequireRecoveryCapability(PersistedMatchConfiguration configuration, IReadOnlyList<RecordedSimulationCommand> commands,
        IReadOnlyList<DeveloperParameterAuditData>? changes, IReadOnlyList<AuthoritativeDomainSnapshot>? domains, DeveloperToolsCapability? capability)
    {
        if (DeveloperPersistenceGuard.Requires(configuration, commands, changes, domains) &&
            (capability is null || !capability.IsCompiled || !ReferenceEquals(capability, DeveloperToolsCapability.Current)))
            throw Failure(MatchPersistenceFailureReason.IncompatibleVersion, "This recovery requires explicit approval from a compiled developer host.");
    }
    private static void VerifyParameterChanges(MatchRuntime runtime, IReadOnlyList<DeveloperParameterAuditData>? expected)
    {
        var actual = CaptureParameterChanges(runtime);
        if (System.Text.Json.JsonSerializer.Serialize(expected ?? Array.Empty<DeveloperParameterAuditData>()) !=
            System.Text.Json.JsonSerializer.Serialize(actual ?? Array.Empty<DeveloperParameterAuditData>()))
            throw Failure(MatchPersistenceFailureReason.StateMismatch, "The ordered parameter audit does not match reconstructed changes.");
    }

    private static void ValidateScenarioIdentity(int schema, PersistedMatchConfiguration configuration)
    {
        if (configuration.Identity is null &&
            ScenarioRegistry.Default.Descriptors.Any(d => d.Key == configuration.CompositionKey) &&
            (schema >= 7 || configuration.CompositionKey != CentralDivideScenario.CompositionKey))
            throw Failure(MatchPersistenceFailureReason.IncompatibleVersion, "This payload requires a complete scenario identity.");
    }

    private static void ValidateSchema(
        int schemaVersion)
    {
        if (schemaVersion is < 1 or > MatchPersistenceSerializer.CurrentSchemaVersion)
        {
            throw Failure(
                MatchPersistenceFailureReason.IncompatibleVersion,
                $"Match schema {schemaVersion} is incompatible with schema {MatchPersistenceSerializer.CurrentSchemaVersion}.");
        }
    }

    private static void RequireCompleteCommandHistory(
        MatchRuntime scenario)
    {
        if (scenario.Replay.IsComplete)
        {
            return;
        }

        string unsupported =
            string.Join(
                ", ",
                scenario.Replay.UnsupportedCommandTypes);

        throw Failure(
            MatchPersistenceFailureReason.IncompleteCommandHistory,
            $"The match contains unsupported queued command types and cannot be recovered safely: {unsupported}.");
    }

    private static MatchPersistenceException Failure(
        MatchPersistenceFailureReason reason,
        string message,
        Exception? innerException = null) =>
        new(
            reason,
            message,
            innerException);
}
