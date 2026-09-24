using System.Diagnostics;
using ForgeLine.Simulation;

namespace ForgeLine.Headless;

internal static class Program
{
    public static int Main(string[] args)
    {
        HeadlessOptions options;

        try
        {
            options = HeadlessOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            WriteUsage(Console.Error);
            return 1;
        }

        if (options.ShowHelp)
        {
            WriteUsage(Console.Out);
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            var simulation = new SimulationCoordinator(
                options.TickRate,
                options.Seed,
                initialEntityCapacity: Math.Max(256, options.EntityCount),
                diagnosticsOptions: new SimulationDiagnosticsOptions
                {
                    Enabled = options.DiagnosticsOutput is not null
                });

            PopulateLightweightEntities(simulation, options.EntityCount);

            var stopwatch = Stopwatch.StartNew();
            ulong executedTicks = simulation.RunTicks(options.TickCount, shutdown.Token);
            stopwatch.Stop();

            HeadlessDiagnosticsReport report = HeadlessDiagnosticsReport.Create(
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
                report.Write(options.DiagnosticsOutput);
                Console.WriteLine(
                    $"Diagnostics report: {Path.GetFullPath(options.DiagnosticsOutput)}");
            }

            return shutdown.IsCancellationRequested ? 2 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void PopulateLightweightEntities(
        SimulationCoordinator simulation,
        int entityCount)
    {
        for (int index = 0; index < entityCount; index++)
        {
            var entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new HeadlessEntityMarker(index));
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("ForgeLine.Headless");
        writer.WriteLine("  --ticks <count>              Number of simulation ticks to execute (default: 1000).");
        writer.WriteLine("  --seed <value>               Deterministic simulation seed (default: 1).");
        writer.WriteLine("  --tick-rate <hz>             Logical simulation tick rate (default: 20).");
        writer.WriteLine("  --entities <count>           Create lightweight ECS entities before ticking.");
        writer.WriteLine("  --diagnostics-output <path>  Write a structured JSON diagnostics report.");
        writer.WriteLine("  --help, -h                   Show this help.");
    }

    private readonly record struct HeadlessEntityMarker(int Index);
}
