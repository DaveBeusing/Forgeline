[CmdletBinding()]
param(
    [string]$OutputRoot = 'artifacts/reference-performance',
    [ValidateSet('Warm','RendererCold')][string]$RunProfile = 'Warm',
    [ValidateSet('Fixed','Sweep','Traverse')][string]$CameraPath = 'Sweep',
    [ValidateSet('Representative','Vehicles1000','Scaling')][string]$Preset = 'Representative',
    [ValidateRange(3,4096)][int]$Warmups = 64,
    [ValidateRange(8,8192)][int]$Samples = 512,
    [ValidateSet('Low','Medium','High','Ultra')][string[]]$Profiles = @('Medium'),
    [ValidateSet('640x360','1920x1080','2560x1440','3840x2160','3440x1440')][string[]]$Displays = @('1920x1080'),
    [ValidateRange(.5,1)][float]$ResolutionScale = 1,
    [ValidateSet('','Full','Half','Quarter')][string]$TextureQuality = '',
    [ValidateSet('Inherit','Default','Upload')][string]$StaticUploadFallback = 'Inherit',
    [ValidateSet('Current')][string]$SubmissionMode = 'Current',
    [switch]$Software,
    [string]$ClientPath,
    [string]$RuntimeAssetRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = [IO.Path]::GetFullPath($OutputRoot, $repositoryRoot)
if (Test-Path -LiteralPath $root) { throw 'Use a fresh paired-run output directory.' }
$scenes = if ($Preset -eq 'Vehicles1000') { @('CombatSupportRepeated') } else { @('IndustrialBase','BattlefieldCombat') }
if ($Preset -eq 'Scaling') { $scenes = @('Scaling1000High','Scaling1000Low','Scaling10000High','Scaling10000Low','Scaling100000High','Scaling100000Low','ScalingTerrainOnly') }
$zooms = if ($Preset -eq 'Scaling') { @('NormalGameplay') } else { @('CloseTactical','Strategic') }
$arguments = @{Capture=$true;Software=$Software;Scenes=$scenes;Profiles=$Profiles;Zooms=$zooms;Displays=$Displays;
    ResolutionScale=$ResolutionScale;TextureQuality=$TextureQuality;RunProfile=$RunProfile;CameraPath=$CameraPath;Warmups=$Warmups;Samples=$Samples;RepeatCaptures=2;ClientPath=$ClientPath;RuntimeAssetRoot=$RuntimeAssetRoot;StaticUploadFallback=$StaticUploadFallback}
# Independent processes; compilation/builds must have finished before starting this lane.
& "$PSScriptRoot/Invoke-RtsReferenceQualification.ps1" @arguments -OutputRoot (Join-Path $root 'run-1')
& "$PSScriptRoot/Invoke-RtsReferenceQualification.ps1" @arguments -OutputRoot (Join-Path $root 'run-2')
& "$PSScriptRoot/Compare-ReferencePerformance.ps1" -CurrentRoot (Join-Path $root 'run-2') -BaselineRoot (Join-Path $root 'run-1') -ReportPath (Join-Path $root 'variance.json')
