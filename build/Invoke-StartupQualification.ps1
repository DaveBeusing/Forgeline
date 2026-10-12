[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientPath,
    [string]$OutputRoot = "artifacts/startup-qualification",
    [ValidateRange(1, 100)][int]$Samples = 5,
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 60,
    [switch]$IncludeStudioIntro,
    [switch]$BaselineExitHook,
    [switch]$RequireApplicationIcons
)

$ErrorActionPreference = "Stop"
if ($RequireApplicationIcons -and -not ('StartupWindowIconProbe' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class StartupWindowIconProbe
{
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    public static ulong Read(IntPtr window, uint size)
    {
        UIntPtr icon;
        if (SendMessageTimeout(window, 0x007F, (UIntPtr)size, IntPtr.Zero, 2, 500, out icon) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Window did not answer the icon probe.");
        return icon.ToUInt64();
    }
}
"@
}
$client = (Resolve-Path -LiteralPath $ClientPath).Path
$destination = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$observations = @()
for ($sample = 0; $sample -le $Samples; $sample++) {
    $reportPath = Join-Path $destination "launch-$sample.json"
    $start = [Diagnostics.ProcessStartInfo]::new($client)
    $start.WorkingDirectory = Split-Path -Parent $client
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('--settings-root', (Join-Path $destination 'settings'),
        '--startup-diagnostics-output', $reportPath)) { $start.ArgumentList.Add($argument) }
    if (-not $IncludeStudioIntro) { $start.ArgumentList.Add('--skip-splash') }
    if ($BaselineExitHook) { $start.Environment['FORGELINE_STARTUP_MENU_QUALIFICATION'] = '1' }
    else { $start.ArgumentList.Add('--startup-menu-smoke-test') }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $windowObserved = $null
    $iconWindow = [IntPtr]::Zero
    $iconChecks = 0
    try {
        while (-not $process.WaitForExit(10)) {
            $process.Refresh()
            if ($null -eq $windowObserved -and $process.MainWindowHandle -ne 0) {
                $windowObserved = $clock.Elapsed.TotalMilliseconds
            }
            if ($RequireApplicationIcons -and $process.MainWindowHandle -ne 0) {
                $window = $process.MainWindowHandle
                if ($iconWindow -ne [IntPtr]::Zero -and $iconWindow -ne $window) {
                    throw "Startup sample $sample replaced its application window."
                }
                $iconWindow = $window
                # A successful menu smoke may close between polling and SendMessageTimeout.
                try {
                    if ([StartupWindowIconProbe]::Read($window, 0) -eq 0 -or
                        [StartupWindowIconProbe]::Read($window, 1) -eq 0) {
                        throw "Startup sample $sample lacks native small/large application icons."
                    }
                    $iconChecks++
                }
                catch {
                    if (-not $process.WaitForExit(100)) { throw }
                }
            }
            if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                $process.Kill($true)
                $process.WaitForExit()
                throw "Startup sample $sample timed out; retain its pending checkpoint."
            }
        }
        if ($process.ExitCode -ne 0) { throw "Startup sample $sample exited $($process.ExitCode)." }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -Depth 32
        if ($report.ApplicationReadiness -ne 'Ready' -or $report.SessionReadiness -ne 'NotRequested' -or
            $report.DroppedEvents -ne 0) { throw "Startup sample $sample did not qualify an interactive menu." }
        $phases = @{}
        foreach ($phase in @('WindowVisible', 'NativeBootstrapFirstFrame', 'GraphicsReady',
            'FirstPresentedFrame', 'RuntimeAssetsReady', 'StudioSplashFirstFrame', 'MainMenuInteractive')) {
            $event = $report.Events | Where-Object { $_.Phase -eq $phase -and $_.Kind -eq 'Milestone' } | Select-Object -First 1
            $phases[$phase] = if ($null -ne $event) { $event.ElapsedMilliseconds } else { $null }
        }
        if (-not $BaselineExitHook) {
            $nativeFrame = $phases['NativeBootstrapFirstFrame']
            if ($null -eq $nativeFrame -or $null -eq $phases['GraphicsReady'] -or
                $null -eq $phases['FirstPresentedFrame'] -or $nativeFrame -ge $phases['FirstPresentedFrame']) {
                throw "Startup sample $sample did not paint native feedback before graphics presentation."
            }
            foreach ($event in $report.Events) {
                if ($event.Kind -eq 'Started' -and $event.Phase -in @('Settings', 'BootRenderer', 'RuntimeAssets') -and
                    $event.ElapsedMilliseconds -le $nativeFrame) {
                    throw "Startup sample $sample began $($event.Phase) before native painting."
                }
            }
        }
        if ($IncludeStudioIntro -and $null -eq $phases['StudioSplashFirstFrame']) {
            throw "Startup sample $sample did not present the requested studio video; inspect its bypass/failure evidence."
        }
        if ($RequireApplicationIcons -and $iconChecks -eq 0) { throw "Startup sample $sample has no native icon observations." }
        $observations += [PSCustomObject]@{ sample = $sample; warmup = ($sample -eq 0);
            launchToWindowObservationMs = $windowObserved; launchToExitMs = $clock.Elapsed.TotalMilliseconds;
            nativeIconChecks = $iconChecks; phases = $phases; report = $reportPath }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        if ($process.HasExited) {
            $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $destination "launch-$sample.stdout.log")
            $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $destination "launch-$sample.stderr.log")
        }
        $process.Dispose()
    }
}
$summary = [PSCustomObject]@{ client = $client; measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O');
    cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors);
    graphics = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion);
    cacheCondition = 'Warm repetitions after one excluded warmup; no cache eviction or reboot';
    requiresApplicationIcons = [bool]$RequireApplicationIcons;
    includesStudioIntro = [bool]$IncludeStudioIntro; baselineExitHook = [bool]$BaselineExitHook;
    observations = $observations }
$summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $destination 'summary.json')
Write-Output "Qualified $Samples menu launches after one warmup: $destination"
