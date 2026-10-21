#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath('..',$PSScriptRoot)
$root=Join-Path $repo 'artifacts/sn3'
if(Test-Path -LiteralPath $root){throw 'Fresh evidence output required.'}
$client=Join-Path $repo '../scout-install3/player/ForgeLine.Client.exe'
$baseline=Join-Path $repo 'assets/runtime'
$candidate=Join-Path $repo '../scout-install3/player/assets/runtime'
$cases=@(
 @{Name='gallery';Scenes=@('ScoutVehicleProduction');Profiles=@('High');Zooms=@('NormalGameplay');Displays=@('1920x1080')},
 @{Name='states';Scenes=@('ScoutSingle','ScoutActive','ScoutSelected');Profiles=@('High');Zooms=@('CloseTactical');Displays=@('1920x1080');InspectionDistanceMeters=20},
 @{Name='hundred';Scenes=@('ScoutRepeated100');Profiles=@('High');Zooms=@('NormalGameplay');Displays=@('1920x1080')},
 @{Name='thousand-close';Scenes=@('ScoutRepeated1000');Profiles=@('Low');Zooms=@('CloseTactical');Displays=@('3840x2160')},
 @{Name='thousand-strategic';Scenes=@('ScoutRepeated1000');Profiles=@('High');Zooms=@('Strategic');Displays=@('3840x2160')},
 @{Name='dark';Scenes=@('ScoutVehicleProduction');Profiles=@('High');Zooms=@('NormalGameplay');Displays=@('1920x1080');WorldDaylight=.15;CloudCover=.8},
 @{Name='wet';Scenes=@('ScoutVehicleProduction');Profiles=@('High');Zooms=@('NormalGameplay');Displays=@('1920x1080');CloudCover=.65;Wetness=1})
$common=@{Capture=$true;Configuration='Release';ClientPath=$client;Warmups=8;Samples=32;RepeatCaptures=8;ContinueAfterMismatch=$true}
$results=@()
foreach($case in $cases){
 $name=$case.Name;$settings=@{};foreach($key in $case.Keys){if($key -ne 'Name'){$settings[$key]=$case[$key]}}
 $before=Join-Path $root "$name/before";$after=Join-Path $root "$name/after"
 & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot $baseline -OutputRoot $before
 & (Join-Path $repo 'build/Invoke-RtsReferenceQualification.ps1') @common @settings -RuntimeAssetRoot $candidate -OutputRoot $after -BaselineRoot $before
 & (Join-Path $repo 'build/Compare-VisualReferenceEvidence.ps1') -CurrentRoot $after -BaselineRoot $before -ComparePixels -ReportPath (Join-Path $root "$name/pixels.json")
 $results+=@{name=$name;before=$before;after=$after;pixels=(Join-Path $root "$name/pixels.json")}
}
@{schemaVersion=1;sourceSha=(& git -C $repo rev-parse HEAD);cohorts=$results;warmups=8;samples=32;repeatCaptures=8;structuralCommandsCompleted=$true;performanceAcceptance='pending-controlled-physical-measurements';humanArtApproval='pending'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'summary.json') -Encoding utf8NoBOM
