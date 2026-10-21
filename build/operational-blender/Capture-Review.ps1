#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$RuntimeAssetRoot,
    [string]$BaselineRoot,
    [string]$ClientPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath('../..', $PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputRoot, $repo)
$allowed = [IO.Path]::GetFullPath('artifacts', $repo) + [IO.Path]::DirectorySeparatorChar
if (-not ($output + [IO.Path]::DirectorySeparatorChar).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Review output must remain in repository artifacts.' }
if (Test-Path -LiteralPath $output) { throw 'Review output must be fresh.' }
New-Item -ItemType Directory -Path $output | Out-Null
$families = @('VehicleFactory','PowerPlant','Barracks','Radar')
$galleries = @($families | ForEach-Object { $_ + 'Production' })
$singles = @($families | ForEach-Object { $_ + 'Single' })
$hundreds = @($families | ForEach-Object { $_ + 'Repeated' })
$specifications = @(
    @{ Name = 'galleries'; Scenes = $galleries; Zooms = @('CloseTactical','NormalGameplay','Strategic') },
    @{ Name = 'single'; Scenes = $singles; Zooms = @('NormalGameplay') },
    @{ Name = 'hundred'; Scenes = $hundreds; Zooms = @('NormalGameplay') },
    @{ Name = 'thousand'; Scenes = @('OperationalBuildingsRepeated'); Zooms = @('CloseTactical','NormalGameplay','Strategic') },
    @{ Name = 'dark'; Scenes = $galleries; Zooms = @('NormalGameplay'); Profiles = @('High'); Displays = @('1920x1080'); WorldDaylight = .12; CloudCover = .6 },
    @{ Name = 'wet'; Scenes = $galleries; Zooms = @('NormalGameplay'); Profiles = @('High'); Displays = @('1920x1080'); CloudCover = 1; Wetness = 1 }
)
foreach ($yaw in @(0,90,180,270)) {
    $specifications += @{ Name = "side-$yaw"; Scenes = $singles; Zooms = @('CloseTactical'); Profiles = @('High'); Displays = @('1920x1080'); YawDegrees = $yaw }
}
foreach ($specification in $specifications) {
    $name = $specification.Name
    $arguments = @{
        Capture = $true; Profiles = @('Low','Medium','High'); Displays = @('1920x1080','3840x2160')
        Warmups = 8; Samples = 32; RepeatCaptures = 8
        RuntimeAssetRoot = [IO.Path]::GetFullPath($RuntimeAssetRoot, $repo)
        OutputRoot = (Join-Path $output $name)
    }
    foreach ($key in $specification.Keys) { if ($key -ne 'Name') { $arguments[$key] = $specification[$key] } }
    if ($BaselineRoot) { $arguments.BaselineRoot = Join-Path ([IO.Path]::GetFullPath($BaselineRoot, $repo)) $name }
    if ($ClientPath) { $arguments.ClientPath = [IO.Path]::GetFullPath($ClientPath, $repo) }
    & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @arguments
    if ($BaselineRoot) {
        & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot $arguments.OutputRoot -BaselineRoot $arguments.BaselineRoot -ComparePixels -ReportPath (Join-Path $arguments.OutputRoot 'pixel-observations.json')
    }
}
Write-Host "Operational review matrix completed: $output"
