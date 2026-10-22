[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientPath,[string]$RuntimeAssetRoot='assets/runtime',[Parameter(Mandatory)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$capture=Join-Path $PSScriptRoot 'Invoke-RtsReferenceQualification.ps1'
$common=@{Capture=$true;ClientPath=$ClientPath;RuntimeAssetRoot=$RuntimeAssetRoot;Warmups=8;Samples=32;RepeatCaptures=2}
& $capture @common -Scenes EnvironmentKitBridge,InfrastructureRoadJunctions,InfrastructureBridgeStates,InfrastructureFordDeck,InfrastructureBridgeDeck -Profiles Low,Medium,High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080,3840x2160 -OutputRoot "$OutputRoot/main"
& $capture @common -Scenes InfrastructureCurveShort,InfrastructureCurveLong,InfrastructureTJunction,InfrastructureCrossJunction -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -OutputRoot "$OutputRoot/junctions"
& $capture @common -Scenes InfrastructureBridgeApproach,InfrastructureBridgeExit,InfrastructureFordApproach,InfrastructureFordExit -Profiles High -Zooms CloseTactical,NormalGameplay -Displays 1920x1080 -OutputRoot "$OutputRoot/traffic"
& $capture @common -Scenes InfrastructureSingle,InfrastructureHundred,InfrastructureThousand -Profiles High -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -OutputRoot "$OutputRoot/loads"
foreach($weather in @('dark','wet')){
 $settings=if($weather -eq 'dark'){@{WorldDaylight=.1}}else{@{CloudCover=.8;Wetness=1}}
 & $capture @common @settings -Scenes EnvironmentKitBridge,InfrastructureRoadJunctions,InfrastructureFordDeck -Profiles High -Zooms CloseTactical,NormalGameplay -Displays 1920x1080 -OutputRoot "$OutputRoot/$weather"
}
foreach($yaw in @(0,90)){
 & $capture @common -Scenes InfrastructureBridgeDeck,InfrastructureFordDeck,InfrastructureCurveLong -Profiles High -Zooms CloseTactical -Displays 1920x1080 -InspectionDistanceMeters 30 -YawDegrees $yaw -OutputRoot "$OutputRoot/detail-$yaw"
}
& $capture @common -Scenes EnvironmentKitBridge,InfrastructureRoadJunctions,InfrastructureFordDeck -Profiles High -Zooms NormalGameplay,Strategic -Displays 1920x1080 -CameraPath Sweep -OutputRoot "$OutputRoot/sweep"
& $capture @common -Scenes InfrastructureBridgeDeck,InfrastructureRoadJunctions -Profiles High -Zooms NormalGameplay -Displays 3440x1440 -OutputRoot "$OutputRoot/ultrawide"
