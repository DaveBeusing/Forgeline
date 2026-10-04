[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$RenderStressInstances = 1000,
    [string]$SettingsRoot = "artifacts/window-mode-settings",
    [string]$WindowedReport = "artifacts/visual-qualification.json",
    [string]$BorderlessReport = "artifacts/borderless-startup-qualification.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$clientProject = Join-Path $repositoryRoot "src/ForgeLine.Client/ForgeLine.Client.csproj"
$settingsRootPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $SettingsRoot))
$windowedReportPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $WindowedReport))
$borderlessReportPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $BorderlessReport))

function Invoke-ClientSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReportPath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedMode
    )

    $dotnetArguments = @(
        "run",
        "--project", $clientProject,
        "--configuration", $Configuration,
        "--no-build",
        "--",
        "--smoke-test",
        "--render-stress", $RenderStressInstances,
        "--settings-root", $settingsRootPath,
        "--visual-qualification-output", $ReportPath
    )

    & dotnet @dotnetArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Client smoke qualification failed for expected mode '$ExpectedMode' with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $ReportPath)) {
        throw "Client smoke qualification did not create report '$ReportPath'."
    }

    $report = Get-Content -Path $ReportPath -Raw | ConvertFrom-Json -Depth 32
    $surface = $report.metrics.surface

    if ($report.settings.requestedMode -ne $ExpectedMode) {
        throw "Expected requested window mode '$ExpectedMode' but report contains '$($report.settings.requestedMode)'."
    }

    if ($report.window.mode -ne $ExpectedMode) {
        throw "Expected active window mode '$ExpectedMode' but report contains '$($report.window.mode)'."
    }

    if (-not $report.window.isOpen) {
        throw "Qualified client window was not open when the final report was captured."
    }

    if ($report.window.isMinimized) {
        throw "Qualified client window remained minimized at the end of the smoke run."
    }

    if ($report.window.clientWidth -le 0 -or $report.window.clientHeight -le 0) {
        throw "Qualified client window reported invalid client dimensions $($report.window.clientWidth)x$($report.window.clientHeight)."
    }

    if ($surface.isSuspended) {
        throw "Graphics surface remained suspended at the end of the '$ExpectedMode' smoke run."
    }

    if ($surface.resizePending) {
        throw "Graphics surface retained a pending resize at the end of the '$ExpectedMode' smoke run."
    }

    if ($surface.isOccluded) {
        throw "Graphics surface remained occluded at the end of the '$ExpectedMode' smoke run."
    }

    if ($surface.width -ne $report.window.clientWidth -or
        $surface.height -ne $report.window.clientHeight) {
        throw "Graphics surface $($surface.width)x$($surface.height) does not match final client size $($report.window.clientWidth)x$($report.window.clientHeight)."
    }

    if ($surface.submittedFrameCount -le 0) {
        throw "No graphics frames were submitted during the '$ExpectedMode' smoke run."
    }

    if ($surface.presentedFrameCount -le 0) {
        throw "No successful Presents were observed during the '$ExpectedMode' smoke run."
    }

    if ($surface.frameIndex -lt 0 -or
        $surface.frameIndex -ge $surface.bufferCount) {
        throw "Graphics surface reported invalid back-buffer index $($surface.frameIndex) for $($surface.bufferCount) buffers."
    }

    if ($surface.resizeGeneration -ne $surface.appliedResizeGeneration) {
        throw "Graphics resize generation $($surface.resizeGeneration) was not fully applied; last applied generation is $($surface.appliedResizeGeneration)."
    }

    Write-Host (
        "Window mode qualification passed: mode={0}; client={1}x{2}; surface={3}x{4}; submitted={5}; presented={6}; frameIndex={7}/{8}" -f
        $ExpectedMode,
        $report.window.clientWidth,
        $report.window.clientHeight,
        $surface.width,
        $surface.height,
        $surface.submittedFrameCount,
        $surface.presentedFrameCount,
        $surface.frameIndex,
        $surface.bufferCount
    )
}

if (Test-Path $settingsRootPath) {
    Remove-Item -Path $settingsRootPath -Recurse -Force
}

New-Item -Path $settingsRootPath -ItemType Directory -Force | Out-Null

Invoke-ClientSmoke -ReportPath $windowedReportPath -ExpectedMode "Windowed"

$settingsPath = Join-Path $settingsRootPath "FORGELINE/settings.json"
if (-not (Test-Path $settingsPath)) {
    throw "Default Windowed smoke did not create persisted settings at '$settingsPath'."
}

$settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json -Depth 32
$settings.borderlessFullscreen = $true
$settings | ConvertTo-Json -Depth 32 | Set-Content -Path $settingsPath -Encoding utf8

Invoke-ClientSmoke -ReportPath $borderlessReportPath -ExpectedMode "BorderlessFullscreen"
