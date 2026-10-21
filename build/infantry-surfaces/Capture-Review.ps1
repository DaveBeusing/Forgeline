#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaselineRuntime,
      [Parameter(Mandatory)][string]$CandidateRuntime,
      [Parameter(Mandatory)][string]$ClientPath,
      [Parameter(Mandatory)][string]$OutputRoot,
      [string[]]$SkipCohorts = @())
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
if (Test-Path -LiteralPath $output) { throw 'Use fresh evidence output.' }
$common = @{ Capture=$true; ClientPath=[IO.Path]::GetFullPath($ClientPath,$repo);
    Warmups=8; Samples=32; RepeatCaptures=8; ContinueAfterMismatch=$true }
$roles = @('InfantrySkinning','InfantrySkinningEngineer')
$loads = @('InfantryRifleSingle','InfantryRifleHundred','InfantryRifleThousand',
           'InfantryEngineerSingle','InfantryEngineerHundred','InfantryEngineerThousand')
$cohorts = @(
    @{Name='gallery';Scenes=$roles;Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080','3840x2160');InspectionDistanceMeters=20},
    @{Name='distances';Scenes=$roles;Profiles=@('High');Zooms=@('CloseTactical','NormalGameplay','Strategic');Displays=@('1920x1080')},
    @{Name='quality';Scenes=$roles;Profiles=@('Low','Medium');Zooms=@('NormalGameplay');Displays=@('1920x1080')},
    @{Name='loads';Scenes=$loads;Profiles=@('High');Zooms=@('NormalGameplay');Displays=@('1920x1080')},
    @{Name='close-loads';Scenes=$loads;Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080');InspectionDistanceMeters=40},
    @{Name='dark';Scenes=$roles;Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080');InspectionDistanceMeters=20;WorldDaylight=.15;CloudCover=.8},
    @{Name='wet';Scenes=$roles;Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080');InspectionDistanceMeters=20;Wetness=1;CloudCover=.65}
)
foreach ($phase in @(0,.25,.5,.75,1)) {
    $cohorts += @{Name="phase-$phase";Scenes=$roles;Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080');InspectionDistanceMeters=20;MechanicalSeconds=$phase}
}
$results = @()
foreach ($name in $SkipCohorts) {
    if ($name -notin $cohorts.Name) { throw "Unknown cohort: $name" }
}
foreach ($cohort in $cohorts) {
    if ($cohort.Name -in $SkipCohorts) { continue }
    $settings = @{MechanicalSeconds=.375}
    foreach ($key in $cohort.Keys) { if ($key -ne 'Name') { $settings[$key]=$cohort[$key] } }
    $before = Join-Path $output "$($cohort.Name)/before"
    $after = Join-Path $output "$($cohort.Name)/after"
    & "$repo/build/Invoke-RtsReferenceQualification.ps1" @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($BaselineRuntime,$repo)) -OutputRoot $before
    & "$repo/build/Invoke-RtsReferenceQualification.ps1" @common @settings -RuntimeAssetRoot ([IO.Path]::GetFullPath($CandidateRuntime,$repo)) -OutputRoot $after -BaselineRoot $before
    & "$repo/build/Compare-VisualReferenceEvidence.ps1" -CurrentRoot $after -BaselineRoot $before -ComparePixels -ReportPath (Join-Path $output "$($cohort.Name)/pixels.json")
    $results += @{name=$cohort.Name;before=$before;after=$after}
}
@{sourceSha=(& git -C $repo rev-parse HEAD);cohorts=$results;skippedCohorts=$SkipCohorts;warmups=8;samples=32;repeatCaptures=8;
    humanArtApproval='pending';controlledPerformanceAcceptance='pending';motionScope='sampled phases, not live movement approval'} |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'summary.json') -Encoding utf8NoBOM
