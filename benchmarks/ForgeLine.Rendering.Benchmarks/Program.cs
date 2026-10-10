using BenchmarkDotNet.Running;

namespace ForgeLine.Rendering.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--operations-hotpaths", string operationsOutput])
        {
            OperationsMeasurements.Run(operationsOutput);
            return;
        }
        if (args is ["--combat-group-hotpaths", string groupOutput])
        {
            CombatGroupMeasurements.Run(groupOutput);
            return;
        }
        if (args is ["--selection-hotpaths", string selectionOutput])
        {
            SelectionHotPathMeasurements.Run(selectionOutput);
            return;
        }
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

        if (args is ["--hover-hotpaths", string hoverOutput])
        {
            HoverHotPathMeasurements.Run(hoverOutput);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
