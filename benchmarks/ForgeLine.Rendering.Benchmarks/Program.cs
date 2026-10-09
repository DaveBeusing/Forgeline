using BenchmarkDotNet.Running;

namespace ForgeLine.Rendering.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length is 2 or 3 && args[0] is "--scalability" or "--gpu-scalability")
        {
            RenderingScalabilityMeasurements.Run(args[1], args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1024,
                native: args[0] == "--gpu-scalability");
            return;
        }
        if (args is ["--culled-hotpath", string culledOutput])
        {
            FrameHotPathMeasurements.RunCulled(culledOutput);
            return;
        }

        if (args is ["--frame-hotpaths", string output])
        {
            FrameHotPathMeasurements.Run(output);
            return;
        }

        if (args is ["--interaction-hotpaths", string interactionOutput])
        {
            InteractionHotPathMeasurements.Run(interactionOutput);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
