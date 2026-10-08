using BenchmarkDotNet.Running;

namespace ForgeLine.Simulation.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length is 2 or 3 && args[0] is "--scalability" or "--scalability-no-phase")
        {
            SimulationScalabilityMeasurements.Run(args[1], args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1024,
                phaseTiming: args[0] == "--scalability");
            return;
        }
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
