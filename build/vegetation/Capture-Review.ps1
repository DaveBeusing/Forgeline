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
$common = @{ Capture = $true; Configuration = 'Release'; ClientPath = [IO.Path]::GetFullPath($ClientPath, $repo);
             Warmups = 32; Samples = 128; RepeatCaptures = 8; ContinueAfterMismatch = $true }
$cohorts = @(
    @{ Name = 'groves-and-loads'; Scenes = @('VegetationSingle','VegetationGrove','VegetationRepeated100','VegetationRepeated1000'); Profiles = @('Low','Medium','High'); Zooms = @('CloseTactical','NormalGameplay','Strategic'); Displays = @('1920x1080','3840x2160') },
    @{ Name = 'density-half'; Scenes = @('VegetationRepeated1000'); Profiles = @('Low','Medium','High'); Zooms = @('CloseTactical','NormalGameplay','Strategic'); Displays = @('1920x1080'); VegetationDensity = .5 },
    @{ Name = 'dark'; Scenes = @('VegetationGrove'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080','3840x2160'); WorldDaylight = .15; CloudCover = .8 },
    @{ Name = 'wet'; Scenes = @('VegetationGrove'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080','3840x2160'); CloudCover = .65; Wetness = 1 },
    @{ Name = 'traverse-full'; Scenes = @('VegetationRepeated1000'); Profiles = @('Medium'); Zooms = @('NormalGameplay'); Displays = @('2560x1440'); CameraPath = 'Traverse' },
    @{ Name = 'traverse-half'; Scenes = @('VegetationRepeated1000'); Profiles = @('Medium'); Zooms = @('NormalGameplay'); Displays = @('2560x1440'); CameraPath = 'Traverse'; VegetationDensity = .5 }
)
foreach ($yaw in @(0,90,180,270)) {
    $cohorts += @{ Name = ('bark-' + $yaw); Scenes = @('VegetationSingle'); Profiles = @('High'); Zooms = @('CloseTactical'); Displays = @('1920x1080'); InspectionDistanceMeters = 40; YawDegrees = $yaw }
}
$results = @()
foreach ($cohort in $cohorts) {
    $name = $cohort.Name
    $settings = @{}
    foreach ($key in $cohort.Keys) { if ($key -ne 'Name') { $settings[$key] = $cohort[$key] } }
    $before = Join-Path $output "$name/before"
    $after = Join-Path $output "$name/after"
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($BaselineRuntime, $repo)) -OutputRoot $before
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($CandidateRuntime, $repo)) -OutputRoot $after -BaselineRoot $before
    & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot $after -BaselineRoot $before -ComparePixels -ReportPath (Join-Path $output "$name/pixels.json")
    $results += @{ name = $name; before = $before; after = $after; pixels = (Join-Path $output "$name/pixels.json") }
}
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); cohorts = $results;
   structuralCommandsCompleted = $true; warmups = 32; samples = 128; repeatCaptures = 8;
   visualCandidate = $true; performanceAcceptance = 'pending-controlled-physical-pairs';
   windTemporalImages = 'fixed-phase-only'; cameraMotion = 'Traverse-v2-and-eight-adjacent-poses';
   sustainedLifecycleAcceptance = 'pending'; humanArtApproval = 'pending' } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8NoBOM
