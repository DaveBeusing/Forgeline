[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [int]$RenderStressInstances = 1000,
    [string]$OutputRoot = 'artifacts/rts-reference',
    [switch]$Capture,
    [switch]$Software,
    [string[]]$Scenes = @('DaylightTerrain','IndustrialBase','FormationConvoy','LogisticsCorridor','DamageDestruction','BattlefieldCombat'),
    [string[]]$Profiles = @('Low','Medium','High'),
    [string[]]$Zooms = @('CloseTactical','NormalGameplay','Strategic'),
    [ValidateSet('640x360','1920x1080','2560x1440','3840x2160','3440x1440')][string[]]$Displays = @('1920x1080','2560x1440','3840x2160','3440x1440'),
    [ValidateRange(3,4096)][int]$Warmups = 32,
    [ValidateRange(8,8192)][int]$Samples = 128,
    [ValidateRange(2,128)][int]$RepeatCaptures = 8,
    [switch]$ContinueAfterMismatch,
    [ValidateSet(0,96,120,144,192)][uint]$RasterDpi = 0,
    [ValidateRange(0,360)][float]$YawDegrees = 45,
    [ValidateRange(0,120)][float]$InspectionDistanceMeters = 0,
    [ValidateSet('Warm','RendererCold')][string]$RunProfile = 'Warm',
    [ValidateSet('Fixed','Sweep')][string]$CameraPath = 'Fixed',
    [string]$RuntimeAssetRoot,
    [string]$ClientPath,
    [string]$BaselineRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$referenceRoot = [IO.Path]::GetFullPath($OutputRoot, $repositoryRoot)
if ($Capture) {
    # Source identity and runtime payloads must represent the binaries being reviewed.
    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve capture source identity.' }
    if ((& git -C $repositoryRoot status --porcelain)) { throw 'Commit capture changes and rebuild before exporting acceptance evidence.' }
    if (Test-Path -LiteralPath $referenceRoot) { throw 'Use a new evidence directory; existing references are never overwritten.' }
    $drivers = @(Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name): $($_.DriverVersion)" }) -join '; '
    $request = @{
        outputRoot = $referenceRoot; commit = $commit
        runtimeRoot = $(if ($RuntimeAssetRoot) { [IO.Path]::GetFullPath($RuntimeAssetRoot) } else { Join-Path $repositoryRoot 'assets/runtime' })
        runProfile = $RunProfile; cameraPath = $CameraPath
        driver = $drivers; software = [bool]$Software; warmups = $Warmups; samples = $Samples; repeatCaptures = $RepeatCaptures; continueAfterMismatch = [bool]$ContinueAfterMismatch; rasterDpi = $RasterDpi; yawDegrees = $YawDegrees; inspectionDistanceMeters = $InspectionDistanceMeters
        scenes = $Scenes; profiles = $Profiles; zooms = $Zooms
        displays = @($Displays | ForEach-Object { $parts = $_.Split('x'); @{ width = [int]$parts[0]; height = [int]$parts[1] } })
    }
    $requestPath = "$referenceRoot.request.json"
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($requestPath)) -Force | Out-Null
    $stream = [IO.File]::Open($requestPath, [IO.FileMode]::CreateNew)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Depth 8)); $stream.Write($bytes) }
    finally { $stream.Dispose() }
    if ($ClientPath) {
        & ([IO.Path]::GetFullPath($ClientPath)) --reference-capture-request $requestPath
    } else {
        & dotnet run --project (Join-Path $repositoryRoot 'src/ForgeLine.Client/ForgeLine.Client.csproj') `
            --configuration $Configuration --no-build -- --reference-capture-request $requestPath
    }
    if ($LASTEXITCODE -ne 0) { throw "Native reference capture failed; inspect $referenceRoot/failure.json and client output." }
    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $referenceRoot -BaselineRoot $BaselineRoot
    return
}
if ($Software -or $BaselineRoot -or $RasterDpi -or $YawDegrees -ne 45 -or $InspectionDistanceMeters -ne 0 -or $RunProfile -ne 'Warm' -or $CameraPath -ne 'Fixed' -or $RuntimeAssetRoot -or $ClientPath) { throw 'Capture options require explicit -Capture mode.' }
$displays = @(@(1920, 1080), @(2560, 1440), @(3840, 2160), @(3440, 1440))
foreach ($display in $displays) {
    foreach ($zoom in @('CloseTactical', 'NormalGameplay', 'Strategic')) {
        $name = "$($display[0])x$($display[1])-$zoom"
        $settingsRoot = Join-Path $referenceRoot "$name/settings"
        $settingsDirectory = Join-Path $settingsRoot 'FORGELINE'
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        @{
            schemaVersion = 1; windowWidth = $display[0]; windowHeight = $display[1]
            uiScale = 1.0; edgeScrollEnabled = $false; showOnboarding = $false; showStudioSplash = $false
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settingsDirectory 'settings.json') -Encoding utf8
        $reportPath = Join-Path $referenceRoot "$name/metrics.json"
        & dotnet run --project (Join-Path $repositoryRoot 'src/ForgeLine.Client/ForgeLine.Client.csproj') `
            --configuration $Configuration --no-build -- --smoke-test --render-stress $RenderStressInstances `
            --reference-zoom $zoom --settings-root $settingsRoot --visual-qualification-output $reportPath
        if ($LASTEXITCODE -ne 0) { throw "Reference qualification failed for $name." }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.metrics.textureBindingFailureCount -ne 0 -or $report.metrics.materialBindingFailureCount -ne 0) {
            throw "Runtime resource binding failures in $name."
        }
        if ($report.metrics.surface.width -ne $display[0] -or $report.metrics.surface.height -ne $display[1]) {
            Write-Warning "Display limited $name to $($report.metrics.surface.width)x$($report.metrics.surface.height); requested resolution is unqualified."
        }
    }
}
