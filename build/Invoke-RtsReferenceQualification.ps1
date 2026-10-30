[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [int]$RenderStressInstances = 1000,
    [string]$OutputRoot = 'artifacts/rts-reference',
    [switch]$Capture,
    [switch]$Software,
    [ValidateSet('Inherit','Default','Upload')][string]$StaticUploadFallback = 'Inherit',
    [ValidatePattern('^[^,]+$')][string[]]$Scenes = @('DaylightTerrain','IndustrialBase','FormationConvoy','LogisticsCorridor','DamageDestruction','BattlefieldCombat'),
    [ValidatePattern('^[^,]+$')][string[]]$Profiles = @('Low','Medium','High','Ultra'),
    [ValidatePattern('^[^,]+$')][string[]]$Zooms = @('CloseTactical','NormalGameplay','Strategic'),
    [ValidateSet('640x360','1280x720','1920x1080','2560x1440','3840x2160','3440x1440')][string[]]$Displays = @('1920x1080','2560x1440','3840x2160','3440x1440'),
    [ValidatePattern('^[^,]+$')][string[]]$UiScenarios = @('World'),
    [ValidateScript({ $_ -in @(.75,1,2) })][float[]]$UiScales = @(1),
    [switch]$CinematicFrontend,
    [ValidateRange(3,4096)][int]$Warmups = 32,
    [ValidateRange(8,8192)][int]$Samples = 128,
    [ValidateRange(2,128)][int]$RepeatCaptures = 8,
    [switch]$ContinueAfterMismatch,
    [ValidateSet(0,96,120,144,192)][uint]$RasterDpi = 0,
    [ValidateRange(0,360)][float]$YawDegrees = 45,
    [ValidateRange(0,120)][float]$InspectionDistanceMeters = 0,
    [Nullable[double]]$MechanicalSeconds = $null,
    [ValidateSet('Warm','RendererCold')][string]$RunProfile = 'Warm',
    [ValidateSet('Fixed','Sweep','Traverse')][string]$CameraPath = 'Fixed',
    [ValidateRange(0,1)][float]$WorldDaylight = 1,
    [ValidateRange(0,1)][float]$CloudCover = 0,
    [ValidateRange(0,1)][float]$Wetness = 0,
    [ValidateRange(0,1)][float]$VegetationDensity = 1,
    [ValidateRange(100,4000)][float]$VegetationDistance = 4000,
    [ValidateRange(1,4096)][int]$TerrainResidentChunks = 128,
    [switch]$EagerTerrain,
    [switch]$LocalizedHaze,
    [switch]$SurfaceTracks,
    [ValidateRange(.5,1)][float]$ResolutionScale = 1,
    [ValidateSet('','Full','Half','Quarter')][string]$TextureQuality = '',
    [ValidateSet('','Disabled','Low','High')][string]$Antialiasing = '',
    [ValidateSet('','Disabled','Low','Medium','High')][string]$ShadowQuality = '',
    [ValidateSet('','Disabled','Distance','Height')][string]$AtmosphereQuality = '',
    [ValidateSet('','Disabled','Low','Medium','High')][string]$ParticleQuality = '',
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
    $graphicsSettings = @{ resolutionScale = $ResolutionScale;
        environment = @{daylight=$WorldDaylight;cloudCover=$CloudCover;wetness=$Wetness};
        vegetation = @{density=$VegetationDensity;maximumDistanceMeters=$VegetationDistance};
        terrainResidency = @{enabled= !$EagerTerrain;maximumResidentChunks=$TerrainResidentChunks} }
    foreach ($choice in @(@('textures',$TextureQuality),@('antialiasing',$Antialiasing),@('shadows',$ShadowQuality),
        @('atmosphere',$AtmosphereQuality),@('particles',$ParticleQuality))) {
        if ($choice[1]) { $graphicsSettings[$choice[0]] = $choice[1] }
    }
    $request = @{
        graphicsSettings = $graphicsSettings
        localHaze = $(if($LocalizedHaze){@{center=@{x=900;y=15;z=750};radiusMeters=450;density=.001}}else{$null})
        surfaceMarks = @($(if($SurfaceTracks){@(
            @{kind='TireTrack';start=@{x=630;y=505};end=@{x=690;y=535};widthMeters=3;strength=.35},
            @{kind='TrackedVehicle';start=@{x=810;y=690};end=@{x=980;y=840};widthMeters=5;strength=.45},
            @{kind='RoadWear';start=@{x=880;y=510};end=@{x=1030;y=600};widthMeters=8;strength=.35})}))
        outputRoot = $referenceRoot; commit = $commit
        runtimeRoot = $(if ($RuntimeAssetRoot) { [IO.Path]::GetFullPath($RuntimeAssetRoot) } else { Join-Path $repositoryRoot 'assets/runtime' })
        runProfile = $RunProfile; cameraPath = $CameraPath
        driver = $drivers; software = [bool]$Software; warmups = $Warmups; samples = $Samples; repeatCaptures = $RepeatCaptures; continueAfterMismatch = [bool]$ContinueAfterMismatch; rasterDpi = $RasterDpi; yawDegrees = $YawDegrees; inspectionDistanceMeters = $InspectionDistanceMeters; mechanicalSeconds = $MechanicalSeconds
        scenes = $Scenes; profiles = $Profiles; zooms = $Zooms
        uiScenarios = $UiScenarios; uiScales = $UiScales; cinematicFrontend = [bool]$CinematicFrontend
        displays = @($Displays | ForEach-Object { $parts = $_.Split('x'); @{ width = [int]$parts[0]; height = [int]$parts[1] } })
    }
    $requestPath = "$referenceRoot.request.json"
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($requestPath)) -Force | Out-Null
    $stream = [IO.File]::Open($requestPath, [IO.FileMode]::CreateNew)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Depth 8)); $stream.Write($bytes) }
    finally { $stream.Dispose() }
    $oldFallback = [Environment]::GetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK', 'Process')
    try {
        if ($StaticUploadFallback -eq 'Default') { Remove-Item Env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK -ErrorAction SilentlyContinue }
        elseif ($StaticUploadFallback -eq 'Upload') { [Environment]::SetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK', '1', 'Process') }
    if ($ClientPath) {
        # A GUI apphost invoked outside a pipeline can return before its process exits.
        & ([IO.Path]::GetFullPath($ClientPath)) --reference-capture-request $requestPath | Out-Host
    } else {
        & dotnet run --project (Join-Path $repositoryRoot 'src/ForgeLine.Client/ForgeLine.Client.csproj') `
            --configuration $Configuration --no-build -- --reference-capture-request $requestPath
    }
    if ($LASTEXITCODE -ne 0) { throw "Native reference capture failed; inspect $referenceRoot/failure.json and client output." }
    } finally {
        if ($null -eq $oldFallback) { Remove-Item Env:FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable('FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK', $oldFallback, 'Process') }
    }

    & "$PSScriptRoot/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $referenceRoot -BaselineRoot $BaselineRoot
    return
}
if ($StaticUploadFallback -ne 'Inherit' -or $null -ne $MechanicalSeconds -or $Software -or $BaselineRoot -or $RasterDpi -or $YawDegrees -ne 45 -or $InspectionDistanceMeters -ne 0 -or $RunProfile -ne 'Warm' -or $CameraPath -ne 'Fixed' -or $RuntimeAssetRoot -or $ClientPath -or $CinematicFrontend -or ($UiScenarios -join ',') -ne 'World' -or ($UiScales -join ',') -ne '1') { throw 'Capture options require explicit -Capture mode.' }
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
