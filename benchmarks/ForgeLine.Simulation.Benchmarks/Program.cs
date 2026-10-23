using BenchmarkDotNet.Running;

namespace ForgeLine.Simulation.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length is >= 2 and <= 4 && args[0] == "--conveyor-measurements")
        {
            ConveyorMeasurements.Run(args[1],
                args.Length >= 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 512,
                args.Length == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : null);
            return;
        }
        if (args.Length == 2 && args[0] == "--vehicle-service")
        {
            VehicleServiceMeasurements.Run(args[1]);
            return;
        }
        if (args.Length is 2 or 3 && args[0] is "--scalability" or "--scalability-no-phase")
        {
            SimulationScalabilityMeasurements.Run(args[1], args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1024,
                phaseTiming: args[0] == "--scalability");
            return;
        }
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

