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
        VerticalSliceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireCompleteCommandHistory(
            scenario);

        VerticalSliceAuthoritativeSnapshot state =
            VerticalSliceAuthoritativeSnapshot.Capture(
                scenario);
        string stateHash =
            state.ComputeSha256();

        return new MatchSaveData(
            MatchPersistenceSerializer.CurrentSchemaVersion,
            PersistedVerticalSliceConfiguration.Capture(
                scenario.RuntimeSettings),
            scenario.Simulation.CurrentTick.Value,
            scenario.Simulation.Random.State,
            scenario.Replay.CaptureCommands(),
            state,
            stateHash);
    }

    public static MatchReplayData CaptureReplay(
        VerticalSliceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireCompleteCommandHistory(
            scenario);

        VerticalSliceAuthoritativeSnapshot state =
            VerticalSliceAuthoritativeSnapshot.Capture(
                scenario);

        return new MatchReplayData(
            MatchPersistenceSerializer.CurrentSchemaVersion,
            PersistedVerticalSliceConfiguration.Capture(
                scenario.RuntimeSettings),
            scenario.Simulation.CurrentTick.Value,
            scenario.Simulation.Random.State,
            scenario.Replay.CaptureCommands(),
            scenario.GetMatchState(),
            state.ComputeSha256());
    }

    public static VerticalSliceScenario Restore(
        MatchSaveData save)
    {
        ValidateSave(
            save);

        return Reconstruct(
            save.Configuration,
            save.SavedTick,
            save.RandomState,
            save.Commands,
            save.StateSha256,
            save.State);
    }

    public static VerticalSliceScenario PlayReplay(
        MatchReplayData replay)
    {
        ValidateReplay(
            replay);

        VerticalSliceScenario? scenario = null;

        try
        {
            scenario =
                CreateAndSchedule(
                    replay.Configuration,
                    replay.Commands);
            scenario.Simulation.RunTicks(
                replay.FinalTick);

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

            VerticalSliceAuthoritativeSnapshot actual =
                VerticalSliceAuthoritativeSnapshot.Capture(
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

            VerticalSliceScenario result =
                scenario;
            scenario = null;
            return result;
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
        VerticalSliceAuthoritativeSnapshot expected,
        VerticalSliceAuthoritativeSnapshot actual)
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

    private static VerticalSliceScenario Reconstruct(
        PersistedVerticalSliceConfiguration configuration,
        ulong targetTick,
        ulong expectedRandomState,
        IReadOnlyList<RecordedSimulationCommand> commands,
        string expectedStateHash,
        VerticalSliceAuthoritativeSnapshot expectedState)
    {
        VerticalSliceScenario? scenario = null;

        try
        {
            scenario =
                CreateAndSchedule(
                    configuration,
                    commands);
            scenario.Simulation.RunTicks(
                targetTick);

            if (scenario.Simulation.Random.State !=
                expectedRandomState)
            {
                throw Failure(
                    MatchPersistenceFailureReason.RandomStateMismatch,
                    $"Loaded random state diverged at tick {targetTick}: expected {expectedRandomState}, actual {scenario.Simulation.Random.State}.");
            }

            VerticalSliceAuthoritativeSnapshot actualState =
                VerticalSliceAuthoritativeSnapshot.Capture(
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

            VerticalSliceScenario result =
                scenario;
            scenario = null;
            return result;
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

    private static VerticalSliceScenario CreateAndSchedule(
        PersistedVerticalSliceConfiguration configuration,
        IReadOnlyList<RecordedSimulationCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);

        VerticalSliceRuntimeSettings runtime =
            configuration.CreateHeadlessRuntimeSettings();
        VerticalSliceScenario scenario =
            VerticalSliceScenario.Create(
                runtime);

        try
        {
            RecordedSimulationCommand[] ordered =
                commands
                    .OrderBy(
                        static command =>
                            command.Sequence)
                    .ToArray();

            ulong expectedSequence = 1;

            for (int index = 0;
                 index < ordered.Length;
                 index++)
            {
                RecordedSimulationCommand entry =
                    ordered[index];

                if (entry.Sequence !=
                        expectedSequence ||
                    entry.TargetTick == 0)
                {
                    throw Failure(
                        MatchPersistenceFailureReason.CorruptDocument,
                        $"Replay command sequence is invalid at entry {index}.");
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

    private static void ValidateSave(
        MatchSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);

        ValidateSchema(
            save.SchemaVersion);
        ArgumentNullException.ThrowIfNull(
            save.Configuration);
        ArgumentNullException.ThrowIfNull(
            save.Commands);
        ArgumentNullException.ThrowIfNull(
            save.State);

        if (save.State.SchemaVersion !=
            VerticalSliceAuthoritativeSnapshot.CurrentSchemaVersion)
        {
            throw Failure(
                MatchPersistenceFailureReason.IncompatibleVersion,
                $"Save state schema {save.State.SchemaVersion} is incompatible with state schema {VerticalSliceAuthoritativeSnapshot.CurrentSchemaVersion}.");
        }

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

    private static void ValidateReplay(
        MatchReplayData replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        ValidateSchema(
            replay.SchemaVersion);
        ArgumentNullException.ThrowIfNull(
            replay.Configuration);
        ArgumentNullException.ThrowIfNull(
            replay.Commands);

        if (string.IsNullOrWhiteSpace(
                replay.FinalStateSha256))
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "Replay final-state checksum is missing.");
        }
    }

    private static void ValidateSchema(
        int schemaVersion)
    {
        if (schemaVersion !=
            MatchPersistenceSerializer.CurrentSchemaVersion)
        {
            throw Failure(
                MatchPersistenceFailureReason.IncompatibleVersion,
                $"Match schema {schemaVersion} is incompatible with schema {MatchPersistenceSerializer.CurrentSchemaVersion}.");
        }
    }

    private static void RequireCompleteCommandHistory(
        VerticalSliceScenario scenario)
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
