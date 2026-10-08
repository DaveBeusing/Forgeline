[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$RenderStressInstances = 1000,
    [string]$SettingsRoot = "artifacts/window-mode-settings",
    [string]$WindowedReport = "artifacts/visual-qualification.json",
    [string]$BorderlessReport = "artifacts/borderless-startup-qualification.json",
    [string]$AssetQualificationReport = "artifacts/asset-qualification.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$clientProject = Join-Path $repositoryRoot "src/ForgeLine.Client/ForgeLine.Client.csproj"
$settingsRootPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $SettingsRoot))
$windowedReportPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $WindowedReport))
$borderlessReportPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $BorderlessReport))
$assetQualificationReportPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $AssetQualificationReport))

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

    $previousDebugLayer = $env:FORGELINE_D3D12_DEBUG_LAYER
    $env:FORGELINE_D3D12_DEBUG_LAYER = "1"
    try {
        & dotnet @dotnetArguments
    }
    finally {
        $env:FORGELINE_D3D12_DEBUG_LAYER = $previousDebugLayer
    }

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

    $metrics = $report.metrics

    if ($metrics.textureBindingFailureCount -ne 0 -or
        $metrics.materialBindingFailureCount -ne 0) {
        throw "Texture/material binding failures were reported during '$ExpectedMode' qualification."
    }

    if ($metrics.runtimeMeshInstances -le 0) {
        throw "No runtime mesh instances were rendered during '$ExpectedMode' qualification; production geometry may have fallen back to development primitives."
    }

    if ($metrics.texturedRuntimeMeshInstances -le 0) {
        throw "No textured runtime mesh instances were rendered during '$ExpectedMode' qualification; production material sampling was not exercised."
    }

    if ($metrics.visibleTerrainChunks -le 0 -or
        $metrics.terrainDrawCalls -le 0) {
        throw "No visible terrain draw was submitted during '$ExpectedMode' qualification."
    }

    if ($metrics.terrainTextureBindingsPerDraw -gt 13 -or
        $metrics.terrainMaximumTextureSamplesPerPixel -gt 13) {
        throw "Terrain texture/sample budget exceeded the four-layer baseline during '$ExpectedMode' qualification."
    }

    & (Join-Path $PSScriptRoot 'Validate-VisualDrawBudgets.ps1') -Metrics $metrics

    if ($metrics.peakResidentTextureBytes -gt 2097152) {
        throw "Peak resident texture bytes $($metrics.peakResidentTextureBytes) exceeded the 2 MiB Vertical Slice budget."
    }

    if ($metrics.peakShaderResourceDescriptorsUsed -gt 512) {
        throw "Peak SRV descriptor use $($metrics.peakShaderResourceDescriptorsUsed) exceeded the accepted budget of 512."
    }

    if ($metrics.textureUploadCount -ne
        ($metrics.loadedTextureCount + $metrics.textureReleaseCount)) {
        throw "Texture lifetime accounting indicates repeated or unbalanced uploads during '$ExpectedMode' qualification."
    }

    if ($metrics.shaderResourceDescriptorsUsed -ne
        $metrics.loadedTextureCount) {
        throw "Current SRV descriptor usage does not match the loaded texture count during '$ExpectedMode' qualification."
    }

    if ($metrics.gpuTimingAvailable -and
        $null -eq $metrics.gpuMilliseconds) {
        throw "GPU timing was reported available but no completed frame timing was published."
    }

    if ($metrics.debugLayerEnabled -and
        ($metrics.debugLayerWarningCount -ne 0 -or
         $metrics.debugLayerErrorCount -ne 0)) {
        throw "D3D12 debug-layer warnings/errors were reported during '$ExpectedMode' qualification."
    }

    if ($metrics.lightingDirectionalIntensity -le 0 -or
        $metrics.lightingAmbientIntensity -lt 0 -or
        $metrics.lightingExposure -le 0) {
        throw "Scene lighting/exposure diagnostics were invalid during '$ExpectedMode' qualification."
    }

    if ($metrics.lightingToneMapping -ne "AcesFitted") {
        throw "Expected AcesFitted scene tone mapping during '$ExpectedMode' qualification but observed '$($metrics.lightingToneMapping)'."
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

if (Test-Path $assetQualificationReportPath) {
    $assetQualification = Get-Content -Path $assetQualificationReportPath -Raw | ConvertFrom-Json -Depth 32

    $studioTextureBytes = [long]$assetQualification.studioSplashTextureRuntimeBytes
    $gameplayTextureBytes = [long]$assetQualification.textureRuntimeBytes - $studioTextureBytes

    if ($gameplayTextureBytes -gt 524288) {
        throw "Compiled gameplay texture footprint $gameplayTextureBytes bytes exceeded the 512 KiB Vertical Slice budget."
    }

    if ($studioTextureBytes -gt 3145728) {
        throw "Compiled startup studio texture footprint $studioTextureBytes bytes exceeded the 3 MiB intro budget."
    }

    Write-Host "Qualified texture budgets: gameplay=$gameplayTextureBytes bytes (512 KiB max); startupStudio=$studioTextureBytes bytes (3 MiB max)."
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
