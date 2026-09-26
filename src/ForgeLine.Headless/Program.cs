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
            VerticalSliceScenario scenario =
                VerticalSliceScenario.Create(
                    settings,
                    matchSeed,
                    enableDiagnostics: true);

            var matchStopwatch =
                Stopwatch.StartNew();
            ulong executedTicks = 0;

            while (executedTicks < options.TickCount &&
                   !cancellationToken.IsCancellationRequested &&
                   !scenario.GetMatchState().IsTerminal)
            {
                scenario.Simulation.AdvanceOneTick();
                executedTicks++;
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

            Console.WriteLine(
                $"Vertical slice match {report.MatchIndex}/{options.MatchCount}: " +
                $"status={report.MatchStatus}; winner={report.Winner}; " +
                $"ticks={report.ExecutedTicks}; logical={report.LogicalSeconds:F1}s; " +
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

            if (options.RequireTerminal &&
                !scenario.GetMatchState().IsTerminal)
            {
                terminalFailure = true;
                Console.Error.WriteLine(
                    $"Vertical slice match {report.MatchIndex} did not reach a terminal state within {options.TickCount} ticks.");
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

        if (cancellationToken.IsCancellationRequested)
        {
            return 2;
        }

        return terminalFailure
            ? 3
            : 0;
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
        writer.WriteLine("  --help, -h                   Show this help.");
    }

    private readonly record struct HeadlessEntityMarker(int Index);
}
