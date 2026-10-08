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

            WaitForCompiler(compiler, cancellationToken);
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
            compiler.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
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
