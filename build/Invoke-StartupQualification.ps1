[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientPath,
    [string]$OutputRoot = "artifacts/startup-qualification",
    [ValidateRange(1, 100)][int]$Samples = 5,
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 60,
    [switch]$IncludeStudioIntro,
    [switch]$BaselineExitHook
)

$ErrorActionPreference = "Stop"
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
    try {
        while (-not $process.WaitForExit(10)) {
            $process.Refresh()
            if ($null -eq $windowObserved -and $process.MainWindowHandle -ne 0) {
                $windowObserved = $clock.Elapsed.TotalMilliseconds
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
        $observations += [PSCustomObject]@{ sample = $sample; warmup = ($sample -eq 0);
            launchToWindowObservationMs = $windowObserved; launchToExitMs = $clock.Elapsed.TotalMilliseconds;
            phases = $phases; report = $reportPath }
    }
    finally {
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
    includesStudioIntro = [bool]$IncludeStudioIntro; baselineExitHook = [bool]$BaselineExitHook;
    observations = $observations }
$summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $destination 'summary.json')
Write-Output "Qualified $Samples menu launches after one warmup: $destination"
