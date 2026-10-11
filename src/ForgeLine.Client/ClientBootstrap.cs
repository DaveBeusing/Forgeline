using System.Diagnostics;
using System.Runtime.CompilerServices;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ClientBootstrap
{
    internal static int Run(string[] args, long processEntry)
    {
        try { return RunCore(args, processEntry); }
        catch (Exception error)
        {
            // Native platform creation can fail before a surface exists.
            Console.Error.WriteLine($"[startup:platform-failed] {error}");
            _ = ClientDiagnostics.WriteCrashReport(error);
            Console.Error.Flush();
            return 1;
        }
    }

    private static int RunCore(string[] args, long processEntry)
    {
        if (!Program.TryParseArguments(args, out bool smokeTest, out int instances,
            out string? qualification, out string? settingsRoot)) return 2;
        bool menuSmoke = args.Contains("--startup-menu-smoke-test", StringComparer.OrdinalIgnoreCase);
        string? output = ArgumentValue(args, "--startup-diagnostics-output");
        Guid identity = Guid.NewGuid();
        using var platform = new WindowsPlatform();
        for (int launch = 1; ; launch++)
        {
            var diagnostics = new StartupDiagnostics(processEntry, identity, launch);
            diagnostics.Mark(StartupPhase.ProcessEntered, detail: "Managed entry; excludes runtime bootstrap");
            diagnostics.Begin(StartupPhase.Launch);
            diagnostics.Begin(StartupPhase.Run);
            string path = output is null ? Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "FORGELINE", "diagnostics",
                $"startup-{Environment.ProcessId}-{launch}.json") : launch == 1 ? output :
                Path.Combine(Path.GetDirectoryName(output) ?? string.Empty,
                    $"{Path.GetFileNameWithoutExtension(output)}-launch{launch}{Path.GetExtension(output)}");
            using IWindow window = diagnostics.Measure(StartupPhase.Window,
                () => platform.CreateBootstrapWindow(new WindowConfiguration("FORGELINE", 1600, 900)));
            diagnostics.Mark(StartupPhase.WindowVisible);
            if (platform.BootstrapPainted(window)) diagnostics.Mark(StartupPhase.NativeBootstrapFirstFrame);
            using var checkpoint = new StartupCheckpoint(diagnostics, path);
            try
            {
                int result = RunApplication(args, platform, window, diagnostics, settingsRoot,
                    smokeTest, menuSmoke, instances, qualification, launch > 1);
                diagnostics.Finish();
                checkpoint.Flush();
                if (result != ClientApplication.RestartRequestedExitCode) return result;
            }
            catch (Exception error)
            {
                diagnostics.Mark(StartupPhase.StartupFailed, detail: error.GetType().Name);
                diagnostics.Finish(error);
                checkpoint.Flush();
                string? report = ClientDiagnostics.WriteCrashReport(error);
                Console.Error.WriteLine($"[startup:failed] {error}");
                Console.Error.Flush();
                if (window.IsOpen)
                {
                    platform.UpdateBootstrap(window, "STARTUP FAILED\n" + error.Message +
                        "\nClose this window to exit. Diagnostics: " + (report ?? path), failed: true);
                    // Automation records the visible failure but does not wait for user input.
                    if (!smokeTest && !menuSmoke)
                        while (window.IsOpen && platform.PumpEvents())
                            platform.WaitForEvents(TimeSpan.FromMilliseconds(16));
                }
                return error is OperationCanceledException ? 0 : 1;
            }
        }
    }

    // Keep application composition and its JIT/dependency work behind native visibility.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApplication(string[] args, WindowsPlatform platform, IWindow window,
        StartupDiagnostics diagnostics, string? settingsRoot, bool smokeTest, bool menuSmoke,
        int instances, string? qualification, bool restart)
    {
        platform.UpdateBootstrap(window, "LOADING SETTINGS");
        using var cancellation = new CancellationTokenSource();
        Task<ClientSettingsLoadResult> settings = Task.Factory.StartNew(() => diagnostics.Measure(
            StartupPhase.Settings, () => new ClientSettingsStore(settingsRoot).LoadCancellable(cancellation.Token)),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            while (!settings.IsCompleted)
            {
                if (!window.IsOpen || !platform.PumpEvents())
                {
                    cancellation.Cancel();
                    return 0;
                }
                platform.WaitForEvents(TimeSpan.FromMilliseconds(16));
            }
            ClientSettingsLoadResult loaded = settings.GetAwaiter().GetResult();
            if (!window.IsOpen || !platform.PumpEvents()) return 0;
            Console.WriteLine($"[settings:loaded] path=\"{loaded.Path}\" createdDefaults={loaded.CreatedDefaults} recoveredInvalid={loaded.RecoveredInvalidSettings}");
            if (!string.IsNullOrWhiteSpace(loaded.RecoveryMessage))
                Console.WriteLine($"[settings:recovery] {loaded.RecoveryMessage}");
            RtsReferenceZoom zoom = ArgumentValue(args, "--reference-zoom") is string raw
                ? Enum.Parse<RtsReferenceZoom>(raw, true) : RtsReferenceZoom.NormalGameplay;
            platform.ConfigureBootstrap(window, loaded.Settings.CreateWindowConfiguration());
            platform.UpdateBootstrap(window, "PREPARING GRAPHICS");
            var application = new ClientApplication(platform, loaded.Settings with { ReferenceZoom = zoom },
                loaded.Path, diagnostics);
            return application.Run(smokeTest, instances, qualification,
                args.Contains("--skip-splash", StringComparer.OrdinalIgnoreCase), restart, window, menuSmoke);
        }
        finally
        {
            cancellation.Cancel();
            // Finish any in-flight file operation while still dispatching native messages.
            while (!settings.IsCompleted)
            {
                _ = platform.PumpEvents();
                Thread.Sleep(16);
            }
            try { _ = settings.GetAwaiter().GetResult(); } catch { }
        }
    }

    private static string? ArgumentValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
