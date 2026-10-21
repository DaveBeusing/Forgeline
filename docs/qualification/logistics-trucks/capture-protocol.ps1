param([Parameter(Mandatory)][ValidateSet('before','after')][string]$Stage)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$script:failures=[Collections.Generic.List[string]]::new()
$client=(Resolve-Path 'src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64/ForgeLine.Client.exe').Path
$runtime=(Resolve-Path 'assets/runtime').Path
function Capture-Cohort([string]$Name,[hashtable]$Options){
    $root="artifacts/trucks-$Stage/$Name"
    $params=@{Capture=$true;ClientPath=$client;RuntimeAssetRoot=$runtime;OutputRoot=$root;Warmups=16;Samples=64;RepeatCaptures=2;ContinueAfterMismatch=$true;Displays=@('1920x1080');Profiles=@('High');Zooms=@('NormalGameplay')}
    foreach($key in $Options.Keys){$params[$key]=$Options[$key]}
    if($Stage -eq 'after'){$params.BaselineRoot="artifacts/trucks-before/$Name"}
    try{
        & ./build/Invoke-RtsReferenceQualification.ps1 @params *> "artifacts/trucks-$Stage-$Name.log"
        if($Stage -eq 'after'){
            & ./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot $root -BaselineRoot $params.BaselineRoot -ComparePixels -ReportPath "$root/pixels.json" *> "artifacts/trucks-$Stage-$Name-pixels.log"
        }
        Write-Host "$Stage/$Name completed"
    }catch{$script:failures.Add("$Name : $($_.Exception.Message)");Write-Host "$Stage/$Name FAILED: $($_.Exception.Message)"}
}
Capture-Cohort 'gallery' @{Scenes=@('CargoTruckProduction','SupplyTruckProduction');Profiles=@('Low','Medium','High');Zooms=@('CloseTactical','NormalGameplay','Strategic');Displays=@('1920x1080','3840x2160')}
Capture-Cohort 'loads' @{Scenes=@('CargoTruckSingle','CargoTruckHundred','CargoTruckRepeated','SupplyTruckSingle','SupplyTruckHundred','SupplyTruckRepeated');Zooms=@('CloseTactical','Strategic')}
Capture-Cohort 'dark' @{Scenes=@('CargoTruckProduction','SupplyTruckProduction');Zooms=@('CloseTactical','NormalGameplay');WorldDaylight=.15}
Capture-Cohort 'wet' @{Scenes=@('CargoTruckProduction','SupplyTruckProduction');Zooms=@('CloseTactical','NormalGameplay');CloudCover=.8;Wetness=.8}
foreach($yaw in @(45,135,225,315)){Capture-Cohort "inspect-$yaw" @{Scenes=@('CargoTruckProduction','SupplyTruckProduction');Zooms=@('CloseTactical');InspectionDistanceMeters=70;YawDegrees=$yaw}}
Capture-Cohort 'traffic' @{Scenes=@('LogisticsLoadingProduction','LogisticsTransitProduction','LogisticsUnloadingProduction','LogisticsSustainedProduction')}
@{stage=$Stage;success=$script:failures.Count -eq 0;failures=@($script:failures)}|ConvertTo-Json|Set-Content "artifacts/trucks-$Stage-result.json" -Encoding utf8NoBOM
if($script:failures.Count){throw 'Native cohorts have failures; inspect per-cohort logs.'}
