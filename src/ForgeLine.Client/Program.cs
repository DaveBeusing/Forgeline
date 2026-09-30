using ForgeLine.Platform.Windows;

namespace ForgeLine.Client;

internal static class Program
{
    private const int DefaultRenderInstanceCount = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!TryParseArguments(
                args,
                out bool smokeTest,
                out int renderInstanceCount,
                out string? visualQualificationOutput))
        {
            return 2;
        }

        try
        {
            using var platform = new WindowsPlatform();

            while (true)
            {
                var application =
                    new ClientApplication(platform);
                int result =
                    application.Run(
                        smokeTest,
                        renderInstanceCount,
                        visualQualificationOutput);

                if (smokeTest ||
                    result !=
                    ClientApplication.RestartRequestedExitCode)
                {
                    return result;
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[platform:error] type={exception.GetType().Name} message={exception.Message}");
            return 1;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out bool smokeTest,
        out int renderInstanceCount,
        out string? visualQualificationOutput)
    {
        smokeTest = false;
        renderInstanceCount = DefaultRenderInstanceCount;
        visualQualificationOutput = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

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
            "[--visual-qualification-output <report.json>]");
    }
}
