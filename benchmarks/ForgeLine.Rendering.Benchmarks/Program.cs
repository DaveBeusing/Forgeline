using BenchmarkDotNet.Running;
using System.Runtime;

namespace ForgeLine.Rendering.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        // Exact allocation guards require stable counter accounting. Keep this
        // opt-in local to these synthetic modes; native/CPU timing modes retain
        // the caller's production GC policy (dotnet/runtime#134724).
        if (args.Length == 2 &&
            (args[0] is "--typography-hotpaths" or "--gameplay-ui-hotpaths" or "--alert-map-hotpaths") &&
            GCSettings.LatencyMode != GCLatencyMode.Batch)
        {
            throw new InvalidOperationException(
                "Exact allocation guards require process-local DOTNET_gcConcurrent=0. " +
                "Background GC can misattribute unused allocation-context bytes.");
        }

        if (args is ["--typography-hotpaths", string typographyOutput])
        {
            UiTypographyMeasurements.Run(typographyOutput);
            return;
        }
        if (args is ["--gameplay-ui-hotpaths", string gameplayUiOutput])
        {
            GameplayUiMeasurements.Run(gameplayUiOutput);
            return;
        }
        if (args is ["--alert-map-hotpaths", string alertMapOutput])
        {
            AlertMapMeasurements.Run(alertMapOutput);
            return;
        }
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
