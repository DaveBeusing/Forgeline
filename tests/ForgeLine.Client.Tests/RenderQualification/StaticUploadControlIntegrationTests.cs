using System.Diagnostics;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class StaticUploadControlIntegrationTests
{
    [Theory]
    [InlineData("Default", null)]
    [InlineData("Upload", null)]
    [InlineData("Inherit", null)]
    [InlineData("Default", "0")]
    [InlineData("Upload", "1")]
    public async Task FailedReferenceChildRestoresExactProcessControl(string mode, string? initial)
    {
        string? repository = AppContext.BaseDirectory;
        while (repository is not null && !File.Exists(Path.Combine(repository, "build", "Invoke-RtsReferenceQualification.ps1")))
            repository = Path.GetDirectoryName(repository.TrimEnd(Path.DirectorySeparatorChar));
        Assert.NotNull(repository);
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("forgeline-static-control-");
        string root = temporary.FullName;
        string capture = Path.Combine(root, "capture");
        string scriptPath = Path.Combine(repository, "build", "Invoke-RtsReferenceQualification.ps1");
        string setup = initial is null
            ? "Remove-Item Env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK -ErrorAction SilentlyContinue"
            : $"[Environment]::SetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK', {Quote(initial)}, 'Process')";
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            {{setup}}
            $before = [Environment]::GetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK','Process')
            $caught = $false
            try {
                & {{Quote(scriptPath)}} -Capture -Scenes @('ScalingTerrainOnly') -Profiles @('Medium') -Zooms @('NormalGameplay') -Displays @('1920x1080') -Warmups 3 -Samples 8 -RepeatCaptures 2 -StaticUploadFallback {{Quote(mode)}} -ClientPath {{Quote(Path.Combine(root, "missing.exe"))}} -OutputRoot {{Quote(capture)}} | Out-Null
            } catch { $caught = $true }
            if (-not $caught -or -not (Test-Path -LiteralPath {{Quote(capture + ".request.json")}})) {
                throw 'Reference invocation did not reach the controlled child failure.'
            }
            $after = [Environment]::GetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK','Process')
            if ($null -eq $before) {
                if ($null -ne $after) { throw 'An absent process control was not restored.' }
            } elseif ($before -cne $after) { throw 'The original process control value was not restored.' }
            Write-Output 'Exact fallback control restored after actual reference-child failure.'
            """;
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                string[] output = await Task.WhenAll(stdout, stderr);
                Assert.True(process.ExitCode == 0, string.Concat(output));
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
