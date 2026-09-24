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
                options.Seed);

            var stopwatch = Stopwatch.StartNew();
            ulong executedTicks = simulation.RunTicks(options.TickCount, shutdown.Token);
            stopwatch.Stop();

            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            double throughput = elapsedSeconds > 0d
                ? executedTicks / elapsedSeconds
                : 0d;

            Console.WriteLine(
                $"ForgeLine headless completed {executedTicks} ticks " +
                $"at logical {options.TickRate} Hz with seed {options.Seed}.");
            Console.WriteLine(
                $"Elapsed: {stopwatch.Elapsed.TotalMilliseconds:F3} ms; " +
                $"throughput: {throughput:F0} ticks/s; " +
                $"pending commands: {simulation.PendingCommandCount}.");

            return shutdown.IsCancellationRequested ? 2 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("ForgeLine.Headless");
        writer.WriteLine("  --ticks <count>      Number of simulation ticks to execute (default: 1000).");
        writer.WriteLine("  --seed <value>       Deterministic simulation seed (default: 1).");
        writer.WriteLine("  --tick-rate <hz>     Logical simulation tick rate (default: 20).");
        writer.WriteLine("  --help, -h           Show this help.");
    }
}
