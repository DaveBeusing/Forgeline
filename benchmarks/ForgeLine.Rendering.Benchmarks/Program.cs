using BenchmarkDotNet.Running;

namespace ForgeLine.Rendering.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--frame-hotpaths", string output])
        {
            FrameHotPathMeasurements.Run(output);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
