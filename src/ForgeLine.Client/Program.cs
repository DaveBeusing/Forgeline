using System.Diagnostics;
using System.Runtime.CompilerServices;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class Program
{
    private const int DefaultRenderInstanceCount = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        long processEntryTimestamp = Stopwatch.GetTimestamp();
        if (args.Length == 3 && args[0] == "--ui-typography-capture")
            return UiTypographyCapture.Run(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--reference-capture-request")
            return RunReferenceCapture(args[1]);
        return ClientBootstrap.Run(args, processEntryTimestamp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunReferenceCapture(string path) => VisualReferenceCapture.Run(path);

    internal static bool TryParseArguments(
        string[] args,
        out bool smokeTest,
        out int renderInstanceCount,
        out string? visualQualificationOutput,
        out string? settingsRoot)
    {
        smokeTest = false;
        renderInstanceCount = DefaultRenderInstanceCount;
        visualQualificationOutput = null;
        settingsRoot = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (string.Equals(argument, "--reference-zoom", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    !Enum.TryParse(args[++index], true, out RtsReferenceZoom zoom) || !Enum.IsDefined(zoom))
                {
                    Console.Error.WriteLine("--reference-zoom requires CloseTactical, NormalGameplay or Strategic.");
                    return false;
                }
                continue;
            }

            if (string.Equals(argument, "--startup-diagnostics-output", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("--startup-diagnostics-output requires a file path.");
                    return false;
                }
                index++;
                continue;
            }

            if (string.Equals(argument, "--startup-menu-smoke-test", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(argument, "--skip-splash", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                smokeTest = true;
                continue;
            }

            if (string.Equals(argument, "--render-stress", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    !int.TryParse(args[++index], out renderInstanceCount) ||
                    renderInstanceCount <= 0)
                {
                    Console.Error.WriteLine(
                        "--render-stress requires a positive instance count.");
                    return false;
                }

                continue;
            }

            if (string.Equals(
                    argument,
                    "--visual-qualification-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    args[index + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        "--visual-qualification-output requires a file path.");
                    return false;
                }

                visualQualificationOutput =
                    args[++index];
                continue;
            }

            if (string.Equals(
                    argument,
                    "--settings-root",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length ||
                    args[index + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        "--settings-root requires a directory path.");
                    return false;
                }

                settingsRoot =
                    args[++index];
                continue;
            }

            if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase))
            {
                WriteUsage(Console.Out);
                return false;
            }

            Console.Error.WriteLine($"Unknown argument: {argument}");
            WriteUsage(Console.Error);
            return false;
        }

        return true;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "ForgeLine.Client [--smoke-test] [--render-stress <instances>] " +
            "[--visual-qualification-output <report.json>] " +
            "[--settings-root <directory>] [--skip-splash] " +
            "[--startup-diagnostics-output <report.json>] [--startup-menu-smoke-test] " +
            "[--reference-zoom <CloseTactical|NormalGameplay|Strategic>]");
        writer.WriteLine("Standalone native evidence: ForgeLine.Client --reference-capture-request <request.json>");
    }
}
