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
             Warmups = 3; Samples = 8; RepeatCaptures = 2; ContinueAfterMismatch = $true }
$cohorts = @(
    @{ Name = 'gallery'; Scenes = @('ResourceDepositsGallery'); Profiles = @('High'); Zooms = @('CloseTactical','NormalGameplay','Strategic'); Displays = @('1920x1080','3840x2160') },
    @{ Name = 'quality'; Scenes = @('ResourceDepositsGallery'); Profiles = @('Low','Medium'); Zooms = @('NormalGameplay'); Displays = @('1920x1080') },
    @{ Name = 'loads'; Scenes = @('ResourceDepositSingle','ResourceDeposits100','ResourceDeposits1000'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080') },
    @{ Name = 'strategic'; Scenes = @('ResourceDeposits1000'); Profiles = @('Low'); Zooms = @('Strategic'); Displays = @('1920x1080') },
    @{ Name = 'dark'; Scenes = @('ResourceDepositsGallery'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080'); WorldDaylight = .15; CloudCover = .8 },
    @{ Name = 'wet'; Scenes = @('ResourceDepositsGallery'); Profiles = @('High'); Zooms = @('NormalGameplay'); Displays = @('1920x1080'); CloudCover = .65; Wetness = 1 }
)
foreach ($cohort in $cohorts) {
    $settings = @{}
    foreach ($key in $cohort.Keys) { if ($key -ne 'Name') { $settings[$key] = $cohort[$key] } }
    $before = Join-Path $output "$($cohort.Name)/before"
    $after = Join-Path $output "$($cohort.Name)/after"
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($BaselineRuntime, $repo)) -OutputRoot $before
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($CandidateRuntime, $repo)) -OutputRoot $after -BaselineRoot $before
    & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot $after -BaselineRoot $before -ComparePixels -ReportPath (Join-Path $output "$($cohort.Name)/pixels.json")
}
@{ schemaVersion = 1; sourceSha = (& git -C $repo rev-parse HEAD); functionalCommandsCompleted = $true;
   warmups = 3; samples = 8; repeatCaptures = 2; performanceAcceptance = 'pending-controlled-physical-measurements';
   fourTerrainMaterials = 'pending'; humanArtApproval = 'pending' } |
    ConvertTo-Json | Set-Content (Join-Path $output 'summary.json') -Encoding utf8NoBOM
