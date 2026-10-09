using System.Diagnostics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class Program
{
    private const int DefaultRenderInstanceCount = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        bool diagnosticsRequested = args.Any(argument =>
            string.Equals(argument, "--startup-diagnostics-output", StringComparison.OrdinalIgnoreCase));
        long processEntryTimestamp = diagnosticsRequested ? Stopwatch.GetTimestamp() : 0;
        Guid processId = diagnosticsRequested ? Guid.NewGuid() : Guid.Empty;
        StartupDiagnostics startup = StartupDiagnostics.Disabled;
        if (!TryParseArguments(
                args,
                out bool smokeTest,
                out int renderInstanceCount,
                out string? visualQualificationOutput,
                out string? settingsRoot))
        {
            return 2;
        }

        bool skipSplash = args.Any(argument =>
            string.Equals(argument, "--skip-splash", StringComparison.OrdinalIgnoreCase));
        RtsReferenceZoom referenceZoom = RtsReferenceZoom.NormalGameplay;
        for (int index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], "--reference-zoom", StringComparison.OrdinalIgnoreCase))
                referenceZoom = Enum.Parse<RtsReferenceZoom>(args[++index], ignoreCase: true);

        string? diagnosticsOutput = null;
        for (int index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], "--startup-diagnostics-output", StringComparison.OrdinalIgnoreCase))
                diagnosticsOutput = args[++index];
        int launchId = diagnosticsOutput is null ? 0 : 1;
        startup = diagnosticsOutput is null ? StartupDiagnostics.Disabled
            : new StartupDiagnostics(processEntryTimestamp, processId, launchId);
        startup.Begin(StartupPhase.Launch);
        startup.Begin(StartupPhase.Run);

        try
        {
            var settingsStore =
                new ClientSettingsStore(
                    settingsRoot);
            using var platform =
                new WindowsPlatform();

            while (true)
            {
                ClientSettingsLoadResult settingsLoad =
                    startup.Measure(StartupPhase.Settings, settingsStore.Load);

                Console.WriteLine(
                    $"[settings:loaded] path=\"{settingsLoad.Path}\" " +
                    $"createdDefaults={settingsLoad.CreatedDefaults} " +
                    $"recoveredInvalid={settingsLoad.RecoveredInvalidSettings}");

                if (!string.IsNullOrWhiteSpace(
                        settingsLoad.RecoveryMessage))
                {
                    Console.WriteLine(
                        $"[settings:recovery] {settingsLoad.RecoveryMessage}");
                }

                var application =
                    new ClientApplication(
                        platform,
                        settingsLoad.Settings with { ReferenceZoom = referenceZoom },
                        settingsLoad.Path,
                        startup);
                int result =
                    application.Run(
                        smokeTest,
                        renderInstanceCount,
                        visualQualificationOutput,
                        skipSplash);

                startup.Finish();
                if (diagnosticsOutput is not null)
                    startup.WriteReport(ReportPath(diagnosticsOutput, launchId));

                if (smokeTest ||
                    result !=
                    ClientApplication.RestartRequestedExitCode)
                {
                    return result;
                }

                if (diagnosticsOutput is not null)
                {
                    startup = new StartupDiagnostics(processEntryTimestamp, processId, ++launchId);
                    startup.Begin(StartupPhase.Launch);
                    startup.Begin(StartupPhase.Run);
                }
            }
        }
        catch (Exception exception)
        {
            startup.Finish(exception);
            if (diagnosticsOutput is not null)
                startup.WriteReport(ReportPath(diagnosticsOutput, launchId));
            string? report =
                ClientDiagnostics.WriteCrashReport(
                    exception);

            Console.Error.WriteLine(
                $"[platform:error] type={exception.GetType().Name} message={exception.Message}");

            if (!string.IsNullOrWhiteSpace(report))
            {
                Console.Error.WriteLine(
                    $"[diagnostics:failure-report] path=\"{report}\"");
            }

            return 1;
        }
    }

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

    private static string ReportPath(string output, int launchId) =>
        launchId <= 1 ? output : Path.Combine(Path.GetDirectoryName(output) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(output)}-launch{launchId}{Path.GetExtension(output)}");

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "ForgeLine.Client [--smoke-test] [--render-stress <instances>] " +
            "[--visual-qualification-output <report.json>] " +
            "[--settings-root <directory>] [--skip-splash] " +
            "[--startup-diagnostics-output <report.json>] " +
            "[--reference-zoom <CloseTactical|NormalGameplay|Strategic>]");
    }
}
