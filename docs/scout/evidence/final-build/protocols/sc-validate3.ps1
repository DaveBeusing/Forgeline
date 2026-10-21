Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath('..',$PSScriptRoot)
Set-Location $repo
$root=Join-Path $repo 'artifacts/sv3'
if(Test-Path -LiteralPath $root){throw 'Fresh typed input destination required.'}
$source=Join-Path $repo 'artifacts/sc3'
$q=Get-Content -LiteralPath (Join-Path $source 'qualification.json') -Raw | ConvertFrom-Json
if(-not $q.success -or -not $q.typedCompiledComparison -or -not $q.sourceLessIntegrity){throw 'Invalid native qualification input.'}
$records=@()
foreach($variant in @('baseline','first','second','reopened')){
 $target=Join-Path $root $variant
 New-Item -ItemType Directory -Path $target -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $source "$variant/runtime") -Destination $target -Recurse
 if($variant -ne 'baseline'){Copy-Item -LiteralPath (Join-Path $source "$variant/poses.json") -Destination (Join-Path $target 'poses.json')}
}
foreach($file in Get-ChildItem -LiteralPath $root -File -Recurse){
 $relative=[IO.Path]::GetRelativePath($root,$file.FullName)
 $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 if($hash -ne (Get-FileHash -LiteralPath (Join-Path $source $relative) -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Native typed input copy mismatch.'}
 $records+=@{path=$relative;sha256=$hash}
}
@{sourceSha=(& git rev-parse HEAD);nativeQualificationSource=$q.sourceSha;qualifiedRoot=$source;copiedInputs=$records} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'native-inputs.json') -Encoding utf8NoBOM
$env:FORGELINE_SCOUT_MASTER=$root
$env:FORGELINE_SCOUT_RUNTIME_SEED=Join-Path $repo 'assets/runtime'
try{
 & dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ScoutBlenderMasterTests ForgeLine.Presentation.Tests.ScoutBlenderPoseTests ForgeLine.Presentation.Tests.ScoutReferenceSceneTests ForgeLine.Presentation.Tests.OperationalBuildingReferenceSceneTests ForgeLine.Presentation.Tests.VisualReferenceSceneTests ForgeLine.Presentation.Tests.MechanicalMotionTests ForgeLine.Presentation.Tests.MechanicalMotionQualityTests *> artifacts/sc-integration-tests.log
 if($LASTEXITCODE -ne 0){throw 'Merged-source presentation/compiled/pose tests failed.'}
}finally{Remove-Item Env:FORGELINE_SCOUT_MASTER;Remove-Item Env:FORGELINE_SCOUT_RUNTIME_SEED}
 & dotnet tools/ForgeLine.AssetCompiler/bin/Release/net10.0/ForgeLine.AssetCompiler.dll --mode validate --source artifacts/absent-source --runtime artifacts/sv3/overlay/runtime --qualification-output artifacts/sc-overlay-source-less3.json *> artifacts/sc-overlay-source-less3.log
 if($LASTEXITCODE -ne 0){throw 'Complete merged overlay source-less qualification failed.'}
 & dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Game.Tests.VehicleQueuePhysicalTests *> artifacts/sc-incoming-queue-tests.log
 if($LASTEXITCODE -ne 0){throw 'Incoming queue regression failed.'}
$global:LASTEXITCODE=0
