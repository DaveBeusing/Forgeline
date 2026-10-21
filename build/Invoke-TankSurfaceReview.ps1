[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BeforeRuntime,
    [Parameter(Mandatory)][string]$AfterRuntime,
    [Parameter(Mandatory)][string]$ClientPath,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('Before','After','Both')][string]$Side = 'Both'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$capture = Join-Path $PSScriptRoot 'Invoke-RtsReferenceQualification.ps1'
$sides = if ($Side -eq 'Both') { @('Before','After') } else { @($Side) }
foreach ($name in $sides) {
    $runtime = if ($name -eq 'Before') { $BeforeRuntime } else { $AfterRuntime }
    $common = @{ Capture=$true; ClientPath=$ClientPath; RuntimeAssetRoot=$runtime;
        Warmups=8; Samples=32; RepeatCaptures=2 }
    $root = Join-Path $OutputRoot $name.ToLowerInvariant()
    & $capture @common -Scenes MainBattleTankProduction -Profiles Low,Medium,High `
        -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080,3840x2160 -OutputRoot "$root/gallery"
    & $capture @common -Scenes MainBattleTankSingle,MainBattleTankHundred,MainBattleTankThousand `
        -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -OutputRoot "$root/loads"
    foreach ($yaw in 0,90,180,270) {
        & $capture @common -Scenes MainBattleTankSingle -Profiles High -Zooms CloseTactical -Displays 1920x1080 `
            -InspectionDistanceMeters 30 -YawDegrees $yaw -OutputRoot "$root/inspection-$yaw"
    }
    foreach ($weather in 'dark','wet') {
        $settings = if ($weather -eq 'dark') { @{WorldDaylight=.1} } else { @{CloudCover=.8;Wetness=1} }
        & $capture @common @settings -Scenes MainBattleTankProduction -Profiles High `
            -Zooms CloseTactical,NormalGameplay -Displays 1920x1080 -OutputRoot "$root/$weather"
    }
    foreach ($seconds in 1.55,1.60,1.72,1.90) {
        & $capture @common -Scenes MainBattleTankSingle -Profiles High -Zooms CloseTactical -Displays 1920x1080 `
            -InspectionDistanceMeters 30 -MechanicalSeconds $seconds -OutputRoot "$root/pose-$seconds"
    }
    foreach ($zoom in 'CloseTactical','NormalGameplay') {
        & $capture @common -Scenes MainBattleTankSingle -Profiles High -Zooms $zoom -Displays 1920x1080 `
            -CameraPath Sweep -OutputRoot "$root/sweep-$zoom"
    }

}
