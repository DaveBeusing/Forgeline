using System.ComponentModel;
using System.Diagnostics;

namespace ForgeLine.Client;

internal static class RuntimeAssetDevelopmentBootstrap
{
    public static void EnsureAvailable(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    RuntimeAssetPathResolver.OverrideEnvironmentVariable)) ||
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "CI"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? repositoryRoot =
            RuntimeAssetPathResolver.FindRepositoryRoot(
                AppContext.BaseDirectory) ??
            RuntimeAssetPathResolver.FindRepositoryRoot(
                Environment.CurrentDirectory);
        if (repositoryRoot is null)
        {
            return;
        }

        string compilerProject =
            Path.Combine(
                repositoryRoot,
                "tools",
                "ForgeLine.AssetCompiler",
                "ForgeLine.AssetCompiler.csproj");
        string sourceRoot =
            Path.Combine(
                repositoryRoot,
                "assets",
                "source");
        string runtimeRoot =
            Path.Combine(
                repositoryRoot,
                "assets",
                "runtime");

        if (!File.Exists(
                compilerProject) ||
            !Directory.Exists(
                sourceRoot))
        {
            return;
        }

        Console.WriteLine(
            $"[assets:runtime] source-checkout=true action=compile root=\"{runtimeRoot}\"");

        var startInfo =
            new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        startInfo.ArgumentList.Add(
            "run");
        startInfo.ArgumentList.Add(
            "--project");
        startInfo.ArgumentList.Add(
            compilerProject);
        startInfo.ArgumentList.Add(
            "--configuration");
        startInfo.ArgumentList.Add(
            "Release");
        startInfo.ArgumentList.Add(
            "--");
        startInfo.ArgumentList.Add(
            "--source");
        startInfo.ArgumentList.Add(
            sourceRoot);
        startInfo.ArgumentList.Add(
            "--runtime");
        startInfo.ArgumentList.Add(
            runtimeRoot);
        string progressPath = Path.Combine(repositoryRoot, "artifacts", "asset-startup",
            $"compile-{Environment.ProcessId}.jsonl");
        startInfo.ArgumentList.Add("--progress-log");
        startInfo.ArgumentList.Add(progressPath);
        Console.WriteLine($"[assets:runtime] progressLog=\"{progressPath}\" launcher=dotnet-run configuration=Release");

        try
        {
            using Process? compiler =
                Process.Start(
                    startInfo);
            if (compiler is null)
            {
                Console.Error.WriteLine(
                    "[assets:runtime] compile=failed reason=process-start-returned-null");
                return;
            }

            long start = Stopwatch.GetTimestamp();
            Console.WriteLine($"[assets:runtime] launcherPid={compiler.Id} state=waiting");
            WaitForCompiler(compiler, cancellationToken);
            Console.WriteLine($"[assets:runtime] launcherPid={compiler.Id} elapsedMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} exitCode={compiler.ExitCode}");
            if (compiler.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"[assets:runtime] compile=failed exitCode={compiler.ExitCode}");
                return;
            }

            RuntimeAssetPathResolution compiled =
                RuntimeAssetPathResolver.Resolve();
            Console.WriteLine(
                compiled.Found
                    ? $"[assets:runtime] compile=completed root=\"{compiled.RuntimeRoot}\""
                    : "[assets:runtime] compile=completed manifest=missing");
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine(
                $"[assets:runtime] compile=failed type={exception.GetType().Name} message={exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(
                $"[assets:runtime] compile=failed type={exception.GetType().Name} message={exception.Message}");
        }
    }

    internal static void WaitForCompiler(Process compiler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        try
        {
            long start = Stopwatch.GetTimestamp();
            Task completion = compiler.WaitForExitAsync(cancellationToken);
            while (true)
            {
                try
                {
                    completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).GetAwaiter().GetResult();
                    break;
                }
                catch (TimeoutException)
                {
                    compiler.Refresh();
                    Console.WriteLine($"[assets:runtime] launcherPid={compiler.Id} state=waiting " +
                        $"elapsedMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} " +
                        $"launcherCpuMs={compiler.TotalProcessorTime.TotalMilliseconds:F1} " +
                        $"launcherWorkingSetBytes={compiler.WorkingSet64} launcherPrivateBytes={compiler.PrivateMemorySize64}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[assets:runtime] launcherPid={compiler.Id} state=cancelled action=terminate-process-tree");
            if (!compiler.HasExited)
            {
                try { compiler.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (compiler.HasExited) { }
            }
            compiler.WaitForExit();
            throw;
        }
    }
}
