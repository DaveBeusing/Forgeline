#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaselineRuntime,
      [Parameter(Mandatory)][string]$CandidateRuntime,
      [Parameter(Mandatory)][string]$ClientPath,
      [Parameter(Mandatory)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $output) { throw 'Use fresh evidence output.' }
$baseline = [IO.Path]::GetFullPath($BaselineRuntime, $repo)
$candidate = [IO.Path]::GetFullPath($CandidateRuntime, $repo)
$client = [IO.Path]::GetFullPath($ClientPath, $repo)
$common = @{ Capture = $true; Configuration = 'Release'; ClientPath = $client;
             Warmups = 8; Samples = 32; RepeatCaptures = 8; ContinueAfterMismatch = $true }
$cohorts = @(
    @{ Name = 'gallery'; Scenes = @('ScoutVehicleProduction'); Profiles = @('Low','Medium','High'); Zooms = @('CloseTactical','NormalGameplay','Strategic'); Displays = @('1920x1080','3840x2160') },
    @{ Name = 'loads'; Scenes = @('ScoutSingle','ScoutRepeated100','ScoutRepeated1000'); Profiles = @('Low','Medium','High'); Zooms = @('CloseTactical','NormalGameplay','Strategic'); Displays = @('1920x1080','3840x2160') },
    @{ Name = 'state'; Scenes = @('ScoutActive','ScoutSelected'); Profiles = @('High'); Zooms = @('CloseTactical','Strategic'); Displays = @('1920x1080','3840x2160') },
    @{ Name = 'dark'; Scenes = @('ScoutVehicleProduction'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080'); WorldDaylight = .15; CloudCover = .8 },
    @{ Name = 'wet'; Scenes = @('ScoutVehicleProduction'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080'); CloudCover = .65; Wetness = 1 },
    @{ Name = 'inspection'; Scenes = @('ScoutSingle','ScoutActive','ScoutSelected','ScoutRepeated100','ScoutRepeated1000'); Profiles = @('High'); Zooms = @('CloseTactical'); Displays = @('1920x1080'); InspectionDistanceMeters = 20 }
)
foreach ($yaw in @(0,90,180,270)) {
    $cohorts += @{ Name = ('side-' + $yaw); Scenes = @('ScoutSingle'); Profiles = @('High'); Zooms = @('CloseTactical'); Displays = @('1920x1080'); InspectionDistanceMeters = 20; YawDegrees = $yaw }
}
$results = @()
foreach ($cohort in $cohorts) {
    $name = $cohort.Name
    $settings = @{}
    foreach ($key in $cohort.Keys) { if ($key -ne 'Name') { $settings[$key] = $cohort[$key] } }
    $before = Join-Path $output "$name/before"
    $after = Join-Path $output "$name/after"
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot $baseline -OutputRoot $before
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot $candidate -OutputRoot $after -BaselineRoot $before
    & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot $after -BaselineRoot $before -ComparePixels -ReportPath (Join-Path $output "$name/pixels.json")
    $results += @{ name = $name; before = $before; after = $after; pixels = (Join-Path $output "$name/pixels.json") }
}
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); cohorts = $results;
   structuralCommandsCompleted = $true; warmups = 8; samples = 32; repeatCaptures = 8;
   performanceAcceptance = 'pending-controlled-physical-measurements'; humanArtApproval = 'pending' } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8NoBOM
