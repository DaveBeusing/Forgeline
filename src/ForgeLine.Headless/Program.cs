using System.Diagnostics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Headless;

internal static class Program
{
    public static int Main(string[] args)
    {
        HeadlessOptions options;

        try
        {
            options =
                HeadlessOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(
                exception.Message);
            WriteUsage(Console.Error);
            return 1;
        }

        if (options.ShowHelp)
        {
            WriteUsage(Console.Out);
            return 0;
        }

        if (options.LoadInput is not null)
        {
            return ValidateSaveRecovery(
                options.LoadInput);
        }

        if (options.ReplayInput is not null)
        {
            return ValidateReplayPlayback(
                options.ReplayInput);
        }

        using var shutdown =
            new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler =
            (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

        Console.CancelKeyPress +=
            cancelHandler;

        try
        {
            return options.Scenario switch
            {
                HeadlessScenarioKind.Lightweight =>
                    RunLightweight(
                        options,
                        shutdown.Token),
                HeadlessScenarioKind.VerticalSlice =>
                    RunVerticalSlice(
                        options,
                        shutdown.Token),
                _ =>
                    throw new InvalidOperationException(
                        $"Unsupported headless scenario '{options.Scenario}'.")
            };
        }
        finally
        {
            Console.CancelKeyPress -=
                cancelHandler;
        }
    }

    private static int RunLightweight(
        HeadlessOptions options,
        CancellationToken cancellationToken)
    {
        var simulation =
            new SimulationCoordinator(
                options.TickRate,
                options.Seed,
                initialEntityCapacity:
                    Math.Max(
                        256,
                        options.EntityCount),
                diagnosticsOptions:
                    new SimulationDiagnosticsOptions
                    {
                        Enabled =
                            options.DiagnosticsOutput is not null
                    });

        PopulateLightweightEntities(
            simulation,
            options.EntityCount);

        var stopwatch =
            Stopwatch.StartNew();
        ulong executedTicks =
            simulation.RunTicks(
                options.TickCount,
                cancellationToken);
        stopwatch.Stop();

        HeadlessDiagnosticsReport report =
            HeadlessDiagnosticsReport.Create(
                options,
                executedTicks,
                stopwatch.Elapsed,
                simulation);

        Console.WriteLine(
            $"ForgeLine headless completed {executedTicks} ticks " +
            $"at logical {options.TickRate} Hz with seed {options.Seed}.");
        Console.WriteLine(
            $"Elapsed: {report.ElapsedMilliseconds:F3} ms; " +
            $"throughput: {report.ThroughputTicksPerSecond:F0} ticks/s; " +
            $"entities: {simulation.Entities.EntityCount}; " +
            $"pending commands: {simulation.PendingCommandCount}.");

        if (options.DiagnosticsOutput is not null)
        {
            report.Write(
                options.DiagnosticsOutput);
            Console.WriteLine(
                $"Diagnostics report: {Path.GetFullPath(options.DiagnosticsOutput)}");
        }

        return cancellationToken.IsCancellationRequested
            ? 2
            : 0;
    }

    private static int RunVerticalSlice(
        HeadlessOptions options,
        CancellationToken cancellationToken)
    {
        VerticalSliceScenarioSettings settings =
            VerticalSliceScenarioSettings.Create(
                options.Profile);
        var reports =
            new List<VerticalSliceMatchReport>(
                options.MatchCount);
        var telemetryMatches =
            new List<GameplayTelemetryMatch>(
                options.MatchCount);
        var overallStopwatch =
            Stopwatch.StartNew();
        bool terminalFailure = false;

        for (int matchIndex = 0;
             matchIndex < options.MatchCount &&
             !cancellationToken.IsCancellationRequested;
             matchIndex++)
        {
            ulong matchSeed =
                checked(
                    options.Seed +
                    (ulong)matchIndex);
            VerticalSliceRuntimeSettings runtimeSettings =
                VerticalSliceRuntimeSettings.CreateHeadless(
                    settings.Profile,
                    matchSeed,
                    enableDiagnostics: true,
                    enableDebugCapture:
                        options.DiagnosticsOutput is not null) with
                {
                    Scenario = settings
                };
            using VerticalSliceScenario scenario =
                VerticalSliceScenario.Create(
                    runtimeSettings,
                    cancellationToken);
            GameplayTelemetryCollector? telemetry =
                options.TelemetryOutput is not null
                    ? new GameplayTelemetryCollector(
                        scenario)
                    : null;
            SkirmishProgressionDiagnostics? progression = null;
            if (options.DiagnosticsOutput is not null)
            {
                progression = new SkirmishProgressionDiagnostics(
                    scenario.Inventories,
                    DirectorateContent.CreateUnitCatalog(),
                    new Dictionary<PlayerId, SkirmishOpponentConfiguration>
                    {
                        [scenario.West.Player] = settings.WestOpponent,
                        [scenario.East.Player] = settings.EastOpponent
                    });
                scenario.Simulation.RegisterSystem(progression);
            }

            var matchStopwatch =
                Stopwatch.StartNew();
            ulong executedTicks = 0;

            while (executedTicks < options.TickCount &&
                   !cancellationToken.IsCancellationRequested &&
                   !scenario.GetMatchState().HasResult)
            {
                scenario.Simulation.AdvanceOneTick();
                executedTicks++;
                telemetry?.Observe();
            }

            if (!cancellationToken.IsCancellationRequested &&
                scenario.GetMatchState().HasResult &&
                !scenario.GetMatchState().IsCompleted)
            {
                FinalizeHeadlessMatch(
                    scenario);
            }

            matchStopwatch.Stop();

            VerticalSliceMatchReport report =
                VerticalSliceMatchReport.Capture(
                    matchIndex + 1,
                    matchSeed,
                    executedTicks,
                    matchStopwatch.Elapsed,
                    scenario);
            reports.Add(report);

            if (telemetry is not null)
            {
                GameplayTelemetrySnapshot snapshot =
                    telemetry.Capture();
                telemetryMatches.Add(
                    new GameplayTelemetryMatch(
                        matchIndex + 1,
                        matchSeed,
                        snapshot));
                WriteTelemetrySummary(
                    snapshot);
            }

            Console.WriteLine(
                $"Vertical slice match {report.MatchIndex}/{options.MatchCount}: " +
                $"status={report.MatchStatus}; lifecycle={report.Lifecycle.Phase}; " +
                $"outcome={report.Lifecycle.Outcome}; reason={report.Lifecycle.TerminationReason}; " +
                $"winner={report.Winner}; ticks={report.ExecutedTicks}; logical={report.LogicalSeconds:F1}s; " +
                $"elapsed={report.ElapsedMilliseconds:F1}ms; " +
                $"avgTick={report.AverageTickMilliseconds:F3}ms; " +
                $"maxTick={report.MaximumTickMilliseconds:F3}ms; " +
                $"allocated={report.AllocatedBytes} bytes.");
            Console.WriteLine(
                $"Integrated metrics: cargoDelivered={report.DeliveredCargoQuantity:F1}; " +
                $"cargoCompleted={report.CompletedCargoOrders}; cargoFailed={report.FailedCargoOrders}; " +
                $"routeFailures={report.RouteFailures}; " +
                $"supplyFuel={report.TotalFuelTransferred:F1}; " +
                $"supplyAmmo={report.TotalAmmunitionTransferred:F1}; " +
                $"artilleryShots={report.TotalArtilleryShots}; " +
                $"artilleryImpacts={report.TotalArtilleryImpacts}; " +
                $"westContacts={report.West.KnownHostileContacts}; " +
                $"eastContacts={report.East.KnownHostileContacts}; " +
                $"westReadiness={report.West.AverageReadiness:F3}; " +
                $"eastReadiness={report.East.AverageReadiness:F3}.");

            WriteSideSummary(
                "west",
                report.West);
            WriteSideSummary(
                "east",
                report.East);

            if (progression is not null && options.DiagnosticsOutput is not null)
            {
                SkirmishProgressionReport.Write(
                    options.DiagnosticsOutput, options.Profile, matchIndex + 1,
                    matchSeed, executedTicks, scenario.GetMatchState(), progression);
                WriteDistributionSummary(scenario);
            }

            if (options.SaveOutput is not null)
            {
                MatchSaveData save =
                    MatchPersistenceService.CaptureSave(
                        scenario);
                MatchPersistenceSerializer.WriteSave(
                    options.SaveOutput,
                    save);
                Console.WriteLine(
                    $"Match save: {Path.GetFullPath(options.SaveOutput)}");
            }

            if (options.ReplayOutput is not null)
            {
                MatchReplayData replay =
                    MatchPersistenceService.CaptureReplay(
                        scenario);
                MatchPersistenceSerializer.WriteReplay(
                    options.ReplayOutput,
                    replay);
                Console.WriteLine(
                    $"Match replay: {Path.GetFullPath(options.ReplayOutput)}");
            }

            if (options.RequireTerminal &&
                !scenario.GetMatchState().IsCompleted)
            {
                terminalFailure = true;
                Console.Error.WriteLine(
                    $"Vertical slice match {report.MatchIndex} did not complete its lifecycle within {options.TickCount} ticks.");
                break;
            }
        }

        overallStopwatch.Stop();

        var overallReport =
            VerticalSliceHeadlessReport.Create(
                options,
                overallStopwatch.Elapsed,
                reports);

        if (options.DiagnosticsOutput is not null)
        {
            overallReport.Write(
                options.DiagnosticsOutput);
            Console.WriteLine(
                $"Diagnostics report: {Path.GetFullPath(options.DiagnosticsOutput)}");
        }

        if (options.TelemetryOutput is not null)
        {
            GameplayTelemetryBatchReport telemetryReport =
                GameplayTelemetryBatchReport.Create(
                    options,
                    telemetryMatches);
            telemetryReport.Write(
                options.TelemetryOutput);
            Console.WriteLine(
                $"Gameplay telemetry report: {Path.GetFullPath(options.TelemetryOutput)}");

            if (telemetryReport.Comparison.Count > 0)
            {
                Console.WriteLine(
                    $"Gameplay telemetry comparison: {telemetryReport.Comparison.Count} comparable metric series.");
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return 2;
        }

        return terminalFailure
            ? 3
            : 0;
    }

    private static int ValidateSaveRecovery(
        string path)
    {
        try
        {
            MatchSaveData save =
                MatchPersistenceSerializer.ReadSave(
                    path);
            using VerticalSliceScenario restored =
                MatchPersistenceService.Restore(
                    save);
            VerticalSliceAuthoritativeSnapshot state =
                VerticalSliceAuthoritativeSnapshot.Capture(
                    restored);

            Console.WriteLine(
                $"Save recovery validated: tick={state.Tick}; rng={state.RandomState}; state={state.ComputeSha256()}.");
            return 0;
        }
        catch (MatchPersistenceException exception)
        {
            Console.Error.WriteLine(
                $"Save recovery failed ({exception.Reason}): {exception.Message}");
            return 4;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(
                $"Save recovery failed: {exception.Message}");
            return 4;
        }
    }

    private static int ValidateReplayPlayback(
        string path)
    {
        try
        {
            MatchReplayData replay =
                MatchPersistenceSerializer.ReadReplay(
                    path);
            using VerticalSliceScenario playback =
                MatchPersistenceService.PlayReplay(
                    replay);
            VerticalSliceAuthoritativeSnapshot state =
                VerticalSliceAuthoritativeSnapshot.Capture(
                    playback);

            Console.WriteLine(
                $"Replay validated: tick={state.Tick}; rng={state.RandomState}; state={state.ComputeSha256()}.");
            return 0;
        }
        catch (MatchPersistenceException exception)
        {
            Console.Error.WriteLine(
                $"Replay validation failed ({exception.Reason}): {exception.Message}");
            return 4;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(
                $"Replay validation failed: {exception.Message}");
            return 4;
        }
    }

    private static void FinalizeHeadlessMatch(
        VerticalSliceScenario scenario)
    {
        MatchParticipantConfiguration participant =
            scenario.MatchConfiguration.Participants[0];
        var command =
            new EndMatchCommand(
                participant.Player,
                scenario.BattlefieldRuntime.MatchStateEntity,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.ExecuteControlCommand(
            command);

        if (!command.Accepted)
        {
            throw new InvalidOperationException(
                "Headless match result could not transition to the completed lifecycle state.");
        }
    }

    private static void WriteDistributionSummary(VerticalSliceScenario scenario)
    {
        const int maximumRequests = 128;
        IReadOnlyList<LogisticsTransportRequestReadModel> requests =
            scenario.AutomatedDistribution.LastDebugSnapshot.Requests;
        Console.WriteLine($"Distribution: requests={requests.Count}; omitted={Math.Max(0, requests.Count - maximumRequests)}.");
        foreach (LogisticsTransportRequestReadModel request in requests.Take(maximumRequests))
        {
            bool hasPolicy = scenario.Simulation.Entities.TryGetComponent(
                request.PolicyEntity, out LogisticsStockPolicy policy);
            bool hasBuilding = hasPolicy && scenario.Simulation.Entities.HasComponent<CompletedBuilding>(policy.TargetEntity);
            CompletedBuilding building = hasBuilding
                ? scenario.Simulation.Entities.GetComponent<CompletedBuilding>(policy.TargetEntity) : default;
            Console.WriteLine(
                $"Distribution request: id={request.RequestId}; player={building.Owner}; target={policy.TargetEntity}; " +
                $"building={building.BuildingId}; resource={request.ResourceId}; state={request.State}; reason={request.FailureReason}; " +
                $"quantity={request.RequestedQuantity:F3}; reserved={request.ReservedQuantity:F3}; attempts={request.AttemptCount}; " +
                $"created={request.CreatedAtTick.Value}; changed={request.StateChangedAtTick.Value}; truck={request.AssignedTruck}; " +
                $"minimum={policy.DesiredMinimum:F1}; targetStock={policy.DesiredTarget:F1}.");
        }
    }

    private static void WriteSideSummary(
        string label,
        VerticalSliceSideReport side)
    {
        Console.WriteLine(
            $"{label}: state={side.StrategicState}; goal={side.ActiveGoal}; " +
            $"units={side.TotalUnits}/combat={side.CombatUnits}; " +
            $"rifle={side.RifleSquads}; scout={side.Scouts}; tank={side.MainBattleTanks}; " +
            $"artillery={side.MobileArtillery}; cargo={side.CargoTrucks}; supply={side.SupplyTrucks}; " +
            $"facilities={side.UnitProductionFacilities}; depots={side.SupplyDepots}; radar={side.Radars}; " +
            $"steel={side.Steel:F1}; fuel={side.Fuel:F1}; electronics={side.Electronics:F1}; ammo={side.Ammunition:F1}; " +
            $"minimumSupply={side.MinimumSupply:F3}; resupplyOrders={side.ActiveResupplyOrders}; " +
            $"production=({side.UnitProductionSummary}).");
    }

    private static void WriteTelemetrySummary(
        GameplayTelemetrySnapshot snapshot)
    {
        GameplayMetric? duration =
            snapshot.Metrics.FirstOrDefault(
                static metric =>
                    metric.Name ==
                    GameplayMetricNames.MatchDurationSeconds &&
                    metric.Owner ==
                    "match");
        GameplayMetric? damage =
            snapshot.Metrics.FirstOrDefault(
                static metric =>
                    metric.Name ==
                    GameplayMetricNames.CombatDamageApplied &&
                    metric.Owner ==
                    "match");
        GameplayMetric? cargo =
            snapshot.Metrics.FirstOrDefault(
                static metric =>
                    metric.Name ==
                    GameplayMetricNames.CargoDeliveredQuantity &&
                    metric.Owner ==
                    "match");

        Console.WriteLine(
            $"Gameplay telemetry: ticks={snapshot.ObservedTicks}; " +
            $"duration={duration?.Value ?? 0.0:F1}s; " +
            $"damage={damage?.Value ?? 0.0:F1}; " +
            $"cargoDelivered={cargo?.Value ?? 0.0:F1}; " +
            $"milestones={snapshot.Milestones.Count}.");

        for (int index = 0;
             index < snapshot.Debug.Objectives.Count;
             index++)
        {
            GameplayObjectiveDebugSummary objective =
                snapshot.Debug.Objectives[index];
            Console.WriteLine(
                $"Objective summary: player={objective.Player}; " +
                $"state={objective.StrategicState}; goal={objective.ActiveGoal}.");
        }
    }

    private static void PopulateLightweightEntities(
        SimulationCoordinator simulation,
        int entityCount)
    {
        for (int index = 0;
             index < entityCount;
             index++)
        {
            EntityId entity =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                entity,
                new HeadlessEntityMarker(index));
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("ForgeLine.Headless");
        writer.WriteLine("  --scenario <lightweight|vertical-slice>");
        writer.WriteLine("                               Scenario to execute (default: lightweight).");
        writer.WriteLine("  --profile <gameplay|validation>");
        writer.WriteLine("                               Vertical-slice balance profile (default: gameplay).");
        writer.WriteLine("  --ticks <count>              Ticks to execute, or maximum ticks per match (default: 1000).");
        writer.WriteLine("  --seed <value>               Deterministic simulation seed (default: 1).");
        writer.WriteLine("  --tick-rate <hz>             Logical simulation tick rate (default: 20).");
        writer.WriteLine("  --entities <count>           Lightweight scenario entity count.");
        writer.WriteLine("  --matches <count>            Fresh vertical-slice matches to execute (default: 1).");
        writer.WriteLine("  --require-terminal           Fail if a vertical-slice match does not end within the tick budget.");
        writer.WriteLine("  --diagnostics-output <path>  Write a structured JSON diagnostics report.");
        writer.WriteLine("  --telemetry-output <path>    Write observational gameplay telemetry and batch aggregates.");
        writer.WriteLine("  --telemetry-baseline <path>  Compare telemetry aggregates with a prior telemetry report.");
        writer.WriteLine("  --save-output <path>         Write a versioned match save after one vertical-slice match.");
        writer.WriteLine("  --replay-output <path>       Write a deterministic command replay after one vertical-slice match.");
        writer.WriteLine("  --load-input <path>          Validate and reconstruct a saved match, then exit.");
        writer.WriteLine("  --replay-input <path>        Play and validate a replay, then exit.");
        writer.WriteLine("  --help, -h                   Show this help.");
    }

    private readonly record struct HeadlessEntityMarker(int Index);
}
